![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

**FocusTool** is a lightweight Windows utility for **drawing and annotating over the screen**. It provides a virtual **laser pointer** with a smooth trail, a **screen magnifier**, **live pinned zoom areas**, **region masks**, **spotlight mode**, **floating overlay timers**, **screenshots with annotations**, and drawing boards that work over any application.

It is built for **presentations, webinars, online lessons, screen recordings, streams, code reviews, remote support, and quick visual explanations**. The main idea is simple: keep the interface quiet, use hotkeys first, and show the overlay toolbar only when mouse, pen, touch, or whiteboard control is useful.

> Keywords: screen annotation, draw on screen, drawing over screen, presentation laser pointer, screen magnifier, live pinned lens, pinned zoom, region mask, hide screen area, fading annotations, overlay timer, floating timer, countdown timer, stopwatch overlay, spotlight overlay, screenshot with annotations, whiteboard, blackboard, screen board, teaching tool, webinar tool, streaming overlay.

> Development note: FocusTool was built with AI assistance. The functionality has been manually tested across the main workflows: laser pointer, annotations, fading annotations, spotlight, magnifier, pinned lens, region masks, overlay timers, boards, screenshots, hotkeys, toolbar, tray menu, and multi-monitor scenarios.

---

## Demo

[![Watch demo](Assets/FocusToolDemo.png)]([https://youtu.be/gpfk7p2Xd6s])

---

## Use Cases

- **Presentations and talks**: point, highlight, draw arrows, and add quick notes over slides, PDFs, or browser content.
- **Online lessons, webinars, and tutoring**: draw directly over any application while explaining a topic.
- **Streaming and screen recording**: make cursor focus, selected regions, and small details easier to follow.
- **Code demos and reviews**: highlight a line, circle a block, magnify small text, or pin a live output fragment.
- **Privacy during demos**: hide rectangular areas of the screen with Region Mask.
- **Remote support**: show exactly where to click or what to inspect.
- **Quick annotated screenshots**: capture the current monitor, copy it to the clipboard, and save it automatically.

## Features

- **Laser pointer** with a smooth comet-like trail, configurable color, size, glow, trail length, and fade timing.
- **Laser activation modes**: always on, or hold a configured key/mouse button.
- **Screen annotations**: pen, marker, arrow, line, rectangle, oval, text, selection, and move.
- **Fading annotations**: new annotations can stay visible for a configured time and then fade out smoothly without leaving empty undo/redo steps.
- **Overlay Timer**: floating timers above the desktop with Countdown, Stopwatch, Clock, and Until time modes.
- **Live Pinned Lens**: select any screen rectangle and show it enlarged in a separate live floating window. Multiple pinned lenses are supported.
- **Region Mask**: hide selected rectangular screen areas. Existing masks can be moved, resized from corners, and deleted from a context menu.
- **Spotlight mode**: dims the screen while keeping a circular area around the cursor visible.
- **Magnifier**: magnifies the area under the cursor using the Windows Magnification API.
- **Screen Board**: freezes the current screen as a board background so you can draw over it. The final image is saved and copied to the clipboard when you exit.
- **Black Board and White Board**: clean drawing backgrounds for quick explanations.
- **Screenshot capture**: captures the current monitor, saves the image, and copies it to the clipboard.
- **Overlay toolbar**: compact floating toolbar with mode buttons and contextual rows for laser, drawing, spotlight, zoom, pinned lens, masks, timers, boards, and actions.
- **Configurable color slots**: laser, annotation, and region mask colors use editable Color 1-5 slots shared by Settings, toolbar, and tray menu.
- **System tray menu** with quick actions and settings.
- **Global hotkeys** for fast, low-noise control.
- **Multi-monitor and per-monitor DPI support**, including 125% / 150% scaling scenarios.

## Overlay Toolbar

Show or hide the toolbar:

```text
Ctrl+Alt+T
```

Main toolbar row:

- `Laser`: laser pointer and quick laser settings.
- `Draw`: annotation mode, tools, colors, line size, text size, Fade, Undo, Redo, Clear.
- `Spot`: spotlight and its parameters.
- `Zoom`: screen magnifier and its parameters.
- `Pin`: select an area for Live Pinned Lens and tune defaults for new lenses.
- `Mask`: select an area for Region Mask, color, opacity, and clear action.
- `Board`: Screen Board, Black Board, White Board.
- `Shot`: screenshot the current monitor.
- `Timer`: create floating timers and close all active timers.
- `...`: hide toolbar, open settings, or close the app.

The toolbar can collapse into a small `FT` grip. The grip can be dragged, and a normal click opens the toolbar again.

## Color Slots

FocusTool uses configurable color slots instead of hard-coded color names.

- Laser: `Color 1`-`Color 5`.
- Annotation: `Color 1`-`Color 5`.
- Region Mask: `Color 1`-`Color 5`.

Colors are edited in Settings using HEX values, for example:

```text
#FFFF2020
#FF2080FF
#FFFFFFFF
```

After `Apply` / `OK`, the same slots are used by the toolbar and tray menu. If an older settings file contains a custom current color that is not present in the slots, FocusTool keeps it by placing it into `Color 5`.

## Default Hotkeys

### Global

| Action | Shortcut |
|---|---|
| Toggle annotation mode | `Ctrl+Alt+D` |
| Toggle laser activation Always / Hold | `Ctrl+Alt+L` |
| Toggle spotlight | `Ctrl+Alt+S` |
| Toggle magnifier | `Ctrl+Alt+M` |
| Live Pinned Lens: select area | `Ctrl+Alt+P` |
| Region Mask: select area | `Ctrl+Alt+H` |
| Clear all Region Masks | `Ctrl+Alt+Shift+H` |
| Toggle fading annotations | `Ctrl+Alt+F` |
| New overlay timer | `Ctrl+Alt+N` |
| Toggle overlay toolbar | `Ctrl+Alt+T` |
| Screenshot current monitor | `Ctrl+Alt+C` |
| Screen Board | `Ctrl+Alt+G` |
| Black Board | `Ctrl+Alt+B` |
| White Board | `Ctrl+Alt+W` |
| Exit FocusTool | `Ctrl+Alt+Q` |
| Hold laser in Hold mode | `XButton2` |

### Annotation Mode

| Action | Shortcut |
|---|---|
| Tools | `A` Arrow · `R` Rectangle · `C` Circle/Oval · `L` Line · `P` Pen · `H` Marker · `T` Text · `M` Move |
| Color slots | `1`-`5` |
| Line thickness | `[` and `]` |
| Undo / redo | `Ctrl+Z` / `Ctrl+Y` |
| Delete selected object | `Backspace` |
| Clear all annotations | `Delete` or `E` |
| Exit visual mode | `Esc` |

Hotkeys can be changed or disabled in **Settings** from the tray menu.

## Live Pinned Lens

**Live Pinned Lens** is a live enlarged floating copy of any selected screen rectangle. Select a small area, move the enlarged window to a free place, and keep working in the original application while the pinned lens updates in real time.

Supported behavior:

- multiple pinned lenses at the same time;
- drag the lens window with the mouse;
- thin border without the standard system frame;
- `Freeze / Resume`;
- zoom through the context menu or `Ctrl + mouse wheel`;
- close one lens or close all lenses.

Laser and annotations are drawn above pinned lens windows. Region Masks remain visible inside the enlarged image so a hidden area cannot be revealed through zoom.

## Region Mask

**Region Mask** hides selected rectangular areas of the screen.

Behavior:

- one selection creates one mask and returns FocusTool to normal passthrough mode;
- in `Mask` mode, existing masks can be moved by dragging their body;
- masks can be resized from corner handles;
- right-click a mask to open its delete menu;
- deleting one mask does not exit `Mask` mode;
- all masks can be cleared through the hotkey or tray menu;
- new masks use the current mask color slot and opacity from Settings.

Masks are visible in the magnifier, pinned lens, screenshots, and Screen Board. This is intentional: if an area is hidden, helper tools should not reveal it.

## Fading Annotations

Fading annotations are controlled by a global mode. When enabled, newly created annotations stay fully visible for a configured time and then fade out smoothly.

Toggle:

```text
Ctrl+Alt+F
```

Toolbar: `Draw -> Fade`. The small button next to `Fade` opens quick controls for visible time and fade duration.

Existing annotations do not change behavior when the mode is toggled. Automatic removal does not create empty undo/redo steps.

## Overlay Timer

**Overlay Timer** is a live floating timer above the desktop. It does not open a full-screen mode and does not interrupt the current application.

Create a new timer:

```text
Ctrl+Alt+N
```

Supported behavior:

- multiple timers at the same time;
- Countdown, Stopwatch, Clock, and Until time modes;
- drag the timer window by its body;
- double-click the label to edit it;
- double-click the time to edit it in modes where direct time input is available;
- size and opacity controls visible only while the timer is focused;
- context menu for mode, style, 12/24-hour format, progress bar, label, and blink on finish;
- Light, Dark, and Auto themes. Auto follows the Windows app theme.

Keyboard controls while a timer is focused:

| Action | Shortcut |
|---|---|
| Start / pause | `Space` |
| Reset | `R` |
| Cycle mode | `Tab` |
| Adjust time by 1 second | `Left` / `Right` |
| Adjust time by 1 minute | `Up` / `Down` |
| Adjust time by 5 minutes | `Shift+Up` / `Shift+Down` |
| Return focus to previous window | `Esc` |

Active timer settings are saved as defaults for newly created timers. Open timer windows are not restored after application restart.

## Installation

FocusTool runs on **Windows 10 / 11**.

Download one of the release packages:

- `self-contained.zip`: recommended for most users. It includes the required runtime and does not require a separate .NET installation.
- `framework-dependent.zip`: smaller package, but requires the **.NET 10 Desktop Runtime** to be installed.

Unzip the package and run `FocusTool.exe`. The application starts in the system tray.

## Recording and Streaming

FocusTool draws its overlays above the desktop as separate top-level windows. Because of that, recording only a single application window may not include the overlay.

For OBS and similar tools, use **Display Capture** to capture the full monitor together with laser, annotations, spotlight, magnifier, pinned lenses, masks, timers, and toolbar. If you need only part of the screen, crop the display capture source.

## Data Locations

| Data | Location |
|---|---|
| Settings | `%APPDATA%\FocusTool\settings.json` |
| Log file | `%APPDATA%\FocusTool\log.txt` |
| Screenshots and board captures | `Pictures\FocusTool` |

If `Pictures` is unavailable, FocusTool uses `Documents`.

## Build From Source

Requires the **.NET 10 SDK**.

```bash
dotnet build -c Release
dotnet run -c Release
```

Self-contained single-file publish:

```bash
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Framework-dependent single-file publish:

```bash
dotnet publish -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true
```

## License

[MIT](license.txt) (c) 2026 YaRus
