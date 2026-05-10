# MouseClicker

Windows desktop app that runs a simple script to move the cursor, click, scroll, and wait. Commands are read from a text file next to the executable. **F6**, **F7**, and **F12** use a global keyboard hook so they work even when this window is not focused.

## Requirements

- Windows
- [.NET Desktop Runtime](https://dotnet.microsoft.com/download/dotnet) matching the app’s target (this project targets **.NET 10** / `net10.0-windows`)

## Build and run (development)

From the `MouseClicker` folder:

```bash
dotnet build
dotnet run
```

## Publish (copy to another PC)

Framework-dependent (smaller; target PC needs Desktop Runtime installed):

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

Output folder:

`bin/Release/net10.0-windows/win-x64/publish/`

Copy the entire `publish` folder. Close any running `MouseClicker.exe` before publishing, or the build may fail because files are locked.

Self-contained publish may be possible if your SDK can restore the matching runtime packs; if restore fails, use framework-dependent + install runtime on the target machine.

Publishing from this project copies **`README.md`** into the **`publish/`** folder beside the executable.

## Configuration file

On first run, the app creates **`commands.txt`** in the same folder as the executable (typically under `publish/` after publish).

- One command per line (case-insensitive).
- Empty lines and lines starting with `#` are ignored.

### Commands

| Command | Description |
|--------|-------------|
| `MoveTo 1000x1111` | Move cursor to screen coordinates (**instant** `SetCursorPos`; no animated path). |
| `HoverNudge` | Tiny relative mouse move from the **current** position, then restore—helps UIs that need a real movement for hover detection. Pair with `MoveTo` when needed: `MoveTo …` then `HoverNudge`. |
| `LeftClick` | Left mouse button click. |
| `RightClick` | Right mouse button click. |
| `Sleep 1000` | Pause for 1000 milliseconds. |
| `ScrollDown 500` | For about **500 ms**, send repeated vertical wheel ticks so the **document/view scrolls down** (toward later content). |
| `ScrollUp 500` | For about **500 ms**, repeated ticks so the **document/view scrolls up** (toward earlier content). |

Scrolling is implemented by sending wheel events about every **50 ms** until the requested duration ends (cancellable via **F6** / **F12**).

Example:

```text
# Sample
MoveTo 500x400
HoverNudge
LeftClick
Sleep 500
ScrollDown 250
ScrollUp 250
```

### Running behavior

- **F6** starts only after first press; idle on launch.
- On **start**, the app saves the in-editor script to **`commands.txt`**, then parses and runs it.
- Steps run **in a loop** until you stop with **F6** or **F12** (whole script repeats).
- Stop is cooperative: execution stops between steps or during waits/scroll, not necessarily mid-application logic in other programs.

## Hotkeys (global)

| Key | Action |
|-----|--------|
| **F6** | Start (saves editor → reloads `commands.txt` from disk) or request stop. |
| **F7** | After you click **Pick MoveTo** or **Pick Move+Click** in the UI, press **F7** to capture the cursor and append lines to the editor. |
| **F12** | Emergency stop (same cancel idea as stop on **F6**). |

In the app window, the status line shows: `Hotkeys: F6 Start/Stop | F7 Pick | F12 Kill`.

## GUI

- Edit `commands.txt` in the app; **Save Config** / **Reload File**.
- List of parsed steps and a timestamped log (status path to `commands.txt` is shown).
- **Pick MoveTo** / **Pick Move+Click** enables **F7** capture (insert `MoveTo xxy` and optionally `LeftClick`).

## Notes

- Input uses Windows **`SendInput`** and **`SetCursorPos`**. Synthetic input may differ from hardware; games (e.g. some clients with anti-cheat) can ignore or treat it specially—**HoverNudge** can help hover-only quirks but is not universal.
- **ScrollDown / ScrollUp** follow “document moves down/up” semantics; a few apps may invert wheel meaning—adjust script or durations if needed.

## License

Use and modify as you see fit for your own purposes.
# MouseClicker
