FocusTool
=========

FocusTool is a minimalist Windows utility for presentations, lessons, calls,
screen recording, interface reviews, and everyday work over the screen.

It adds, on top of the desktop:

- a laser pointer;
- a stable cursor highlight;
- click pulse to show clicks;
- a spotlight around the cursor;
- a rectangular region spotlight;
- a screen magnifier;
- live pinned lens;
- region masks;
- drawing and text tools;
- numbered step markers;
- fading annotations;
- image and text paste from the clipboard;
- an overlay timer;
- screen board, black board, and white board;
- screenshots of a monitor or a selected area;
- Capture Stage: a window with the chosen application and overlays for capture;
- a compact overlay toolbar and a tray menu.

The main idea of FocusTool is minimum visual noise and fast access through
hotkeys, the tray menu, and a compact panel over the screen.

Documentation is current for version 3.0.0.


1. Which file to run
--------------------

A publish usually has two builds:

self-contained\FocusTool.exe
  Standalone build. Does not require an installed .NET Runtime.
  Recommended for normal use and distribution.

framework-dependent\FocusTool.exe
  Smaller build. Requires the installed .NET Desktop Runtime of a compatible
  version. Convenient if the runtime is already installed.

For normal use, run:

  self-contained\FocusTool.exe

After launch, FocusTool appears in the Windows system tray. If the icon is not
visible, check the hidden tray icons area.

The executable is not yet code-signed. Windows SmartScreen may show a warning on
the first launch of a new unsigned application.


2. Where settings and files are stored
--------------------------------------

Settings:

  %APPDATA%\FocusTool\settings.json

Error log:

  %APPDATA%\FocusTool\log.txt

Screenshots and saved screen boards:

  Pictures\FocusTool

If the Pictures folder is unavailable, the app uses Documents.


3. General behavior
-------------------

FocusTool creates overlay windows above the monitors. In the normal state they
pass clicks through and do not interfere with other applications.

When Draw mode, area selection, mask editing, region spotlight, or a screen
board is active, the overlay starts accepting mouse and keyboard input.

The app does not show a main window. Control goes through:

- the system tray;
- the overlay toolbar;
- global hotkeys;
- context menus of the timer, pinned lens, and region mask;
- the Settings window.


4. Main default hotkeys
-----------------------

Global modes:

  Ctrl+Alt+L        - toggle laser activation Always / Hold
  Ctrl+Alt+D        - toggle Draw mode
  Alt+A             - Push-to-annotate: temporary Draw mode while held
  Ctrl+Alt+U        - toggle Cursor Highlight
  Ctrl+Alt+S        - toggle Spotlight
  Ctrl+Alt+M        - toggle Magnifier
  Ctrl+Alt+P        - select an area for Live Pinned Lens
  Ctrl+Alt+H        - select an area for Region Mask
  Ctrl+Alt+Shift+H  - clear all Region Masks
  Ctrl+Alt+Shift+S  - select an area for Region Spotlight
  Ctrl+Alt+Shift+X  - clear all Region Spotlights
  Ctrl+Alt+F        - toggle Fading annotations
  Ctrl+Alt+N        - create a new Overlay Timer
  Ctrl+Alt+T        - show or hide the Overlay Toolbar
  Ctrl+Alt+C        - screenshot the current monitor
  Ctrl+Alt+Shift+C  - screenshot a selected area
  Ctrl+Alt+G        - toggle Screen Board
  Ctrl+Alt+B        - toggle Black Board
  Ctrl+Alt+W        - toggle White Board
  Ctrl+Alt+Q        - exit FocusTool

Hold in Hold modes:

  Alt+Z             - hold the laser in Hold mode
  Alt+X             - hold Cursor Highlight in Hold mode

Draw tools:

  A                 - Arrow
  R                 - Rectangle
  C                 - Oval / Circle
  L                 - Line
  P                 - Pen
  H                 - Highlighter
  T                 - Text
  M                 - Move
  N                 - Step marker

Colors and size:

  1                 - color slot 1
  2                 - color slot 2
  3                 - color slot 3
  4                 - color slot 4
  5                 - color slot 5
  [                 - decrease line thickness
  ]                 - increase line thickness

