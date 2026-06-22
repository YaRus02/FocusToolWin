FocusTool
=========

FocusTool is a lightweight Windows utility for presentations, lessons, reviews,
calls, screen recordings, streams, and everyday screen explanation. It adds a
laser pointer, spotlight, magnifier, live pinned lens, region masks, drawing
tools, fading annotations, floating overlay timers, screen boards, and quick
screenshots above the desktop.

The main idea is simple: keep visual noise low and make hotkeys the primary
control method. The app runs from the system tray and does not show a main
window. Most actions are available through global hotkeys or through the compact
overlay toolbar.


1. Which file to run
--------------------

There are two builds:

- self-contained\FocusTool.exe
  Full standalone build. It does not require .NET Runtime to be installed on the
  computer. This is the recommended build for most users.

- framework-dependent\FocusTool.exe
  Smaller build. It requires a compatible .NET Desktop Runtime to be installed.
  This is convenient for developers or machines that already have the runtime.

For normal use, run:

  self-contained\FocusTool.exe

After launch, FocusTool appears in the Windows system tray. If the tray icon is
not visible, check the hidden tray icons area.


2. Data locations
-----------------

Settings:

  %APPDATA%\FocusTool\settings.json

Error log:

  %APPDATA%\FocusTool\log.txt

Screenshots and saved screen board captures:

  Pictures\FocusTool

If the Pictures folder is unavailable, FocusTool uses Documents.


3. General behavior
-------------------

FocusTool creates transparent overlay windows above the monitors. In the normal
state, these overlays pass mouse clicks through and do not block other
applications. When annotation mode, area selection, mask editing, or a board is
active, the overlay starts accepting mouse and keyboard input.

The app includes these main modes:

- laser pointer;
- spotlight;
- magnifier;
- live pinned lens;
- region mask;
- annotations over the screen;
- fading annotations;
- overlay timer;
- screen board;
- black board;
- white board;
- screenshots;
- overlay toolbar.


4. Default global hotkeys
-------------------------

Main modes:

  Ctrl+Alt+L        - toggle laser activation mode Always / Hold
  Ctrl+Alt+D        - toggle drawing over the screen
  Ctrl+Alt+S        - toggle spotlight
  Ctrl+Alt+M        - toggle magnifier
  Ctrl+Alt+P        - select an area for live pinned lens
  Ctrl+Alt+H        - select an area for region mask
  Ctrl+Alt+Shift+H  - clear all region masks
  Ctrl+Alt+F        - toggle fading annotations
  Ctrl+Alt+N        - create a new overlay timer
  Ctrl+Alt+T        - show or hide overlay toolbar
  Ctrl+Alt+C        - screenshot the current monitor
  Ctrl+Alt+G        - toggle screen board
  Ctrl+Alt+B        - toggle black board
  Ctrl+Alt+W        - toggle white board
  Ctrl+Alt+Q        - exit FocusTool

Annotation tools:

  A                 - Arrow
  R                 - Rectangle
  C                 - Ellipse / Circle
  L                 - Line
  P                 - Pencil
  H                 - Highlighter
  T                 - Text
  M                 - Move selection

Colors:

  1                 - color slot 1
  2                 - color slot 2
  3                 - color slot 3
  4                 - color slot 4
  5                 - color slot 5

Line thickness:

  [                 - decrease thickness
  ]                 - increase thickness

Annotation commands:

  Ctrl+Z            - Undo
  Ctrl+Y            - Redo
  Backspace         - delete selected objects
  Delete            - clear annotations
  E                 - alternative clear annotations command
  Esc               - leave annotation / visual mode

Laser in Hold mode:

  XButton2          - default hold button for the laser

XButton2 is usually a side mouse button. It can be changed in Settings.

Important: plain Esc is reserved for leaving visual modes. FocusTool does not
allow assigning Esc to global actions such as Screenshot or Toolbar.


5. Laser pointer
----------------

The laser draws a colored point and a smooth trail above the screen. It is meant
for presentations, video recording, screen sharing, teaching, and explaining
actions on screen.

