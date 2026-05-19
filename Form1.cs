using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MouseClicker;

public partial class Form1 : Form
{
    private const int WheelDelta = 120;
    private const int ScrollStepIntervalMs = 50;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int VkF6 = 0x75;
    private const int VkF7 = 0x76;
    private const int VkF12 = 0x7B;
    private const string DefaultConfigFileName = "commands.txt";
    private const string SettingsFileName = "settings.json";

    private string _configPath;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _isRunning;
    private bool _isStopping;
    private PickerMode _pickerMode = PickerMode.None;
    private nint _keyboardHookHandle;
    private LowLevelKeyboardProc? _keyboardHookProc;
    private long _lastF6Tick;
    private long _lastF7Tick;
    private long _lastF12Tick;
    private long _lastHighlightTick;
    private int _highlightPending;
    private int _forceStopRequested;
    private int _stopUiNotified;
    private List<Step> _steps = [];
    private string _configLineEnding = Environment.NewLine;

    public Form1()
    {
        InitializeComponent();
        _configPath = Path.Combine(AppContext.BaseDirectory, DefaultConfigFileName);
        LoadLastConfigPathFromSettings();
        UpdateConfigPathLabel();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        EnsureDefaultConfigFileExists();
        LoadConfigIntoEditor();
        ReloadConfigAndRender();
        StartKeyboardHook();
        AppendLog("Press F6 to start/stop.");
        AppendLog("Emergency stop: F12.");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopRunner();
        StopKeyboardHook();
        base.OnFormClosing(e);
    }