Annotation commands:

  Ctrl+Z            - Undo
  Ctrl+Y            - Redo
  Backspace         - delete selected object
  Delete            - clear annotations
  E                 - alternative clear annotations
  Esc               - leave the visual mode
  Ctrl+V            - paste an image or text from the clipboard

Any combination can be changed or disabled in Settings -> Shortcuts. To disable
a hotkey, leave the field empty or set None.


5. Important: keyboard ghosting
-------------------------------

Some keyboards physically cannot send certain combinations of simultaneously
held keys to the system. This is called keyboard ghosting (or limited key
rollover). It is a hardware trait of the keyboard, not a FocusTool bug: Windows
simply never receives the keypress.

For example, with Ctrl+Space held, the keys 3 or E may not arrive on one
keyboard while other keys work fine. Which keys are "blocked" depends on the
matrix of the specific keyboard.

It is more common on laptop and membrane keyboards. Mechanical keyboards with
full N-key rollover (NKRO) usually do not have this problem.

Practical guidance:

- for hold triggers, prefer single keys or short combinations: the fewer keys
  held at once, the lower the chance of ghosting;
- for this reason Push-to-annotate defaults to Alt+A, and laser / Cursor
  Highlight hold default to Alt+Z and Alt+X;
- if a specific combination does not work, assign a different (or simpler) one
  in Settings -> Shortcuts.


6. Laser Pointer
----------------

The laser draws a colored dot and a smooth trail above the screen.

Supported:

- Always mode;
- Hold mode;
- configurable color slots;
- dot size;
- trail length;
- fade after release;
- glow.

Ctrl+Alt+L toggles the Always / Hold activation mode.
In Hold mode the default hold key is Alt+Z.


7. Cursor Highlight and Click Pulse
-----------------------------------

Cursor Highlight is a stable ring around the cursor. It does not use laser-trail
logic, does not "build up" color while moving, and does not flicker. It is a
calm alternative to the laser for recording and calls.

Click Pulse shows a short animation around the cursor on a mouse click. It is
enabled separately and can be used with or without the highlight ring, so a
viewer sees the fact of a click without extra explanation.

Settings include cursor highlight color slots, Always / Hold mode, hold
shortcut, size, ring thickness, and the Click Pulse toggle.

In Hold mode the default hold key is Alt+X.


8. Spotlight and Region Spotlight
---------------------------------

Spotlight dims the screen around the cursor and keeps a circular area visible.

  Ctrl+Alt+S

Region Spotlight is the rectangular version.

  Ctrl+Alt+Shift+S

Region Spotlight behavior:

- select a rectangular area;
- inside the area the screen stays visible;
- everything around dims;
- the area can be moved;
- it can be resized from corners;
- arrows nudge the selected region;
- Backspace deletes the selected region;
- Enter and Esc leave edit mode without deleting.

Useful for highlighting a table, an interface panel, a block of code, or a
settings fragment when a circular spotlight is not precise enough.


9. Magnifier
------------

The magnifier enlarges the area around the cursor.

  Ctrl+Alt+M

Settings:

- radius;
- zoom.

It uses the Windows Magnification API. Region masks stay visible in the
magnified area so a hidden fragment cannot be revealed through zoom. Pointer
visuals such as the laser / cursor highlight are suppressed while the magnifier
is active to avoid artifacts inside the enlarged image.


10. Live Pinned Lens
--------------------

Live Pinned Lens is a live enlarged fragment of a selected screen area.

Start:

  Ctrl+Alt+P

After starting, select a rectangle on the screen. FocusTool creates a floating
window with a live enlarged copy of the selected area.

Supported:

- multiple pinned lenses at the same time;
- drag the window;
- thin border without the standard system frame;
- right-click context menu;
- Freeze / Resume;
- Zoom in / Zoom out;
- Ctrl + mouse wheel for per-lens zoom;
- Close / Close all;
- delete the selected lens with Backspace / Delete.

Annotations are drawn over pinned lens windows. Region masks stay visible inside
the enlarged image.


11. Region Mask
---------------

Region Mask hides selected rectangular areas of the screen.

Start:

  Ctrl+Alt+H

