namespace MouseClicker;

internal sealed class ConfigEditorAssist : IDisposable
{
    private readonly TextBoxBase _editor;
    private readonly Form _owner;
    private readonly Panel _popup;
    private readonly ListBox _list;
    private readonly Font _listFont;
    private bool _suppressTextChange;
    private int _replaceStart;
    private int _replaceEnd;
    private readonly Action? _afterTextChanged;

    public ConfigEditorAssist(TextBoxBase editor, Form owner, Action? afterTextChanged = null)
    {
        _editor = editor;
        _owner = owner;
        _afterTextChanged = afterTextChanged;
        _listFont = new Font("Segoe UI", 9F);

        _list = new ListBox
        {
            BorderStyle = BorderStyle.None,
            Font = _listFont,
            IntegralHeight = false,
            ItemHeight = 18,
            TabStop = false
        };
        _list.MouseDown += (_, e) => OnListMouseDown(e);
        _list.Click += (_, _) => ApplySelected();
        _list.DoubleClick += (_, _) => ApplySelected();

        _popup = new Panel
        {
            Visible = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window
        };
        _popup.Controls.Add(_list);
        _list.Dock = DockStyle.Fill;

        _owner.Controls.Add(_popup);
        _popup.BringToFront();

        _editor.TextChanged += Editor_TextChanged;
        _editor.KeyDown += Editor_KeyDown;
        _editor.LostFocus += (_, _) => _owner.BeginInvoke(HideIfFocusLeft);
        _editor.MouseWheel += (_, _) => Hide();
        _owner.Deactivate += (_, _) => Hide();
        _owner.MouseDown += Owner_MouseDown;
    }

    public bool IsOpen => _popup.Visible;

    public void Dispose()
    {
        _editor.TextChanged -= Editor_TextChanged;
        _editor.KeyDown -= Editor_KeyDown;
        _owner.MouseDown -= Owner_MouseDown;
        _popup.Dispose();
        _listFont.Dispose();
    }

    private void Owner_MouseDown(object? sender, MouseEventArgs e)
    {
        if (!IsOpen)
        {
            return;
        }

        if (IsPointInsidePopup(e.Location) || _editor.Bounds.Contains(e.Location))
        {
            return;
        }

        Hide();
    }

    private void OnListMouseDown(MouseEventArgs e)
    {
        var index = _list.IndexFromPoint(e.Location);
        if (index >= 0 && index < _list.Items.Count)
        {
            _list.SelectedIndex = index;
        }
    }

    private void HideIfFocusLeft()
    {
        if (!IsOpen)
        {
            return;
        }

        if (_editor.Focused)
        {
            return;
        }

        var cursor = _owner.PointToClient(Cursor.Position);
        if (IsPointInsidePopup(cursor) || _editor.Bounds.Contains(cursor))
        {
            return;
        }

        Hide();
    }