Activation modes:

- Always on
  The laser is always active.

- Hold key / mouse button
  The laser appears only while the configured key or mouse button is held.
  XButton2 is used by default.

Ctrl+Alt+L toggles Always / Hold mode.

Settings:

- laser color slots;
- current laser color;
- point size;
- trail length;
- fade after release;
- glow effect;
- activation mode;
- hold key / mouse button.


6. Spotlight
------------

Spotlight dims the screen around the cursor and keeps a circular area around the
pointer visible. It is useful when you need to focus attention on one part of
the screen.

Toggle:

  Ctrl+Alt+S

Settings:

- spotlight radius;
- dim amount.

Spotlight can be used separately or together with the laser. Esc is a quick exit
from active visual modes.


7. Magnifier
------------

The magnifier enlarges the area under the cursor. It uses the Windows
Magnification API and works above the desktop.

Toggle:

  Ctrl+Alt+M

Settings:

- magnifier radius;
- zoom factor.

The magnifier tries to exclude its own overlay windows and toolbar from the
captured source so it can show the real desktop underneath. If region masks are
present, masks remain visible in the magnified image so hidden areas cannot be
revealed through zoom.


8. Live Pinned Lens
-------------------

Live Pinned Lens is a live enlarged floating copy of any selected screen
rectangle. Select an area, move the enlarged copy to a free place on screen, and
continue working in the original application while the pinned lens updates in
real time.

Start:

  Ctrl+Alt+P

Then select a rectangle on the screen.

Supported behavior:

- multiple pinned lens windows at the same time;
- drag the lens window with the mouse;
- thin border without the standard system frame;
- right-click context menu;
- Freeze / Resume;
- Zoom in / Zoom out;
- Ctrl + mouse wheel for per-lens zoom;
- Close / Close all.

Laser and annotations are drawn above pinned lens windows. Region masks remain
visible inside the enlarged image so a masked area cannot be revealed through
zoom.


9. Region Mask
--------------

Region Mask hides selected rectangular screen areas. It is useful for private
data, keys, personal information, chat fragments, or any screen area that should
not be shown during a demo or recording.

Start:

  Ctrl+Alt+H

Behavior:

- one selection creates one mask and returns FocusTool to normal passthrough
  mode;
- in Mask mode, existing masks can be moved by dragging their body;
- masks can be resized from corner handles;
- right-click a mask to open its delete menu;
- deleting one mask does not exit Mask mode;
- all masks can be cleared with Ctrl+Alt+Shift+H or from the tray menu;
- new masks use the current mask color slot and opacity from Settings.

Masks are visible in the magnifier, pinned lens, screenshots, and screen board.
This is intentional: zoom or screenshots should not reveal hidden content.


10. Annotation mode
-------------------

Annotation mode allows drawing over the current screen.

Toggle:

  Ctrl+Alt+D

Exit:

  Esc

While annotation mode is active, the overlay accepts mouse input. Normal clicks
to applications below the overlay are temporarily unavailable because the mouse
is used for drawing.

Tools:

- Pen
  Freehand drawing.

- Mark
  Highlighter / marker. Draws a wider translucent stroke.

- Arrow
  Arrow from the mouse-down point to the mouse-up point.

- Line
  Straight line.

- Rect
  Rectangle.

- Oval
  Oval or circle.

- Text
  Text note. Click where the text should start and type from the keyboard.
  Enter finishes text input. Backspace deletes characters. Esc exits the mode.

- Move
  Select and move objects. First drag a selection rectangle, then drag the
  selected objects.

Additional behavior:

- For Line / Rect / Oval, hold Shift while drawing to constrain the shape:
  line angle snapping, square instead of rectangle, circle instead of oval.

- Undo / Redo work for adding, deleting, clearing, and moving objects.

- Delete removes selected objects.

- Clear removes all annotations.


11. Fading annotations
----------------------

