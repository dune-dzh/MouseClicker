namespace MouseClicker;

internal sealed class ConfigEditorSyntaxHighlighter : IDisposable
{
    private static readonly Color CommentColor = Color.FromArgb(0, 128, 0);

    private readonly RichTextBox _editor;
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly Func<bool>? _isAssistPopupOpen;
    private bool _suppress;

    public ConfigEditorSyntaxHighlighter(RichTextBox editor, Func<bool>? isAssistPopupOpen = null)
    {
        _editor = editor;
        _isAssistPopupOpen = isAssistPopupOpen;

        _debounceTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            Apply();
        };

        _editor.TextChanged += Editor_TextChanged;
    }

    public void Dispose()
    {
        _editor.TextChanged -= Editor_TextChanged;
        _debounceTimer.Stop();
        _debounceTimer.Dispose();
    }

    public void LoadText(string text)
    {
        _debounceTimer.Stop();
        _suppress = true;
        try
        {
            _editor.Text = text;
        }
        finally
        {
            _suppress = false;
        }

        Apply();
    }

    public void ScheduleApply()
    {
        if (_suppress || (_isAssistPopupOpen?.Invoke() ?? false))
        {
            return;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void Apply()
    {
        if (_suppress || !_editor.IsHandleCreated || string.IsNullOrEmpty(_editor.Text))
        {
            return;
        }

        if (_isAssistPopupOpen?.Invoke() ?? false)
        {
            return;
        }

        _suppress = true;
        try
        {
            var selectionStart = _editor.SelectionStart;
            var selectionLength = _editor.SelectionLength;

            var text = _editor.Text;
            var index = 0;
            while (index < text.Length)
            {
                var lineStart = index;
                while (index < text.Length && text[index] is not '\n' and not '\r')
                {
                    index++;
                }

                var lineEnd = index;
                ApplyLineColors(text, lineStart, lineEnd);

                if (index < text.Length && text[index] == '\r')
                {
                    index++;
                }

                if (index < text.Length && text[index] == '\n')
                {
                    index++;
                }
            }

            _editor.Select(
                Math.Clamp(selectionStart, 0, _editor.TextLength),
                Math.Clamp(selectionLength, 0, Math.Max(0, _editor.TextLength - selectionStart)));
        }
        finally
        {
            _suppress = false;
        }
    }

    private void ApplyLineColors(string text, int lineStart, int lineEnd)
    {
        if (lineEnd <= lineStart)
        {
            return;
        }

        var hashIndex = lineStart;
        while (hashIndex < lineEnd && char.IsWhiteSpace(text[hashIndex]))
        {
            hashIndex++;
        }

        if (hashIndex < lineEnd && text[hashIndex] == '#')
        {
            var length = lineEnd - hashIndex;
            _editor.Select(hashIndex, length);
            _editor.SelectionColor = CommentColor;
            return;
        }

        _editor.Select(lineStart, lineEnd - lineStart);
        _editor.SelectionColor = _editor.ForeColor;
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        ScheduleApply();
    }
}
