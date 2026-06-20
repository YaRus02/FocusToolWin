# FocusTool

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

**FocusTool** is a lightweight Windows utility for presenting, teaching, recording, and explaining things on screen. It adds a clean always-on-top overlay with a laser pointer, screen annotations, spotlight mode, magnifier, screen board, black/white boards, screenshots, and a compact toolbar.

It is designed for low-noise workflows: most actions are available through global hotkeys, while the overlay toolbar can be shown only when you need mouse or pen control.

> Keywords: screen annotation, drawing on screen, presentation laser pointer, screen magnifier, spotlight overlay, screenshot with annotations, whiteboard, blackboard, screen board, teaching tool, webinar tool, streaming overlay. Рисование поверх экрана, аннотации на экране, лазерная указка для презентаций, экранная лупа, увеличение части экрана, прожектор / спотлайт, выделение области экрана, скриншот с рисованием, доска для презентаций, оверлей для стримов, инструмент преподавателя.

> Development note: FocusTool was built with AI assistance. The functionality has been manually tested across the main workflows: laser pointer, annotations, spotlight, magnifier, boards, screenshots, hotkeys, toolbar, tray menu, and multi-monitor scenarios.

---

## Demo

[![Watch demo](Assets/FocusToolDemo.png)](https://youtu.be/vUoYLz4jiDc?si=sl7DXmUyEfBrLMHW)

## Use Cases

- **Presentations and talks**: point, highlight, draw arrows, and add quick notes over slides, PDFs, or browser content.
- **Online lessons and webinars**: draw directly over any application while explaining a topic.
- **Streaming and screen recording**: make cursor focus, selected regions, and small details easier to follow.
- **Code demos and reviews**: highlight a line, circle a block, point to a control, or magnify small text.
- **Remote support**: show exactly where to click or what to inspect.
- **Quick annotated screenshots**: capture the current monitor, copy it to the clipboard, and save it automatically.

## Features

- **Laser pointer** with a smooth trail, configurable color, size, glow, trail length, and fade timing.
- **Laser activation modes**: always on, or hold a configured key/mouse button.
- **Screen annotations**: pen, marker, arrow, line, rectangle, oval, text, selection, and move.
- **Undo/redo and clear** for annotation sessions.
- **Spotlight mode**: dims the screen while keeping a circular area around the cursor visible.
- **Magnifier**: magnifies the area under the cursor using the Windows Magnification API.
- **Screen board**: freezes the current screen as a board background so you can draw over it.
- **Black board and white board**: clean drawing backgrounds for explanations.
- **Screenshot capture**: captures the current monitor, saves the image, and copies it to the clipboard.
- **Overlay toolbar**: compact floating toolbar with modes, board controls, tools, colors, edit actions, and hide/show behavior.
- **System tray menu** with quick actions and settings.
- **Global hotkeys** for fast, low-noise control.
- **Multi-monitor and per-monitor DPI support**, including 125% / 150% scaling scenarios.

## Default Hotkeys

### Global

| Action | Shortcut |
|---|---|
| Toggle annotation mode | `Ctrl+Alt+D` |
| Toggle laser mode | `Ctrl+Alt+L` |
| Toggle spotlight | `Ctrl+Alt+S` |
| Toggle magnifier | `Ctrl+Alt+M` |
| Toggle overlay toolbar | `Ctrl+Alt+T` |
| Screenshot current monitor | `Ctrl+Alt+C` |
| Screen board | `Ctrl+Alt+G` |
| Black board | `Ctrl+Alt+B` |
| White board | `Ctrl+Alt+W` |
| Exit FocusTool | `Ctrl+Alt+Q` |
| Hold laser, when laser activation is set to hold mode | `XButton2` |

### Annotation Mode

| Action | Shortcut |
|---|---|
| Arrow | `A` |
| Rectangle | `R` |
| Oval / circle | `C` |
| Line | `L` |
| Pen | `P` |
| Marker | `H` |
| Text | `T` |
| Move/select | `M` |
| Colors | `1`-`5` |
| Line thickness | `[` / `]` |
| Undo / redo | `Ctrl+Z` / `Ctrl+Y` |
| Delete selected object | `Backspace` |
| Clear all annotations | `Delete` or `E` |
| Exit visual mode | `Esc` |

Hotkeys can be changed or disabled in **Settings** from the tray menu.

## Installation

FocusTool runs on **Windows 10 / 11**.

Download one of the release packages:

- `self-contained.zip`: recommended for most users. It includes the required runtime and does not require a separate .NET installation.
- `framework-dependent.zip`: smaller package, but requires the **.NET 10 Desktop Runtime** to be installed.

Unzip the package and run `FocusTool.exe`. The application starts in the system tray.

## Recording and Streaming

FocusTool draws its overlays above the desktop as separate top-level windows. Because of that, recording only a single application window may not include the overlay.

For OBS and similar tools, use **Display Capture** to capture the full monitor together with the overlay. If you need only part of the screen, crop the display capture source.

## Data Locations

| Data | Location |
|---|---|
| Settings | `%APPDATA%\FocusTool\settings.json` |
| Log file | `%APPDATA%\FocusTool\log.txt` |
| Screenshots and board captures | `Pictures\FocusTool` |

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

[MIT](license.txt) © 2026 YaRus
