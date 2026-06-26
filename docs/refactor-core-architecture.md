# Core Architecture Refactoring

## Baseline

Measured before the refactoring pass on `refactor/core-architecture`.

| File | Lines |
|---|---:|
| `Services/FocusToolController.cs` | 4200 |
| `Overlay/OverlaySurface.cs` | 1773 |
| `Models/AnnotationDocument.cs` | 1371 |
| `Overlay/TimerWindow.cs` | 1259 |
| `Overlay/OverlayToolbarWindow.cs` | 1134 |
| `SettingsWindow.xaml.cs` | 963 |
| `Overlay/PinnedLensHostWindow.cs` | 924 |
| `Tray/TrayIconController.cs` | 752 |

## Guardrails

- Keep behavior-preserving extractions separate from feature work.
- Keep `FocusToolController` as the integration facade until subsystem boundaries are stable.
- Move state ownership by feature area, not by random private-method grouping.
- Avoid a DI container, event bus, MVVM rewrite, or UI redesign during core refactoring.
- Validate each step with build, formatting, diff hygiene, and manual regression checks.

## Manual Regression Checklist

- Laser: always mode, hold mode, color/size/trail/fade/glow changes.
- Cursor highlight and click pulse: always/hold, pulse-only, toolbar/tray/settings toggles.
- Spotlight: cursor spotlight, region spotlight, move/resize/nudge/delete/clear.
- Magnifier: enable/disable, radius/zoom changes, monitor transitions.
- Pinned lens: create, multiple lenses, move, zoom, freeze/resume, close one/all.
- Region mask: create multiple, move, resize, style, color/opacity, delete, clear all.
- Annotations: pen, marker, line, arrow, rectangle, oval, text, move, object edit, undo/redo/clear.
- Step markers: oval/rect, numbering by color, selection/deletion behavior.
- Paste: text and image clipboard insertion.
- Push-to-annotate: hold entry, delayed exit after stroke/text, tool/color shortcuts while held.
- Boards: screen board, black board, white board, save/copy on exit.
- Screenshots: monitor screenshot, region screenshot with edit/Enter/Esc.
- Timer: create multiple, modes, edit time/label, context menu, focus controls, close all.
- Toolbar/tray/settings: state sync, topmost behavior, hidden/collapsed toolbar.
- Multi-monitor/DPI: selection, pinned lens, toolbar/timer clamping, display changes.

## Planned Extraction Order

1. Rect selection and rect edit primitives.
2. Region mask controller.
3. Region spotlight controller.
4. Pinned lens controller.
5. Magnifier controller.
6. Pointer effects controllers.
7. Hotkeys and push-to-annotate routing.
8. Capture/screenshot/screen-board coordination.
9. Overlay rendering helpers.
10. UI layer cleanup for toolbar, tray, and settings.