Fading annotations are a global mode for new annotations. When enabled, newly
created objects stay fully visible for a configured time and then fade out
smoothly.

Toggle:

  Ctrl+Alt+F

Toolbar:

  Draw -> Fade

The small button next to Fade opens quick controls:

- fully visible time;
- fade duration.

Existing annotations do not change behavior when the mode is toggled. Automatic
removal does not leave empty Undo / Redo steps.


12. Overlay Timer
-----------------

Overlay Timer is a live floating timer above the desktop. It does not open a
separate full-screen mode and does not interrupt the current application.

Create a new timer:

  Ctrl+Alt+N

Supported behavior:

- multiple timers at the same time;
- Countdown, Stopwatch, Clock, and Until time modes;
- drag the timer by its body;
- editable label by double-click;
- editable time by double-click in Countdown and Until time modes;
- size and opacity buttons that appear only while the timer is focused;
- context menu for mode, style, 12/24-hour format, progress bar, label, and
  blink on finish;
- Light, Dark, and Auto themes. Auto follows the Windows app theme.

Keyboard controls while a timer is focused:

  Space             - start / pause
  R                 - reset in Countdown and Stopwatch modes
  Tab               - cycle timer mode
  Left / Right      - adjust time by 1 second
  Up / Down         - adjust time by 1 minute
  Shift+Up/Down     - adjust time by 5 minutes
  Esc               - return focus to the previous window

Active timer settings are saved as defaults for newly created timers. Open timer
windows are not restored after application restart.


13. Screen board
----------------

Screen board captures the current monitor and turns the capture into a drawing
board. It is useful when you need to freeze the current screen state and draw
over it without interacting with the original application.

Toggle:

  Ctrl+Alt+G

On entry:

- the current monitor is captured as an image;
- the overlay enters board mode;
- annotation tools can be used;
- the source application below the board no longer visually changes inside the
  board.

On exit:

- the final board image is saved automatically to Pictures\FocusTool;
- the final image is also copied to the clipboard;
- no notification is shown, to avoid extra noise.

Exit:

  Ctrl+Alt+G again
  Esc
  selecting another mode


14. Black board and white board
-------------------------------

Black board:

  Ctrl+Alt+B

White board:

  Ctrl+Alt+W

These modes show a clean black or white background above the screen and allow
drawing annotations. They are useful for quick explanations without relying on
the current desktop content.

Unlike screen board, black board and white board do not create an automatic
screenshot on entry. They are simply blank drawing backgrounds.


15. Screenshots
---------------

Screenshot current monitor:

  Ctrl+Alt+C

When taking a screenshot:

- the monitor under the cursor is captured;
- the image is saved to Pictures\FocusTool;
- the image is copied to the clipboard;
- the toolbar is temporarily hidden so it does not appear in the capture.

File names use this format:

  FocusTool_yyyy-MM-dd_HH-mm-ss-fff.png

Screen board uses a similar name on exit, with this prefix:

  FocusTool_Board_yyyy-MM-dd_HH-mm-ss-fff.png


16. Overlay toolbar
-------------------

Overlay toolbar is a compact control panel above the screen.

Show / hide:

  Ctrl+Alt+T

Main row:

  Laser  Draw  Spot  Zoom  Pin  Mask  Board  Shot  Timer  ...

Each main group has a small settings button. It opens a contextual row:

- Laser
  Activation mode, color slots, glow, trail length.

- Draw
  Pen, Mark, Arrow, Line, Rect, Oval, Text, Move, color slots, line thickness,
  text size, Fade, Undo, Redo, Clear.

- Spot
  Radius and dim amount.

- Zoom
  Magnifier zoom and radius.

- Pin
  Default zoom/FPS for new pinned lenses and close all.

- Mask
  Mask color slots, opacity, clear.

- Board
  Screen, Black, White.

- Timer
  New timer, Close all.

- ...
  Hide, Settings, Close.

The Hide button collapses the panel into a small FT grip. The grip can be
dragged. A normal click opens the toolbar again.