    private bool IsPointInsidePopup(Point ownerClientPoint) =>
        _popup.Visible && _popup.Bounds.Contains(ownerClientPoint);

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_suppressTextChange)
        {
            return;
        }

        UpdateSuggestions();
    }

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsOpen)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Down:
                MoveSelection(1);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.Up:
                MoveSelection(-1);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
            case Keys.Tab:
                ApplySelected();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.Escape:
                Hide();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.PageDown:
            case Keys.PageUp:
            case Keys.Home:
            case Keys.End:
                Hide();
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_list.Items.Count == 0)
        {
            return;
        }

        var next = _list.SelectedIndex + delta;
        if (next < 0)
        {
            next = _list.Items.Count - 1;
        }
        else if (next >= _list.Items.Count)
        {
            next = 0;
        }

        _list.SelectedIndex = next;
    }

    private void UpdateSuggestions()
    {
        if (!TryGetEditContext(out var context))
        {
            Hide();
            return;
        }

        _replaceStart = context.ReplaceStart;
        _replaceEnd = context.ReplaceEnd;

        IEnumerable<ConfigCommandInfo> matches = context.IsPalette
            ? ConfigCommandCatalog.FilterForPalette(context.Query)
            : ConfigCommandCatalog.FilterForAutocomplete(context.Query);

        if (!context.IsPalette && string.IsNullOrEmpty(context.Query))
        {
            Hide();
            return;
        }

        var items = matches.ToList();
        if (items.Count == 0)
        {
            Hide();
            return;
        }

        var reselect = IsOpen && _list.SelectedIndex >= 0 && _list.SelectedIndex < items.Count
            ? Math.Min(_list.SelectedIndex, items.Count - 1)
            : 0;

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in items)
        {
            _list.Items.Add(item);
        }

        _list.DisplayMember = nameof(ConfigCommandInfo.ListDisplay);
        _list.SelectedIndex = reselect;
        _list.EndUpdate();

        ShowPopup();
    }

    private void ShowPopup()
    {
        var lineCount = Math.Min(_list.Items.Count, 8);
        var popupHeight = 4 + lineCount * _list.ItemHeight;
        var popupWidth = Math.Min(_editor.Width - 4, 368);

        _popup.Size = new Size(popupWidth, popupHeight);

        var caretPoint = GetCaretPoint();
        var clientPoint = _owner.PointToClient(_editor.PointToScreen(caretPoint));

        var left = clientPoint.X;
        var top = clientPoint.Y + _list.ItemHeight + 4;

        if (left + popupWidth > _owner.ClientSize.Width - 4)
        {
            left = Math.Max(4, _owner.ClientSize.Width - popupWidth - 4);
        }

        if (top + popupHeight > _owner.ClientSize.Height - 4)
        {
            top = Math.Max(4, clientPoint.Y - popupHeight - 4);
        }

        _popup.Location = new Point(left, top);
        if (!_popup.Visible)
        {
            _popup.Visible = true;
        }

        _popup.BringToFront();
    }

    private Point GetCaretPoint()
    {
        if (_editor.TextLength == 0)
        {
            return new Point(4, 4);
        }

        var index = Math.Clamp(_editor.SelectionStart, 0, _editor.TextLength - 1);
        try
        {
            return _editor.GetPositionFromCharIndex(index);
        }
        catch
        {
            return new Point(4, 4);
        }
    }

    private bool TryGetEditContext(out EditContext context)
    {
        context = default!;
        var text = _editor.Text;
        var caret = _editor.SelectionStart;

        if (string.IsNullOrEmpty(text) || caret < 0)
        {
            return false;
        }

        var lineStart = GetLineStart(text, caret);
        var lineToCaret = text[lineStart..caret];

        if (lineToCaret.TrimStart().StartsWith('#'))
        {
            return false;
        }

        var leadingWhitespace = lineToCaret.Length - lineToCaret.TrimStart().Length;
        var tokenStart = lineStart + leadingWhitespace;
        var token = text[tokenStart..caret];

        if (token.StartsWith('/'))
        {
            context = new EditContext(
                isPalette: true,
                query: token[1..],
                replaceStart: tokenStart,
                replaceEnd: caret);
            return true;
        }

        if (token.Contains(' ') || token.Contains('\t'))
        {
            return false;
        }

        context = new EditContext(
            isPalette: false,
            query: token,
            replaceStart: tokenStart,
            replaceEnd: caret);
        return true;
    }

    private static int GetLineStart(string text, int caret)
    {
        var index = Math.Min(caret, text.Length) - 1;
        while (index >= 0 && text[index] != '\n' && text[index] != '\r')
        {
            index--;
        }

        return index + 1;
    }

    private void ApplySelected()
    {
        if (_list.SelectedItem is not ConfigCommandInfo command)
        {
            Hide();
            return;
        }

        var text = _editor.Text;
        var before = text[.._replaceStart];
        var after = text[_replaceEnd..];
        var insert = command.InsertText;

        _suppressTextChange = true;
        try
        {
            _editor.Text = before + insert + after;
            _editor.SelectionStart = before.Length + insert.Length;
            _editor.SelectionLength = 0;
        }
        finally
        {
            _suppressTextChange = false;
        }

        Hide();
        _editor.Focus();
        _afterTextChanged?.Invoke();
    }

    private void Hide()
    {
        _popup.Visible = false;
        _list.Items.Clear();
    }

    private readonly struct EditContext(bool isPalette, string query, int replaceStart, int replaceEnd)
    {
        public bool IsPalette { get; } = isPalette;
        public string Query { get; } = query;
        public int ReplaceStart { get; } = replaceStart;
        public int ReplaceEnd { get; } = replaceEnd;
    }
}
