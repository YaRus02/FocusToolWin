![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

# FocusTool

**FocusTool** is a minimalist Windows utility for **presentations, online lessons, calls, screen recording, interface walkthroughs, and working over the screen**. It adds, on top of the desktop, a laser pointer, cursor highlight, click pulse, spotlight, rectangular region spotlight, magnifier, live pinned lens, region masks, drawing tools, numbered step markers, fading annotations, image/text paste, an overlay timer, screen boards, screenshots, and **Capture Stage** for window/share capture.

The main idea of FocusTool: minimum visual noise and fast access to actions through hotkeys, the tray menu, and a compact overlay panel.

Documentation is current for version **3.0.0**.

> Keywords: draw over screen, screen annotation, presentation laser pointer, cursor highlight, click pulse, spotlight, region spotlight, screen magnifier, pinned lens, pinned zoom, region mask, hide screen area, fading annotations, overlay timer, countdown, stopwatch, screen board, whiteboard, region screenshot, step markers, push-to-annotate, capture stage, OBS window capture, teaching, webinars, screen recording, streaming.

> Development note: FocusTool was built with AI assistance and manually tested across the main workflows: laser, annotations, fading annotations, spotlight, magnifier, pinned lens, region masks, overlay timers, boards, screenshots, hotkeys, toolbar, tray menu, Capture Stage, and multi-monitor / DPI scenarios.

---

## Demo

[![Watch demo](Assets/FocusToolDemo.png)](https://youtu.be/gpfk7p2Xd6s)

---

## Use Cases

- **Presentations and talks** - pointer, spotlight, quick arrows, frames, text, and numbered steps over slides.
- **Online lessons and webinars** - draw over any application, boards, timers, screenshots, and pasted material.
- **Screen recording** - cursor highlight, click pulse, magnifier, pinned lens, and region screenshots.
- **Code and interface reviews** - highlight lines, magnify small areas, pin live fragments of the screen.
- **Working with private data** - Region Mask hides selected areas and stays visible in screenshots, magnifier, and pinned lens.
- **Timeboxing and exercises** - a floating timer that does not take over the whole screen.
- **Bug reports and docs** - a quick capture of the whole monitor or a precise region with annotations.

## Features

### Pointer / Focus

- **Laser Pointer** - colored dot with a smooth trail, glow, size, trail length, and `Always` / `Hold` modes.
- **Cursor Highlight** - a stable ring around the cursor with no trail and no flicker. A calm alternative to the laser for recording and calls.
- **Click Pulse** - a short click animation around the cursor. Enabled separately; can be used with or without the highlight ring.
- **Spotlight** - dims the whole screen except a circular area around the cursor.
- **Region Spotlight** - a rectangular spotlight: the selected area stays visible, the rest dims. Regions can be moved, resized, nudged with arrows, and deleted with `Backspace`.
- **Magnifier** - a screen magnifier around the cursor based on the Windows Magnification API.
- **Live Pinned Lens** - a selected screen area is shown enlarged in a separate floating live window. Multiple windows, `Freeze / Resume`, zoom, close one or all.

### Draw / Annotations

- **Pen**, **Highlighter**, **Arrow**, **Line**, **Rectangle**, **Oval**, **Text**, **Move**.
- **Numbered step markers** - markers for step-by-step explanations. Oval-by-click and rect-by-selection; numbering runs separately per color slot.
- **Text editing** - double-click text to edit it in place. `Enter` commits, `Esc` cancels/closes, `Shift+Enter` adds a line.
- **Object edit by double-click** - single objects are edited only after an explicit double-click, reducing accidental moves while drawing near existing annotations.
- **Live color / size changes** - selected objects can get a new color, line thickness, or text size without re-creating them.
- **Undo / Redo / Clear / Delete selected** - history is not polluted by already-faded temporary annotations.
- **Fading annotations** - new annotations can live for a set time and then fade out smoothly.
- **Paste image / text** - `Ctrl+V` pastes an image or text from the clipboard as an annotation object that can be moved, scaled, and included in screenshots/boards.

### Privacy / Masking

- **Region Mask** - rectangular privacy masks over the screen.
- Masks work as a top privacy layer and appear in screenshots, screen board, magnifier, and pinned lens.
- Styles: solid fill, diagonal stripes, `HIDE` label, and stripes + label.
- The create-mask mode is designed for adding several areas in a row.
- A selected mask can be moved, resized, deleted with `Backspace/Delete`, and have its color and opacity changed. New masks remember the last chosen style.

### Boards / Capture

- **Screen Board** - snapshots the current monitor and turns it into a board background for annotations. On exit, the result is saved and copied to the clipboard.
- **Black Board / White Board** - a clean black or white background for explanations.
- **Screenshot** - a snapshot of the current monitor, auto-saved to `Pictures\FocusTool` and copied to the clipboard.
- **Region Screenshot** - select an arbitrary area, tune the frame before saving, live `WxH` size, PNG output, and clipboard copy.
- Annotations, region masks, and board content are included in screenshots. The toolbar is hidden temporarily.
- **Capture Stage** - a separate window that mirrors a chosen application together with FocusTool's overlays (annotations, laser, cursor highlight, spotlight, masks, timer). Capture tools take it as an ordinary window (OBS Window Capture, Zoom / Discord / Teams `Share window`), so the overlays end up in the frame even with window capture. The source is chosen with the system picker; this version supports windows (not the whole screen), view-only.

### Overlay Timer

- Several floating timers at once.
- Modes: `Countdown`, `Stopwatch`, `Clock`, `Until time`.
- The timer label is edited with a double-click and saved in a label history.
- Time is edited with a double-click in modes where it applies.
- 12/24-hour format, progress bar, blink on finish, scale, opacity, light/dark/auto theme.
- Controls live in the timer's own context menu so the main settings window stays light.
- No sound, by design.

### Control

- **Overlay toolbar** - a compact panel over the screen with the main modes and contextual settings rows.
- **Tray menu** - access to modes, settings, colors, tools, and exit.
- **Settings** - split into tabs: pointer, focus, draw, mask, shortcuts.
- **Global hotkeys** - every main shortcut can be changed or disabled.
- **Push-to-annotate** - a quick temporary Draw mode: hold the hotkey, make a note, release - the app returns to passthrough.

## Default Hotkeys

### Global

| Action | Shortcut |
|---|---|
| Laser mode `Always / Hold` | `Ctrl+Alt+L` |
| Draw mode | `Ctrl+Alt+D` |
| Push-to-annotate | `Alt+A` |
| Cursor Highlight | `Ctrl+Alt+U` |
| Spotlight | `Ctrl+Alt+S` |
| Magnifier | `Ctrl+Alt+M` |
| Live Pinned Lens | `Ctrl+Alt+P` |
| Region Mask | `Ctrl+Alt+H` |
| Clear Region Masks | `Ctrl+Alt+Shift+H` |
| Region Spotlight | `Ctrl+Alt+Shift+S` |
| Clear Region Spotlights | `Ctrl+Alt+Shift+X` |
| Fading annotations | `Ctrl+Alt+F` |
| Overlay Timer | `Ctrl+Alt+N` |
| Overlay toolbar | `Ctrl+Alt+T` |
| Screenshot | `Ctrl+Alt+C` |
| Region Screenshot | `Ctrl+Alt+Shift+C` |
| Screen Board | `Ctrl+Alt+G` |
| Black Board | `Ctrl+Alt+B` |
| White Board | `Ctrl+Alt+W` |
| Exit FocusTool | `Ctrl+Alt+Q` |
| Laser hold key | `Alt+Z` |
| Cursor Highlight hold key | `Alt+X` |

### In Draw mode

| Action | Shortcut |
|---|---|
| Arrow | `A` |
| Rectangle | `R` |
| Oval / Circle | `C` |
| Line | `L` |
| Pen | `P` |
| Highlighter | `H` |
| Text | `T` |
| Move | `M` |
| Step marker | `N` |
| Color slot 1-5 | `1`-`5` |
| Line thickness | `[` / `]` |
| Undo / Redo | `Ctrl+Z` / `Ctrl+Y` |
| Delete selected | `Backspace` |
| Clear annotations | `Delete` or `E` |
| Exit visual mode | `Esc` |
| Paste image/text | `Ctrl+V` |

Any hotkey can be changed in `Settings -> Shortcuts`. To disable a hotkey, leave the field empty or set `None`.

## Important: keyboard ghosting

Some keyboards physically cannot send certain combinations of simultaneously held keys to the system. This is called **keyboard ghosting** (or limited key rollover). It is a hardware trait of the keyboard, not a FocusTool bug: Windows simply never receives the keypress.

For example, with `Ctrl+Space` held, the keys `3` or `E` may not arrive on one keyboard while other keys work fine. Which keys are "blocked" depends on the matrix of the specific keyboard.

It is more common on laptop and membrane keyboards. Mechanical keyboards with full N-key rollover (NKRO) usually do not have this problem.

Practical guidance:

- for hold triggers, prefer single keys or short combinations - the fewer keys held at once, the lower the chance of ghosting;
- for this reason Push-to-annotate defaults to `Alt+A`, and laser / Cursor Highlight hold default to `Alt+Z` and `Alt+X`;
- if a specific combination does not work on your keyboard, assign a different (or simpler) one in `Settings -> Shortcuts`.

## Mode Behavior

### Live Pinned Lens

`Ctrl+Alt+P` starts area selection. After selecting, a live window appears with an enlarged copy of the chosen fragment. The window can be dragged, scaled via the context menu or `Ctrl + mouse wheel`, frozen, and closed individually or all at once.

Annotations are drawn over pinned lens windows. Region masks stay visible inside the enlarged image so a hidden area cannot be revealed through zoom.

### Region Spotlight

`Ctrl+Alt+Shift+S` starts rectangular focus selection. After it is created, the region can be moved, resized from corners, nudged with arrows, and deleted with `Backspace`. `Enter` and `Esc` leave edit mode without deleting the region.

### Region Screenshot

`Ctrl+Alt+Shift+C` starts area selection for a screenshot. After selecting, the frame can be refined before saving. `Enter` saves the area, `Esc` cancels. The image is saved to `Pictures\FocusTool` and copied to the clipboard.

### Push-to-annotate

`Alt+A` temporarily enables Draw mode while held. The last selected tool is used. If you release the hotkey during a drag/stroke, the current shape is not cut off: exit happens after the action completes.

### Text and object editing

A double-click on an object enters edit mode. For text it opens input; for shapes it shows move/resize handles where applicable. Outside edit mode, normal tools create new objects instead of accidentally moving existing ones.

### Capture Stage

The source is chosen with the Windows system picker. A separate window appears that mirrors the chosen application together with the overlays; capture tools take it as an ordinary window.

- You keep working in the **real** source window - the Stage only mirrors it.
- The source must not be **minimized** - a minimized window stops rendering and the frame freezes. It can be covered by other windows.
- The Stage window itself **cannot be minimized**, so you do not accidentally lose the picture in the recording.
- In the capture tool, disable its own cursor drawing (`Capture Cursor` in OBS), otherwise the cursor is doubled.

## Color Slots

FocusTool uses configurable color slots instead of hard-coded colors:

- Laser: `Color 1`-`Color 5`;
- Cursor Highlight: `Color 1`-`Color 5`;
- Annotation: `Color 1`-`Color 5`;
- Region Mask: `Color 1`-`Color 5`.

Colors are set in HEX. Alpha is supported:

```text
#FFFF2020
#80FFFFFF
#BFFFD400
```

Format `#AARRGGBB`: the first two characters after `#` set transparency. For example, `#80FFFFFF` is white at roughly 50% transparency.

## Installation

Requires **Windows 10 / 11**.

A release usually has two builds:

- `self-contained.zip` - standalone build. Does not require an installed .NET Runtime.
- `framework-dependent.zip` - smaller build. Requires the installed **.NET 10 Desktop Runtime**.

For most users `self-contained.zip` is recommended: unzip the archive and run `FocusTool.exe`. After launch, the app appears in the system tray.

The executable is not yet code-signed. Windows SmartScreen may show a warning on the first launch of a new unsigned application.

## Data Locations

| Data | Location |
|---|---|
| Settings | `%APPDATA%\FocusTool\settings.json` |
| Error log | `%APPDATA%\FocusTool\log.txt` |
| Screenshots and saved boards | `Pictures\FocusTool` |

If the `Pictures` folder is unavailable, the app uses `Documents`.

## Recording and Streaming

FocusTool draws as a desktop overlay. For OBS and similar tools, the simplest option is **Display Capture / Monitor Capture** - it includes the laser, annotations, timer, masks, pinned lens, and toolbar.

**Window Capture** usually captures only a single application window and does not include FocusTool's overlay windows. This is a Windows window-composition limitation, not a FocusTool bug.

If you need to share **only one application** (not the whole screen) while keeping the overlays, use **Capture Stage**: a separate window composes the chosen source together with annotations and the timer, and capture tools take it as an ordinary window (`Share window` / OBS Window Capture). In the capture tool, disable its own cursor drawing (in OBS - the **Capture Cursor** option): the cursor is already inside the Stage frame, otherwise it is doubled.

## Limitations and Notes

- FocusTool targets Windows 10 / 11.
- An unsigned exe may trigger a Windows SmartScreen warning.
- If a global hotkey conflicts with another program, the app shows a warning and the hotkey must be reassigned.
- Some shortcuts may not work due to keyboard ghosting of a specific keyboard.
- Performance depends on the number of monitors, DPI, pinned lenses, timers, masks, and active animations.
- The timer uses no sound.

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