Clear all masks:

  Ctrl+Alt+Shift+H

Behavior:

- several masks can be created in a row;
- a mask is a top privacy layer;
- masks are visible in screenshots, screen board, magnifier, and pinned lens;
- a selected mask can be moved;
- it can be resized from corners;
- Backspace / Delete deletes the selected mask;
- Ctrl + mouse wheel changes the opacity of the selected mask;
- the selected mask color can be changed live;
- the context menu allows deleting the mask and choosing a style.

Styles:

- solid fill;
- diagonal stripes;
- HIDE label;
- stripes + HIDE label.

New masks use the current color, opacity, and the last chosen style. Existing
masks keep their own parameters.


12. Draw mode and annotations
-----------------------------

Draw mode:

  Ctrl+Alt+D

Tools:

- Pen;
- Highlighter;
- Arrow;
- Line;
- Rectangle;
- Oval;
- Text;
- Move;
- Step marker.

Step marker:

- an oval step is placed with a click;
- a rect step is drawn by selecting a rectangle;
- the number is computed automatically;
- numbering runs separately per color slot;
- Clear resets the sequence.

Editing:

- a double-click on an object enters edit mode;
- outside edit mode, normal tools create new objects;
- selected objects can be deleted with Backspace;
- color, thickness, and text size can be applied live;
- Text is edited in place;
- Enter commits text input;
- Esc closes or cancels input;
- Shift+Enter adds a new line.

Paste:

  Ctrl+V

If the clipboard holds an image or text, FocusTool pastes it as an annotation
object. Unsupported clipboard content is ignored.


13. Fading annotations
----------------------

Fading annotations are enabled globally.

  Ctrl+Alt+F

When enabled, new annotations are first fully visible and then fade out
smoothly. Existing annotations do not change behavior when the mode is toggled.

The visible time and fade time parameters are available in the toolbar and the
Draw settings.


14. Overlay Timer
-----------------

Create a new timer:

  Ctrl+Alt+N

Supported:

- multiple timers at the same time;
- Countdown;
- Stopwatch;
- Clock;
- Until time;
- a custom label;
- a history of entered labels;
- double-click the label to edit it;
- double-click the time to enter time in applicable modes;
- 12/24-hour format;
- progress bar;
- blink on finish;
- scale;
- opacity;
- dark / light / auto theme;
- a control context menu.

There is no sound, by design. The timer is meant for calm visual display over
the current work, not for a full-screen mode.


15. Screen Board, Black Board, White Board
------------------------------------------

Screen Board:

  Ctrl+Alt+G

Screen Board snapshots the current monitor and turns it into a background for
annotations. On exit, the result is saved automatically to Pictures\FocusTool
and copied to the clipboard.

Black Board:

  Ctrl+Alt+B

White Board:

  Ctrl+Alt+W

Black / White Board give a clean background for explanations. Unlike Screen
Board, they do not snapshot the screen on entry.


16. Screenshots
---------------

Screenshot of the current monitor:

  Ctrl+Alt+C

Screenshot of a selected area:

  Ctrl+Alt+Shift+C

When taking a screenshot:

- the image is saved to Pictures\FocusTool;
- the image is copied to the clipboard;
- the toolbar is temporarily hidden;
- annotations and region masks are included in the result.

Region Screenshot lets you select an area, refine the frame before saving, and
see the area size.

File names use the format:

  FocusTool_yyyy-MM-dd_HH-mm-ss-fff.png

Screen board is saved with the prefix:

  FocusTool_Board_yyyy-MM-dd_HH-mm-ss-fff.png


17. Capture Stage
-----------------

Capture Stage lets you share only one application (not the whole screen) while
keeping FocusTool's overlays in the frame.

The source is chosen with the Windows system picker. A separate window appears
that mirrors the chosen application together with the overlays (annotations,
laser, cursor highlight, spotlight, masks, timer). Capture tools take it as an
ordinary window (Share window / OBS Window Capture), so the overlays end up in
the frame even with window capture.

Important about Capture Stage:

- you keep working in the real source window; the Stage only mirrors it;
- the source must not be minimized: a minimized window stops rendering and the
  frame freezes. It can be covered by other windows;