    private void StartKeyboardHook()
    {
        _keyboardHookProc = KeyboardHookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleName = module?.ModuleName;
        var moduleHandle = moduleName is null ? nint.Zero : GetModuleHandle(moduleName);

        _keyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, moduleHandle, 0);
        if (_keyboardHookHandle == nint.Zero)
        {
            AppendLog("Failed to install global keyboard hook.");
        }
    }

    private void StopKeyboardHook()
    {
        if (_keyboardHookHandle == nint.Zero)
        {
            return;
        }

        _ = UnhookWindowsHookEx(_keyboardHookHandle);
        _keyboardHookHandle = nint.Zero;
    }

    private nint KeyboardHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == (nint)WmKeyDown || wParam == (nint)WmSysKeyDown))
        {
            var virtualKeyCode = Marshal.ReadInt32(lParam);
            if (virtualKeyCode == VkF6)
            {
                if (IsDebounced(ref _lastF6Tick, 180))
                {
                    return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
                }

                if (_isRunning || _isStopping || _runTask is { IsCompleted: false })
                {
                    RequestStop(false);
                }
                else
                {
                    BeginInvoke(ToggleRunState);
                }
            }
            else if (virtualKeyCode == VkF7 && _pickerMode != PickerMode.None)
            {
                if (IsDebounced(ref _lastF7Tick, 180))
                {
                    return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
                }

                BeginInvoke(CaptureCurrentCursorPosition);
            }
            else if (virtualKeyCode == VkF12)
            {
                if (IsDebounced(ref _lastF12Tick, 180))
                {
                    return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
                }

                RequestStop(true);
            }
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private static bool IsDebounced(ref long lastTick, int windowMs)
    {
        var now = Environment.TickCount64;
        var previous = Interlocked.Read(ref lastTick);
        if (now - previous < windowMs)
        {
            return true;
        }

        Interlocked.Exchange(ref lastTick, now);
        return false;
    }

    private void ToggleRunState()
    {
        RefreshRunStateFromTask();

        if (_isRunning || _isStopping)
        {
            StopRunner();
            AppendLog("Stopping...");
            return;
        }

        if (_runTask is { IsCompleted: false })
        {
            AppendLog("Runner is still stopping, please wait.");
            return;
        }

        if (!SaveEditorToConfigFile())
        {
            AppendLog("Stop the runner before saving changes.");
            return;
        }

        ReloadConfigAndRender();

        if (_steps.Count == 0)
        {
            AppendLog("No valid steps to run.");
            return;
        }

        _runCancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _forceStopRequested, 0);
        Interlocked.Exchange(ref _stopUiNotified, 0);
        _runTask = Task.Run(() => RunLoop(_runCancellation.Token));
        _isRunning = true;
        _isStopping = false;
        statusLabel.Text = "Status: Running";
        AppendLog("Started.");
    }

    private void StopRunner()
    {
        if (!_isRunning && !_isStopping)
        {
            return;
        }

        _runCancellation?.Cancel();
        Interlocked.Exchange(ref _forceStopRequested, 1);
        if (Interlocked.Exchange(ref _stopUiNotified, 1) == 0)
        {
            _isStopping = true;
            statusLabel.Text = "Status: Stopping...";
            AppendLog("Stopping...");
        }
    }

    private void RunLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (Interlocked.CompareExchange(ref _forceStopRequested, 0, 0) == 1)
                {
                    break;
                }

                for (var i = 0; i < _steps.Count; i++)
                {
                    if (token.IsCancellationRequested || Interlocked.CompareExchange(ref _forceStopRequested, 0, 0) == 1)
                    {
                        break;
                    }

                    var step = _steps[i];
                    HighlightStep(i);
                    ExecuteStep(step, token);
                }

                // Avoid starving global key handling in very tight no-sleep loops.
                Thread.Yield();
            }
        }
        catch (OperationCanceledException)
        {
            // expected when stopped
        }
        catch (Exception ex)
        {
            BeginInvoke(() => AppendLog($"Runtime error: {ex.Message}"));
        }
        finally
        {
            _isRunning = false;
            _isStopping = false;
            Interlocked.Exchange(ref _forceStopRequested, 0);
            Interlocked.Exchange(ref _stopUiNotified, 0);

            BeginInvoke(() =>
            {
                statusLabel.Text = "Status: Stopped";
                stepsListBox.SelectedIndex = -1;
                AppendLog("Stopped.");
            });
        }
    }

    private void RefreshRunStateFromTask()
    {
        if (_isStopping && _runTask is { IsCompleted: true })
        {
            _isRunning = false;
            _isStopping = false;
            statusLabel.Text = "Status: Stopped";
            Interlocked.Exchange(ref _stopUiNotified, 0);
        }
    }

    private static void ExecuteStep(Step step, CancellationToken token)
    {
        switch (step.Kind)
        {
            case StepKind.MoveTo:
                if (step.X is not null && step.Y is not null)
                {
                    token.ThrowIfCancellationRequested();
                    SetCursorPos(step.X.Value, step.Y.Value);
                }

                break;
            case StepKind.LeftClick:
                PerformClickWithRepeat(false, step, token);
                break;
            case StepKind.RightClick:
                PerformClickWithRepeat(true, step, token);
                break;
            case StepKind.Sleep:
                if (step.DurationMs is not null)
                {
                    Task.Delay(step.DurationMs.Value, token).Wait(token);
                }

                break;
            case StepKind.HoverNudge:
                token.ThrowIfCancellationRequested();
                PerformHoverNudge();
                break;
            case StepKind.ScrollDown:
                if (step.DurationMs is not null)
                {
                    PerformScrollForDuration(downward: true, step.DurationMs.Value, token);
                }

                break;
            case StepKind.ScrollUp:
                if (step.DurationMs is not null)
                {
                    PerformScrollForDuration(downward: false, step.DurationMs.Value, token);
                }

                break;
        }
    }

    private static void PerformClickWithRepeat(bool rightButton, Step step, CancellationToken token)
    {
        if (!step.ClickRepeatUntilStop && step.ClickRepeatDurationMs is null)
        {
            PerformMouseClick(rightButton);
            return;
        }

        var delayMs = step.ClickRepeatDelayMs ?? 0;
        if (step.ClickRepeatUntilStop)
        {
            RunClickRepeatUntilStop(rightButton, delayMs, token);
            return;
        }

        var durationMs = step.ClickRepeatDurationMs!.Value;
        if (durationMs <= 0)
        {
            PerformMouseClick(rightButton);
            return;
        }

        RunClickRepeatForDuration(rightButton, delayMs, durationMs, token);
    }

    private static void RunClickRepeatUntilStop(bool rightButton, int delayMs, CancellationToken token)
    {
        var intervalMs = EffectiveClickIntervalMs(delayMs);
        while (!token.IsCancellationRequested)
        {
            PerformMouseClick(rightButton);
            WaitForClickInterval(intervalMs, token);
        }
    }

    private static void RunClickRepeatForDuration(
        bool rightButton,
        int delayMs,
        int durationMs,
        CancellationToken token)
    {
        var intervalMs = EffectiveClickIntervalMs(delayMs);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < durationMs)
        {
            token.ThrowIfCancellationRequested();
            PerformMouseClick(rightButton);

            if (stopwatch.ElapsedMilliseconds >= durationMs)
            {
                break;
            }

            var remaining = durationMs - stopwatch.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                break;
            }

            WaitForClickInterval((int)Math.Min(intervalMs, remaining), token);
        }
    }

    /// <summary>
    /// Delay 0 means as fast as practical; use a 1 ms floor so we do not flood the OS input queue
    /// (queued clicks would keep arriving for seconds after the duration elapses).
    /// </summary>
    private static int EffectiveClickIntervalMs(int delayMs) => delayMs > 0 ? delayMs : 1;

    private static void WaitForClickInterval(int intervalMs, CancellationToken token)
    {
        if (intervalMs > 0)
        {
            Task.Delay(intervalMs, token).Wait(token);
        }
    }

    private static void PerformScrollForDuration(bool downward, int durationMs, CancellationToken token)
    {
        if (durationMs <= 0)
        {
            return;
        }

        // Negative delta moves view toward bottom of document; positive toward top (matches README / UX).
        var deltaPerTick = unchecked((uint)(downward ? -WheelDelta : WheelDelta));
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < durationMs)
        {
            token.ThrowIfCancellationRequested();
            SendMouseWheel(deltaPerTick);

            if (stopwatch.ElapsedMilliseconds >= durationMs)
            {
                break;
            }

            var remaining = durationMs - stopwatch.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                break;
            }

            Task.Delay((int)Math.Min(ScrollStepIntervalMs, remaining), token).Wait(token);
        }
    }

    private static void SendMouseWheel(uint mouseDataWheel)
    {
        var input = new INPUT
        {
            type = 0,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    mouseData = mouseDataWheel,
                    dwFlags = MouseEventFlags.Wheel
                }
            }
        };

        _ = SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void SendMouseMoveRelative(int dx, int dy)
    {
        var input = new INPUT
        {
            type = 0,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = MouseEventFlags.Move | MouseEventFlags.MoveNoCoalesce
                }
            }
        };

        _ = SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static void PerformHoverNudge()
    {
        if (!GetCursorPos(out var current))
        {
            return;
        }

        SendMouseMoveRelative(1, 0);
        SendMouseMoveRelative(-1, 0);
        SetCursorPos(current.X, current.Y);
    }

    private void ReloadConfigAndRender()
    {
        RenderSteps(ParseConfigLines(SplitConfigLines(configEditorTextBox.Text), out var errors), errors);
    }

    private void RenderSteps(List<Step> steps, List<string> errors)
    {
        _steps = steps;
        stepsListBox.Items.Clear();
        foreach (var step in _steps)
        {
            stepsListBox.Items.Add(step.DisplayText);
        }

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                AppendLog(error);
            }
        }
        else
        {
            AppendLog($"Loaded {_steps.Count} step(s).");
        }
    }

    private void saveConfigButton_Click(object sender, EventArgs e)
    {
        if (!SaveEditorToConfigFile())
        {
            AppendLog("Stop the runner before saving changes.");
            return;
        }

        ReloadConfigAndRender();
        AppendLog("Config saved from editor.");
    }

    private void reloadConfigButton_Click(object sender, EventArgs e)
    {
        if (_isRunning)
        {
            AppendLog("Stop the runner before reloading file.");
            return;
        }

        LoadConfigIntoEditor();
        ReloadConfigAndRender();
        AppendLog("Editor reloaded from file.");
    }

    private void loadConfigButton_Click(object sender, EventArgs e)
    {
        if (_isRunning)
        {
            AppendLog("Stop the runner before loading another config file.");
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Load config file",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (!string.IsNullOrEmpty(_configPath))
        {
            var initialDir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
            {
                dialog.InitialDirectory = initialDir;
            }

            dialog.FileName = Path.GetFileName(_configPath);
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SetConfigPath(dialog.FileName);
        LoadConfigIntoEditor();
        ReloadConfigAndRender();
        AppendLog($"Loaded config file: {_configPath}");
    }

    private void pickMoveToButton_Click(object sender, EventArgs e)
    {
        if (_isRunning)
        {
            AppendLog("Stop the runner before using position picker.");
            return;
        }

        _pickerMode = PickerMode.MoveOnly;
        UpdatePickerHint();
        AppendLog("Position picker enabled for MoveTo. Move mouse to target and press F7.");
    }

    private void pickMoveAndLeftClickButton_Click(object sender, EventArgs e)
    {
        if (_isRunning)
        {
            AppendLog("Stop the runner before using position picker.");
            return;
        }

        _pickerMode = PickerMode.MoveAndLeftClick;
        UpdatePickerHint();
        AppendLog("Position picker enabled for MoveTo + LeftClick. Move mouse to target and press F7.");
    }

    private void HighlightStep(int index)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastHighlightTick);
        if (now - last < 60)
        {
            return;
        }

        if (Interlocked.Exchange(ref _highlightPending, 1) == 1)
        {
            return;
        }

        BeginInvoke(() =>
        {
            try
            {
                if (stepsListBox.SelectedIndex != index)
                {
                    stepsListBox.SelectedIndex = index;
                }
            }
            finally
            {
                Interlocked.Exchange(ref _lastHighlightTick, Environment.TickCount64);
                Interlocked.Exchange(ref _highlightPending, 0);
            }
        });
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (logTextBox.TextLength > 0)
        {
            logTextBox.AppendText(Environment.NewLine);
        }

        logTextBox.AppendText(line);
    }

    private void LoadConfigIntoEditor()
    {
        if (!File.Exists(_configPath))
        {
            configEditorTextBox.Text = string.Empty;
            return;
        }

        configEditorTextBox.Text = ReadConfigFileText(_configPath, out _configLineEnding);
    }

    private bool SaveEditorToConfigFile()
    {
        if (_isRunning)
        {
            return false;
        }

        var text = NormalizeConfigLineEndings(configEditorTextBox.Text, _configLineEnding);
        File.WriteAllText(_configPath, text, Utf8WithoutBom);
        SaveLastConfigPathToSettings();
        return true;
    }

    private void CaptureCurrentCursorPosition()
    {
        if (_pickerMode == PickerMode.None)
        {
            return;
        }

        var position = Cursor.Position;
        var moveToLine = $"MoveTo {position.X}x{position.Y}";
        AppendEditorLine(moveToLine);

        if (_pickerMode == PickerMode.MoveAndLeftClick)
        {
            AppendEditorLine("LeftClick");
            AppendLog($"Captured position: {moveToLine} + LeftClick");
        }
        else
        {
            AppendLog($"Captured position: {moveToLine}");
        }

        _pickerMode = PickerMode.None;
        UpdatePickerHint();
        ReloadConfigAndRender();
    }

    private void UpdatePickerHint()
    {
        pickerHintLabel.Text = _pickerMode switch
        {
            PickerMode.MoveOnly => "Pick mode ON: MoveTo (press F7)",
            PickerMode.MoveAndLeftClick => "Pick mode ON: Move+LeftClick (press F7)",
            _ => "Picker idle (use Pick button)"
        };
    }

    private void EmergencyStop()
    {
        if (!_isRunning && !_isStopping)
        {
            AppendLog("Emergency stop pressed, but runner is idle.");
            return;
        }

        AppendLog("Emergency stop requested (F12).");
        StopRunner();
    }

    private void RequestStop(bool isEmergency)
    {
        Interlocked.Exchange(ref _forceStopRequested, 1);
        _runCancellation?.Cancel();

        BeginInvoke(() =>
        {
            if (isEmergency)
            {
                EmergencyStop();
                return;
            }

            StopRunner();
        });
    }

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private static string ReadConfigFileText(string path, out string lineEnding)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        text = StripUtf8Bom(text);
        lineEnding = DetectLineEnding(text);
        return text;
    }

    private static string StripUtf8Bom(string text) =>
        text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;

    private static string DetectLineEnding(string text)
    {
        if (text.Contains("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        if (text.Contains('\n'))
        {
            return "\n";
        }

        if (text.Contains('\r'))
        {
            return "\r";
        }

        return Environment.NewLine;
    }

    private static string[] SplitConfigLines(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        normalized = StripUtf8Bom(normalized);
        return normalized.Split('\n');
    }

    private static string NormalizeConfigLineEndings(string content, string lineEnding) =>
        string.Join(lineEnding, SplitConfigLines(content));

    private void AppendEditorLine(string line)
    {
        if (configEditorTextBox.TextLength == 0)
        {
            configEditorTextBox.Text = line;
            return;
        }

        configEditorTextBox.AppendText(_configLineEnding + line);
    }

    private static List<Step> ParseConfigLines(string[] lines, out List<string> errors)
    {
        errors = [];
        var steps = new List<Step>();

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim('\r', '\n', ' ', '\t');
            var lineNumber = i + 1;

            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (raw.StartsWith('#'))
            {
                continue;
            }

            if (raw.StartsWith("MoveTo", StringComparison.OrdinalIgnoreCase))
            {
                var part = raw["MoveTo".Length..].Trim();
                var coords = part.Split('x', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (coords.Length == 2 &&
                    int.TryParse(coords[0], out var x) &&
                    int.TryParse(coords[1], out var y))
                {
                    steps.Add(Step.MoveTo(x, y));
                }
                else
                {
                    errors.Add($"Line {lineNumber}: invalid MoveTo format. Expected: MoveTo 1000x1111");
                }

                continue;
            }

            if (TryParseClickCommand(raw, rightButton: false, lineNumber, steps, errors))
            {
                continue;
            }

            if (TryParseClickCommand(raw, rightButton: true, lineNumber, steps, errors))
            {
                continue;
            }

            if (raw.StartsWith("Sleep", StringComparison.OrdinalIgnoreCase))
            {
                var part = raw["Sleep".Length..].Trim();
                if (int.TryParse(part, out var ms) && ms >= 0)
                {
                    steps.Add(Step.Sleep(ms));
                }
                else
                {
                    errors.Add($"Line {lineNumber}: invalid Sleep value. Expected: Sleep 1000");
                }

                continue;
            }

            if (raw.Equals("HoverNudge", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(Step.HoverNudge());
                continue;
            }

            if (raw.StartsWith("ScrollDown", StringComparison.OrdinalIgnoreCase))
            {
                var part = raw["ScrollDown".Length..].Trim();
                if (int.TryParse(part, out var scrollMs) && scrollMs >= 0)
                {
                    steps.Add(Step.ScrollDown(scrollMs));
                }
                else
                {
                    errors.Add($"Line {lineNumber}: invalid ScrollDown duration. Expected: ScrollDown 500");
                }

                continue;
            }

            if (raw.StartsWith("ScrollUp", StringComparison.OrdinalIgnoreCase))
            {
                var part = raw["ScrollUp".Length..].Trim();
                if (int.TryParse(part, out var scrollMs2) && scrollMs2 >= 0)
                {
                    steps.Add(Step.ScrollUp(scrollMs2));
                }
                else
                {
                    errors.Add($"Line {lineNumber}: invalid ScrollUp duration. Expected: ScrollUp 500");
                }

                continue;
            }

            errors.Add($"Line {lineNumber}: unknown command '{raw}'.");
        }

        return steps;
    }

    private static bool TryParseClickCommand(
        string raw,
        bool rightButton,
        int lineNumber,
        List<Step> steps,
        List<string> errors)
    {
        var command = rightButton ? "RightClick" : "LeftClick";
        if (!raw.StartsWith(command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = raw[command.Length..].Trim();
        if (remainder.Length == 0)
        {
            steps.Add(rightButton ? Step.RightClick() : Step.LeftClick());
            return true;
        }

        if (!remainder.StartsWith("Repeat", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Line {lineNumber}: invalid {command} syntax. Expected: {command} or {command} Repeat [delayMs] [durationMs]");
            return true;
        }

        remainder = remainder["Repeat".Length..].Trim();
        if (remainder.Length == 0)
        {
            steps.Add(rightButton ? Step.RightClickRepeatUntilStop(0) : Step.LeftClickRepeatUntilStop(0));
            return true;
        }

        var parts = remainder.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out var delayMs) &&
            int.TryParse(parts[1], out var durationMs) &&
            delayMs >= 0 &&
            durationMs > 0)
        {
            steps.Add(rightButton
                ? Step.RightClickRepeat(delayMs, durationMs)
                : Step.LeftClickRepeat(delayMs, durationMs));
            return true;
        }

        steps.Add(rightButton ? Step.RightClick() : Step.LeftClick());
        return true;
    }

    private void EnsureDefaultConfigFileExists()
    {
        var defaultPath = Path.Combine(AppContext.BaseDirectory, DefaultConfigFileName);
        if (!string.Equals(_configPath, defaultPath, StringComparison.OrdinalIgnoreCase) || File.Exists(_configPath))
        {
            return;
        }

        var sample = """
                     # One command per line
                     MoveTo 1000x1111
                     HoverNudge
                     LeftClick
                     LeftClick Repeat 0 10000
                     ScrollDown 250
                     ScrollUp 250
                     Sleep 1000
                     MoveTo 800x900
                     RightClick
                     """;
        File.WriteAllText(defaultPath, sample, Utf8WithoutBom);
        _configLineEnding = DetectLineEnding(sample);
        AppendLog("Created sample commands.txt file.");
    }

    private void SetConfigPath(string path)
    {
        _configPath = path;
        UpdateConfigPathLabel();
        SaveLastConfigPathToSettings();
    }

    private void UpdateConfigPathLabel()
    {
        filePathLabel.Text = $"Config: {_configPath}";
    }

    private string SettingsPath => Path.Combine(AppContext.BaseDirectory, SettingsFileName);

    private void LoadLastConfigPathFromSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            if (!string.IsNullOrWhiteSpace(settings?.LastConfigPath) && File.Exists(settings.LastConfigPath))
            {
                _configPath = settings.LastConfigPath;
            }
        }
        catch
        {
            // ignore corrupt settings
        }
    }

    private void SaveLastConfigPathToSettings()
    {
        try
        {
            var settings = new AppSettings { LastConfigPath = _configPath };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json, Utf8WithoutBom);
        }
        catch (Exception ex)
        {
            AppendLog($"Could not save settings: {ex.Message}");
        }
    }

    private static void PerformMouseClick(bool rightButton)
    {
        var downFlag = rightButton ? MouseEventFlags.RightDown : MouseEventFlags.LeftDown;
        var upFlag = rightButton ? MouseEventFlags.RightUp : MouseEventFlags.LeftUp;

        var inputs = new INPUT[2];
        inputs[0] = new INPUT
        {
            type = 0,
            U = new InputUnion
            {
                mi = new MOUSEINPUT { dwFlags = downFlag }
            }
        };
        inputs[1] = new INPUT
        {
            type = 0,
            U = new InputUnion
            {
                mi = new MOUSEINPUT { dwFlags = upFlag }
            }
        };

        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private enum StepKind
    {
        MoveTo,
        LeftClick,
        RightClick,
        Sleep,
        HoverNudge,
        ScrollDown,
        ScrollUp
    }

    private enum PickerMode
    {
        None,
        MoveOnly,
        MoveAndLeftClick
    }

    private sealed record Step(
        StepKind Kind,
        int? X,
        int? Y,
        int? DurationMs,
        int? ClickRepeatDelayMs,
        int? ClickRepeatDurationMs,
        bool ClickRepeatUntilStop,
        string DisplayText)
    {
        public static Step MoveTo(int x, int y) =>
            new(StepKind.MoveTo, x, y, null, null, null, false, $"MoveTo {x}x{y}");

        public static Step LeftClick() =>
            new(StepKind.LeftClick, null, null, null, null, null, false, "LeftClick");

        public static Step RightClick() =>
            new(StepKind.RightClick, null, null, null, null, null, false, "RightClick");

        public static Step LeftClickRepeatUntilStop(int delayMs) =>
            new(StepKind.LeftClick, null, null, null, delayMs, null, true,
                delayMs == 0 ? "LeftClick Repeat" : $"LeftClick Repeat {delayMs}");

        public static Step RightClickRepeatUntilStop(int delayMs) =>
            new(StepKind.RightClick, null, null, null, delayMs, null, true,
                delayMs == 0 ? "RightClick Repeat" : $"RightClick Repeat {delayMs}");

        public static Step LeftClickRepeat(int delayMs, int durationMs) =>
            new(StepKind.LeftClick, null, null, null, delayMs, durationMs, false, $"LeftClick Repeat {delayMs} {durationMs}");

        public static Step RightClickRepeat(int delayMs, int durationMs) =>
            new(StepKind.RightClick, null, null, null, delayMs, durationMs, false, $"RightClick Repeat {delayMs} {durationMs}");

        public static Step Sleep(int ms) =>
            new(StepKind.Sleep, null, null, ms, null, null, false, $"Sleep {ms}");

        public static Step HoverNudge() =>
            new(StepKind.HoverNudge, null, null, null, null, null, false, "HoverNudge");

        public static Step ScrollDown(int ms) =>
            new(StepKind.ScrollDown, null, null, ms, null, null, false, $"ScrollDown {ms}");

        public static Step ScrollUp(int ms) =>
            new(StepKind.ScrollUp, null, null, ms, null, null, false, $"ScrollUp {ms}");
    }

    private sealed class AppSettings
    {
        public string? LastConfigPath { get; set; }
    }

    [Flags]
    private enum MouseEventFlags : uint
    {
        Move = 0x0001,
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010,
        MoveNoCoalesce = 0x2000,
        Wheel = 0x0800,
        Absolute = 0x8000,
        VirtualDesk = 0x4000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public MouseEventFlags dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);
}
