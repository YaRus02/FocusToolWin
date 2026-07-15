![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Version](https://img.shields.io/badge/version-3.2.1-2468D8)
![License](https://img.shields.io/badge/license-MIT-green)

# FocusTool

FocusTool is a compact Windows utility for presentations, online lessons, screen recording, and interface walkthroughs. It adds pointers, focus effects, drawing, visual masks, timers, screen boards, magnification, and capture tools over the desktop.

Its main principle is fast access to frequently used actions with minimal visual noise. Modes are available through global shortcuts, the system tray, or a compact overlay toolbar.

Documentation is current for version **3.2.1**.

> Development note: AI assistance was used while building FocusTool as an engineering aid. Architecture, integration, behavior checks, and final changes were reviewed and controlled manually.

## Demo

[![Watch the demo](Assets/FocusToolDemo.eng.png)](https://youtu.be/gpfk7p2Xd6s)

## Quick start

1. Extract a release and run `FocusTool.exe`.
2. Open the toolbar with `Ctrl+Alt+G`, or use the notification-area icon.
3. Hold `Alt+Z` for a temporary Laser or `Alt+X` for Cursor Highlight.
4. Press `Ctrl+Alt+A` for Draw, or hold `Alt+A` for temporary Draw.
5. Press `Esc` to cancel the current editor or leave an interactive mode.

Every shortcut can be changed or disabled under `Settings → Shortcuts`.

Full documentation:

- [Complete English guide](publish/FocusTool-Manual-EN.html)
- [Полное руководство на русском](publish/FocusTool-Manual-RU.html)

## Main features

### Pointer and focus

- **Laser Pointer** — a colored point with a smooth fading trail, glow, and Always/Hold modes.
- **Cursor Highlight** — a stable ring with independent color, radius, and thickness.
- **Click Pulse** — brief visual feedback at the click position.
- **Spotlight** — dims the screen outside a circular cursor area.
- **Magnifier** — a cursor-following lens; combined with Spotlight, it creates a magnified clear area over a dimmed screen.
- **Region Spotlight** — one or more rectangular focus regions that can be moved, resized, and nudged precisely with the arrow keys.
- **Pinned Lens** — separate magnified screen regions with zoom and Freeze/Unfreeze.

### Drawing and annotations

- Arrow, Line, Rectangle, Ellipse, Pencil, Highlighter, Eraser, Text, Move, and numbered Step markers.
- Pencil and Highlighter smoothing: Off, Balanced, or Strong.
- Highlighter uses a rectangular nib; briefly holding the endpoint straightens the stroke.
- `Shift` makes Rectangle a square, Ellipse a circle, and snaps Line in 45-degree increments.
- Double-click edits text or a supported shape.
- `Ctrl+V` pastes text or an image as a movable object with preserved aspect ratio.
- Object Eraser removes complete annotations and leaves pasted images untouched.
- Undo/Redo, multi-object selection, per-tool thickness memory, and five color slots.
- **Fading Annotations** removes temporary explanations automatically.
- **Push-to-annotate** starts when the complete assigned shortcut is pressed and stays active while any of its keys remains held. The initial key does not select a matching tool; it can do so after being released and pressed again. An active stroke always completes before exit.

A useful live-presentation combination is to enable Fading Annotations once, press `Alt+A`, release `A` while keeping `Alt` held, select a tool, and draw. Releasing `Alt` returns control to the source application while the explanation disappears on its own.

### Masks, boards, and capture

- **Region Mask** — a visual cover for selected regions with Solid, Stripes, and `HIDE` styles. Masks can be moved, resized, and keep their individual appearance.
- **Screen Board** — a static snapshot of the current monitor used as an annotation background.
- **Black Board / White Board** — a clean background for explanation or electronic-board use.
- **Screenshot** — captures the current monitor as PNG, copies it to the clipboard, and saves it under `Pictures\FocusTool`.
- **Region Screenshot** — an adjustable frame with exact dimensions; arrows nudge it by 1 pixel and `Shift+arrow` by 10.
- **Capture Stage** — a separate borderless presentation of a selected window together with FocusTool overlays for window capture or sharing.

Region Mask is intended as a visual cover and does not modify the source application's data. The contents of a third-party recording depend on the capture method selected in that recorder.

### Timers

- Multiple independent floating timers.
- Countdown, Stopwatch, Clock, and Until time modes.
- Label, theme, 12/24-hour format, progress bar, scale, opacity, and finish blinking.
- Fast selected-timer control: `Space` starts/pauses, `R` resets, `Tab` cycles modes, and arrow keys adjust time.
- Open timers are session state and are not restored after restarting the application.

## Default shortcuts

### Global actions

| Action | Shortcut |
|---|---|
| Laser: toggle / hold | `Ctrl+Alt+Z` / `Alt+Z` |
| Draw: toggle / hold | `Ctrl+Alt+A` / `Alt+A` |
| Cursor Highlight: toggle / hold | `Ctrl+Alt+X` / `Alt+X` |
| Click Pulse | `Ctrl+Alt+C` |
| Spotlight: toggle / hold | `Ctrl+Alt+S` / `Alt+S` |
| Magnifier | `Ctrl+Alt+V` |
| Pinned Lens | `Ctrl+Alt+F` |
| Region Mask | `Ctrl+Alt+R` |
| Region Spotlight | `Ctrl+Alt+Shift+S` |
| Fading Annotations | `Ctrl+Alt+D` |
| Timer | `Ctrl+Alt+T` |
| Toolbar | `Ctrl+Alt+G` |
| Screenshot / Region Screenshot | `Ctrl+Alt+E` / `Ctrl+Alt+Shift+E` |
| Screen / Black / White Board | `Ctrl+Alt+B` / `Ctrl+Alt+Shift+B` / `Ctrl+Alt+W` |
| Exit FocusTool | `Ctrl+Alt+Shift+Q` |

### Inside Draw

| Action | Key |
|---|---|
| Arrow / Rectangle / Ellipse / Line | `A` / `R` / `C` / `S` |
| Pencil / Highlighter / Eraser | `W` / `F` / `E` |
| Text / Move / Step | `T` / `Q` / `D` |
| Color slot | `1`–`5` |
| Thickness down / up | `Shift+Z` / `Shift+X` |
| Undo / Redo | `Ctrl+Z` / `Ctrl+Shift+Z` |
| Paste text or image | `Ctrl+V` |
| Delete selection | `Backspace` |
| Clear annotations | `Delete` or `Shift+E` |
| Cancel / exit | `Esc` |

### Live wheel controls

| Context | `Ctrl + wheel` | `Ctrl + Shift + wheel` |
|---|---|---|
| Magnifier | Zoom | Radius |
| Spotlight | Dim opacity | Radius |
| Selected annotation | Thickness or text size | — |
| Pinned Lens under the pointer | Zoom | — |

## Practical combinations

- **Brief explanation:** Fading Annotations + Push-to-annotate.
- **Small interface element:** Spotlight + Magnifier.
- **Electronic board:** White/Black Board, `Shift` geometry, Step, and pasted material.
- **Static screen review:** Screen Board with arrows, frames, text, and numbered steps.
- **State comparison:** several Pinned Lenses with one frozen.
- **Single-app presentation:** Capture Stage with a pointer, annotations, and timer.
- **Progressive reveal:** several Region Masks removed as the explanation advances.
- **Mouse demonstration:** Cursor Highlight + Click Pulse + temporary Laser.

## Installation

FocusTool supports **Windows 10 and Windows 11**.

A release normally contains two builds:

- `self-contained.zip` — standalone and does not require a preinstalled .NET Runtime;
- `framework-dependent.zip` — smaller and requires the **.NET 10 Desktop Runtime**.

For most users, `self-contained.zip` is the convenient choice: extract it and run `FocusTool.exe`. The application appears in the system tray.

The executable is not currently code-signed, so Windows SmartScreen can show its standard warning for a new unsigned application.

## Recording and sharing

For whole-desktop capture in OBS and similar software, use Display/Monitor Capture. Capturing one application window normally does not include separate overlay windows.

To present only one application together with FocusTool elements, use Capture Stage and capture it as an ordinary window. Stage is a view-only presentation: interaction continues in the source application. The Windows system picker selects application windows, not individual browser tabs.

Some protected or hardware-accelerated surfaces may not expose an image to system capture.

## Data locations

| Data | Location |
|---|---|
| Settings | `%APPDATA%\FocusTool\settings.json` |
| Error log | `%APPDATA%\FocusTool\log.txt` |
| Captures and saved boards | `Pictures\FocusTool` |

If Pictures is unavailable, Documents is used for captures.

## Compatibility and current scope

- A global shortcut can conflict with Windows or another application; it can be reassigned or disabled.
- Some combinations are unavailable because of the physical keyboard's key rollover or ghosting.
- Performance depends on monitor count, DPI, and the number of active lenses, Stages, timers, and animations.
- Shape recognition, pen pressure, a pixel eraser, shortcut profiles, timer sequences, and timer sound are not currently provided.

## Build from source

The **.NET 10 SDK** is required.

```powershell
dotnet build -c Release
dotnet run -c Release
```

Publish a self-contained single-file build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Run verification checks:

```powershell
dotnet run --project Verification\FocusTool.Verification.csproj
```

## License

[MIT](../license.txt) © 2026 YaRus
