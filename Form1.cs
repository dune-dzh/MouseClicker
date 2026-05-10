using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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

    private readonly string _configPath;
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

    public Form1()
    {
        InitializeComponent();
        _configPath = Path.Combine(AppContext.BaseDirectory, "commands.txt");
        filePathLabel.Text = $"Config: {_configPath}";
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        EnsureConfigFileExists();
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
                PerformMouseClick(false);
                break;
            case StepKind.RightClick:
                PerformMouseClick(true);
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

    private static void PerformScrollForDuration(bool downward, int durationMs, CancellationToken token)
    {
        if (durationMs <= 0)
        {
            return;
        }

        // Negative delta moves view toward bottom of document; positive toward top (matches README / UX).
        var deltaPerTick = unchecked((uint)(downward ? -WheelDelta : WheelDelta));
        var end = Environment.TickCount64 + durationMs;

        while (Environment.TickCount64 < end)
        {
            token.ThrowIfCancellationRequested();
            SendMouseWheel(deltaPerTick);

            var remaining = (int)(end - Environment.TickCount64);
            if (remaining <= 0)
            {
                break;
            }

            Task.Delay(Math.Min(ScrollStepIntervalMs, remaining), token).Wait(token);
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
        _steps = ParseConfig(_configPath, out var errors);
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

        configEditorTextBox.Text = File.ReadAllText(_configPath, Encoding.UTF8);
    }

    private bool SaveEditorToConfigFile()
    {
        if (_isRunning)
        {
            return false;
        }

        File.WriteAllText(_configPath, configEditorTextBox.Text, Encoding.UTF8);
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
        if (string.IsNullOrWhiteSpace(configEditorTextBox.Text))
        {
            configEditorTextBox.Text = moveToLine;
        }
        else
        {
            configEditorTextBox.AppendText($"{Environment.NewLine}{moveToLine}");
        }

        if (_pickerMode == PickerMode.MoveAndLeftClick)
        {
            configEditorTextBox.AppendText($"{Environment.NewLine}LeftClick");
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

    private static List<Step> ParseConfig(string path, out List<string> errors)
    {
        errors = [];
        var steps = new List<Step>();

        if (!File.Exists(path))
        {
            errors.Add("Config file not found.");
            return steps;
        }

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            var lineNumber = i + 1;

            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (raw.StartsWith("#", StringComparison.Ordinal))
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

            if (raw.Equals("RightClick", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(Step.RightClick());
                continue;
            }

            if (raw.Equals("LeftClick", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(Step.LeftClick());
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

    private void EnsureConfigFileExists()
    {
        if (File.Exists(_configPath))
        {
            return;
        }

        var sample = """
                     # One command per line
                     MoveTo 1000x1111
                     HoverNudge
                     LeftClick
                     ScrollDown 250
                     ScrollUp 250
                     Sleep 1000
                     MoveTo 800x900
                     RightClick
                     """;
        File.WriteAllText(_configPath, sample, Encoding.UTF8);
        AppendLog("Created sample commands.txt file.");
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

    private sealed record Step(StepKind Kind, int? X, int? Y, int? DurationMs, string DisplayText)
    {
        public static Step MoveTo(int x, int y) => new(StepKind.MoveTo, x, y, null, $"MoveTo {x}x{y}");
        public static Step LeftClick() => new(StepKind.LeftClick, null, null, null, "LeftClick");
        public static Step RightClick() => new(StepKind.RightClick, null, null, null, "RightClick");
        public static Step Sleep(int ms) => new(StepKind.Sleep, null, null, ms, $"Sleep {ms}");
        public static Step HoverNudge() => new(StepKind.HoverNudge, null, null, null, "HoverNudge");
        public static Step ScrollDown(int ms) => new(StepKind.ScrollDown, null, null, ms, $"ScrollDown {ms}");
        public static Step ScrollUp(int ms) => new(StepKind.ScrollUp, null, null, ms, $"ScrollUp {ms}");
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