- the Stage window itself cannot be minimized, so you do not lose the picture in
  the recording;
- in the capture tool, disable its own cursor drawing (in OBS - the Capture
  Cursor option), otherwise the cursor is doubled;
- this version supports windows (not the whole screen), view-only.


18. Overlay Toolbar
-------------------

Overlay Toolbar is a compact control panel over the screen.

Show / hide:

  Ctrl+Alt+T

The panel contains groups:

- Pointer: Laser and Cursor Highlight;
- Spot;
- Zoom and Pin;
- Draw;
- Mask;
- Board;
- Capture;
- Timer;
- extra actions through "...".

The main groups have contextual rows with quick settings. The panel can collapse
into a small grip labeled FT and expand back with a click.


19. Color slots
---------------

FocusTool uses configurable color slots:

- Laser: Color 1..5;
- Cursor Highlight: Color 1..5;
- Annotation: Color 1..5;
- Region Mask: Color 1..5.

Colors are set in HEX:

  #FFFF2020
  #80FFFFFF
  #BFFFD400

Alpha is supported in the #AARRGGBB format. For example, #80FFFFFF is white at
roughly 50% transparency.


20. Tray menu and Settings
--------------------------

FocusTool runs from the system tray. The tray menu has the main modes, colors,
tools, undo/redo/clear, settings, and exit.

Settings are split into tabs:

- Pointer;
- Focus;
- Draw;
- Mask;
- Shortcuts.

In Shortcuts you can change or disable global hotkeys and hotkeys for tools,
colors, and commands.

Hotkey format:

  A
  Alt+A
  Ctrl+Alt+D
  Delete
  Esc
  Backspace
  [
  ]

To disable, use an empty field or None.


21. Recording and streaming
---------------------------

FocusTool draws as a desktop overlay. For OBS and similar tools the simplest
option is Display Capture / Monitor Capture - it includes the laser,
annotations, spotlight, magnifier, pinned lens, region masks, timer, and
toolbar.

Window Capture usually captures only a single application window and does not
include FocusTool's overlay windows. This is a Windows window-composition
limitation, not a FocusTool bug.

If you need to share only one application (not the whole screen) while keeping
the overlays, use Capture Stage (see section 17): a separate window composes the
chosen source together with annotations and the timer, and capture tools take it
as an ordinary window. In the capture tool, disable its own cursor drawing (in
OBS - the Capture Cursor option), otherwise the cursor is doubled.


22. Limitations and notes
-------------------------

- FocusTool targets Windows 10 / 11.
- For distribution, the self-contained build is usually the best choice.
- An unsigned exe may trigger a Windows SmartScreen warning.
- If a global hotkey is taken by another program, it must be reassigned.
- Some shortcuts may not work due to keyboard ghosting of a specific keyboard.
- In Draw mode the overlay captures the mouse. To work normally, leave it with
  Esc.
- Screen Board saves the result on exit. Black Board and White Board do not save
  automatically.
- Performance depends on the number of monitors, DPI, pinned lenses, timers,
  masks, and active animations.
- The timer uses no sound.


23. Quick start
---------------

1. Run self-contained\FocusTool.exe.
2. Press Ctrl+Alt+T to open the overlay toolbar.
3. Press Ctrl+Alt+D to enter Draw mode.
4. Pick Pen / Arrow / Rect / Text / Step via the toolbar or hotkeys.
5. Press Esc to leave Draw mode.
6. Hold Alt+A to make a quick temporary note.
7. Press Ctrl+Alt+N to create an overlay timer.
8. Press Ctrl+Alt+P and select an area for live pinned lens.
9. Press Ctrl+Alt+H and select an area for region mask.
10. Press Ctrl+Alt+Shift+C to capture a selected area.
11. Press Ctrl+Alt+G to create a screen board.


24. Uninstall
-------------

FocusTool does not require installation.

To remove the app:

1. Close FocusTool from the tray menu or with Ctrl+Alt+Q.
2. Delete the folder with the exe.
3. Optionally delete the settings:

   %APPDATA%\FocusTool

4. Optionally delete saved screenshots:

   Pictures\FocusTool