The toolbar position is remembered for the current app session. When shown
again, it tries to appear where the user moved it, and clamps itself into the
monitor work area when needed.


17. Color slots
---------------

FocusTool uses configurable color slots instead of fixed color names.

Separate slot sets are available for:

- Laser;
- Annotation;
- Region Mask.

Each set contains Color 1..5. Colors are edited in Settings using HEX values:

  #FFFF2020
  #FF2080FF
  #FFFFFFFF

After Apply / OK, the same slots are used by the toolbar and tray menu. If an
older settings file contains a custom current color that is not present in the
slots, FocusTool keeps it by placing it into Color 5.


18. Tray menu
-------------

FocusTool runs from the system tray. The tray context menu includes:

- current annotation mode;
- laser activation mode;
- spotlight;
- magnifier;
- pinned lens;
- region mask;
- fading annotations;
- timer;
- toolbar;
- screenshot;
- screen board;
- black board;
- white board;
- glow;
- tool selection;
- color slot selection;
- undo / redo / clear;
- settings;
- exit.

The tray menu also shows assigned hotkeys on the right side of menu items.


19. Settings
------------

Open Settings from the tray menu:

  Settings...

The settings window has two tabs:

- General
- Shortcuts

General:

- laser color slots;
- laser activation mode;
- point size;
- trail length;
- fade after release;
- glow;
- spotlight radius and dim amount;
- magnifier radius and zoom;
- default pinned lens options;
- region mask color slots;
- region mask opacity;
- annotation color slots;
- line thickness;
- text font size;
- fading annotation options.

Shortcuts:

- all global hotkeys;
- tool hotkeys;
- color hotkeys;
- undo / redo / delete / clear;
- exit from annotation mode.

Hotkey format:

  A
  Ctrl+Alt+D
  Delete
  Esc
  Backspace
  [
  ]
  XButton2

To disable a shortcut, leave the field empty or use:

  None

Mouse buttons cannot be used for global hotkeys. They are allowed for Hold laser.


20. Recording and streaming
---------------------------

FocusTool draws as a desktop overlay. For OBS and similar applications, use
Display Capture / Monitor Capture if you need to record the laser, annotations,
spotlight, magnifier, pinned lens, region masks, timers, and toolbar.

Window Capture usually records only the selected application window and may not
include overlay windows. This is a Windows composition behavior, not a FocusTool
bug.


21. Limitations and notes
-------------------------

- The app is intended for Windows.

- For public distribution, the self-contained build is usually the best choice.

- An unsigned exe may trigger a Windows SmartScreen warning. This is common for
  new unsigned applications. Code signing can be considered for wider public
  distribution.

- If a global hotkey is already used by another application, FocusTool shows a
  notification that the hotkey could not be registered.

- In annotation mode, the overlay captures mouse input. To click normal
  applications again, leave the mode with Esc or the corresponding hotkey.

- Screen board saves the final image on exit. Black board and white board do not
  save automatically.


22. Quick start
---------------

1. Run self-contained\FocusTool.exe.
2. Press Ctrl+Alt+T to open overlay toolbar.
3. Press Ctrl+Alt+D to draw over the screen.
4. Select Pen / Arrow / Rect / Text through the toolbar or hotkeys.
5. Press Esc to leave drawing mode.
6. Press Ctrl+Alt+N to create a floating timer.
7. Press Ctrl+Alt+C to take a screenshot.
8. Press Ctrl+Alt+P, select an area, and try live pinned lens.
9. Press Ctrl+Alt+H to hide an area with region mask.
10. Press Ctrl+Alt+G to create a screen board, draw over the frozen image, and
    leave with Esc. The final image will be saved automatically.


23. Uninstall
-------------

FocusTool does not require installation. To remove it:

1. Close FocusTool from the tray menu or with Ctrl+Alt+Q.
2. Delete the folder with the exe.
3. Optionally delete settings:

   %APPDATA%\FocusTool

4. Optionally delete saved screenshots:

   Pictures\FocusTool
