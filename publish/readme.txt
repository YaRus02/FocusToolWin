FocusTool
=========

FocusTool is a minimal Windows utility for presentations, lessons, reviews,
meetings, screen recordings, remote support, and quick visual explanations. It
adds a quiet overlay above the desktop with a laser pointer, spotlight,
magnifier, live pinned lens, region masks, drawing tools, fading annotations,
screen boards, and quick screenshots.

The main idea is low visual noise and fast control through hotkeys. FocusTool
runs from the system tray and does not show a main window. Most actions are
available through global shortcuts or a compact overlay toolbar.


1. Which file to run
--------------------

There are two release packages:

- self-contained\FocusTool.exe
  Full portable version. It does not require .NET Runtime to be installed on the
  computer. This is the recommended option for most users.

- framework-dependent\FocusTool.exe
  Smaller version. It requires the matching .NET Desktop Runtime to be installed.
  Useful for developers or computers that already have the runtime.

For normal use, run:

  \self-contained\FocusTool.exe

After launch, FocusTool appears in the system tray. If the icon is not visible,
check the hidden tray icons in Windows.


2. Settings and files
---------------------

Settings:

  %APPDATA%\FocusTool\settings.json

Error log:

  %APPDATA%\FocusTool\log.txt

Screenshots and saved Screen Board images:

  Pictures\FocusTool

If Pictures is unavailable, FocusTool uses Documents.


3. How it works
---------------

FocusTool creates transparent overlay windows above your monitors. In the normal
state, they pass clicks through and do not block other applications. When
annotation mode, area selection, mask editing, or a board mode is active, the
overlay accepts mouse and keyboard input.

Supported mode groups:

- laser pointer;
- spotlight;
- magnifier;
- live pinned lens;
- region mask;
- screen annotations;
- fading annotations;
- screen board;
- black board;
- white board;
- screenshots;
- overlay toolbar.


4. Default global hotkeys
-------------------------

Main modes:

  Ctrl+Alt+L        Toggle laser activation mode Always / Hold
  Ctrl+Alt+D        Toggle drawing over the screen
  Ctrl+Alt+S        Toggle spotlight
  Ctrl+Alt+M        Toggle magnifier
  Ctrl+Alt+P        Select area for live pinned lens
  Ctrl+Alt+H        Select area for region mask
  Ctrl+Alt+Shift+H  Clear all region masks
  Ctrl+Alt+F        Toggle fading annotations
  Ctrl+Alt+T        Show / hide overlay toolbar
  Ctrl+Alt+C        Screenshot current monitor
  Ctrl+Alt+G        Toggle Screen Board
  Ctrl+Alt+B        Toggle Black Board
  Ctrl+Alt+W        Toggle White Board
  Ctrl+Alt+Q        Exit FocusTool

Annotations:

  A                 Arrow
  R                 Rectangle
  C                 Ellipse / Circle
  L                 Line
  P                 Pencil
  H                 Highlighter
  T                 Text
  M                 Move selection

Colors:

  1                 Color slot 1
  2                 Color slot 2
  3                 Color slot 3
  4                 Color slot 4
  5                 Color slot 5

Line thickness:

  [                 Decrease thickness
  ]                 Increase thickness

Annotation commands:

  Ctrl+Z            Undo
  Ctrl+Y            Redo
  Backspace         Delete selected objects
  Delete            Clear annotations
  E                 Alternative clear annotations
  Esc               Exit annotation / visual mode

Laser in Hold mode:

  XButton2          Hold laser by default

XButton2 is usually a side mouse button. It can be changed in Settings.

Important: plain Esc is reserved for exiting visual modes. FocusTool does not
allow assigning Esc to global actions such as Screenshot or Toolbar.


5. Laser pointer
----------------

The laser draws a colored point with a smooth trail above the screen. It is meant
for presentations, recordings, demos, and explaining actions on the screen.

Activation modes:

- Always on
  The laser is always active.

- Hold key / mouse button
  The laser appears only while the configured key or mouse button is held.
  XButton2 is used by default.

Ctrl+Alt+L switches between Always and Hold.

Configurable settings:

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

Spotlight dims the screen around the cursor and keeps a circular area visible.
This is useful when the audience should focus on one part of the screen.

Toggle:

  Ctrl+Alt+S

Configurable settings:

- spotlight radius;
- dim amount.

Spotlight can be used alone or together with the laser. Esc exits active visual
modes quickly.


7. Magnifier
------------

The magnifier enlarges the area around the cursor. It uses the Windows
Magnification API and works above the desktop.

Toggle:

  Ctrl+Alt+M

Configurable settings:

- magnifier radius;
- zoom factor.

The magnifier normally avoids showing FocusTool's own overlay windows and toolbar
in its source. If a Region Mask exists, the mask stays visible in the magnified
area so hidden content cannot be revealed through zoom.


8. Live Pinned Lens
-------------------

Live Pinned Lens is a live enlarged floating copy of a selected screen rectangle.
Select an area and FocusTool shows an enlarged copy in a separate floating
window. The source area keeps updating in real time.

Start:

  Ctrl+Alt+P

Then drag a rectangle on the screen.

Supported behavior:

- multiple lens windows at the same time;
- mouse dragging;
- thin border without the standard system frame;
- right-click context menu;
- Freeze / Resume;
- Zoom in / Zoom out;
- Ctrl + mouse wheel to zoom a specific lens;
- Close / Close all.

Laser and annotations are drawn above pinned lenses. Region masks remain visible
inside the enlarged image, so masked content cannot be revealed by zooming.


9. Region Mask
--------------

Region Mask hides selected rectangular areas of the screen. It is useful for
private data, keys, personal information, chat fragments, or anything that should
not appear during a demo or recording.

Start:

  Ctrl+Alt+H

Behavior:

- one selection creates one mask and returns FocusTool to normal passthrough mode;
- in Mask mode, existing masks can be moved by dragging their body;
- masks can be resized from corner handles;
- right-click a mask to open the delete menu;
- deleting one mask does not exit Mask mode;
- all masks can be cleared through Ctrl+Alt+Shift+H or the tray menu;
- new masks use the current mask color slot and opacity from Settings.

Masks are visible in the magnifier, pinned lens, screenshots, and Screen Board so
zoom or screenshots cannot reveal hidden content.


10. Annotation mode
-------------------

Annotation mode lets you draw above the current screen.

Toggle:

  Ctrl+Alt+D

Exit:

  Esc

In annotation mode, the overlay accepts mouse input. Normal clicks on underlying
applications are temporarily blocked because the mouse is used for drawing.

Tools:

- Pen
  Freehand drawing.

- Mark
  Highlighter. Draws a wider semi-transparent line.

- Arrow
  Arrow from press point to release point.

- Line
  Straight line.

- Rect
  Rectangle.

- Oval
  Oval or circle.

- Text
  Text note. Click where you want the text and type from the keyboard. Enter
  commits the text. Backspace deletes characters. Esc exits the mode.

- Move
  Select and move objects. Drag a selection rectangle first, then drag the
  selected area.

Additional behavior:

- Holding Shift while drawing Line / Rect / Oval constrains the shape: line angle
  snapping, square rectangle, or circle.

- Undo / Redo work for adding, deleting, clearing, and moving objects.

- Delete removes selected objects.

- Clear removes annotations.


11. Fading annotations
----------------------

Fading annotations are a global mode for newly created annotations. When enabled,
new objects stay fully visible for a configured time and then fade out smoothly.

Toggle:

  Ctrl+Alt+F

Toolbar:

  Draw -> Fade

The small button next to Fade opens quick settings:

- full visible time;
- fade duration.

Existing annotations do not change behavior when the mode is toggled. Automatic
removal does not leave empty Undo / Redo steps.


12. Screen Board
----------------

Screen Board captures the current monitor and turns it into a background board
for annotations. This is useful when you need to freeze the current screen and
draw over it without interacting with the original application.

Toggle:

  Ctrl+Alt+G

When entering Screen Board:

- the current monitor is captured as an image;
- the overlay switches to board mode;
- annotation tools can be used;
- the original application under the board no longer visually changes inside the board.

When exiting Screen Board:

- the final board image is automatically saved to Pictures\FocusTool;
- the final image is also copied to the clipboard;
- no notification is shown to keep the workflow quiet.

Exit:

  Ctrl+Alt+G again
  Esc
  select another mode


13. Black Board and White Board
-------------------------------

Black Board:

  Ctrl+Alt+B

White Board:

  Ctrl+Alt+W

These modes show a clean black or white full-screen background and allow drawing
annotations. They are useful for quick explanations that do not depend on the
current desktop content.

Unlike Screen Board, Black Board and White Board do not automatically capture the
screen when entered. They are just blank drawing backgrounds.


14. Screenshots
---------------

Screenshot current monitor:

  Ctrl+Alt+C

When taking a screenshot:

- the monitor under the cursor is captured;
- the image is saved to Pictures\FocusTool;
- the image is copied to the clipboard;
- the toolbar is temporarily hidden so it does not appear in the image.

Screenshot file names use this format:

  FocusTool_yyyy-MM-dd_HH-mm-ss-fff.png

Screen Board exits use a similar format with this prefix:

  FocusTool_Board_yyyy-MM-dd_HH-mm-ss-fff.png


15. Overlay toolbar
-------------------

Overlay toolbar is a compact control panel above the screen.

Show / hide:

  Ctrl+Alt+T

Main row:

  Laser  Draw  Spot  Zoom  Pin  Mask  Board  Shot  ...

Main groups have a small settings button that opens a contextual row:

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

- ...
  Hide, Settings, Close.

Hide collapses the toolbar into a small FT grip. The grip can be dragged. A
normal click opens the toolbar again.

The toolbar position is remembered during the app session. When shown again, it
tries to appear where the user moved it and clamps itself to the monitor work
area when needed.


16. Color slots
---------------

FocusTool uses configurable color slots instead of fixed color names.

Separate slot groups exist for:

- Laser;
- Annotation;
- Region Mask.

Each group contains Color 1..5. Colors are edited in Settings through HEX values:

  #FFFF2020
  #FF2080FF
  #FFFFFFFF

After Apply / OK, the same slots are used in the toolbar and tray menu. If an
older settings file contains a custom current color that is not present in the
slots, FocusTool keeps it by placing it into Color 5.


17. Tray menu
-------------

FocusTool runs from the system tray. The tray context menu includes:

- current annotation mode;
- laser activation mode;
- spotlight;
- magnifier;
- pinned lens;
- region mask;
- fading annotations;
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

The tray menu also shows assigned shortcuts next to relevant items.


18. Settings
------------

Open Settings from the tray menu:

  Settings...

The Settings window has two tabs:

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
- default pinned lens settings;
- region mask color slots;
- region mask opacity;
- annotation color slots;
- line thickness;
- text font size;
- fading annotation settings.

Shortcuts:

- all global hotkeys;
- tool hotkeys;
- color hotkeys;
- undo / redo / delete / clear;
- exit visual mode.

Shortcut format examples:

  A
  Ctrl+Alt+D
  Delete
  Esc
  Backspace
  [
  ]
  XButton2

To disable a shortcut, leave the field empty or enter:

  None

Mouse buttons cannot be used for global hotkeys. They are allowed for Hold laser.


19. Recording and streaming
---------------------------

FocusTool draws as a desktop overlay. For OBS and similar tools, use Display
Capture / Monitor Capture if you need to record laser, annotations, spotlight,
magnifier, pinned lenses, region masks, and toolbar.

Window Capture usually captures only the target application window and may not
include overlay windows. This is how Windows window composition works; it is not
a FocusTool error.


20. Notes and limitations
-------------------------

- FocusTool is built for Windows.

- For public distribution, self-contained is the recommended package.

- The executable is currently unsigned. Windows SmartScreen may show a warning
  for a new unsigned app. This is expected, but code signing can be considered
  for wider distribution.

- If a global hotkey is already used by another application, FocusTool shows a
  notification that the hotkey could not be registered.

- In annotation mode, the overlay captures mouse input. Exit with Esc or the
  corresponding hotkey to click normal applications again.

- Screen Board saves the final image when exiting. Black Board and White Board do
  not auto-save on exit.


21. Quick start
---------------

1. Run self-contained\FocusTool.exe.
2. Press Ctrl+Alt+T to open the overlay toolbar.
3. Press Ctrl+Alt+D to draw over the screen.
4. Select Pen / Arrow / Rect / Text through the toolbar or hotkeys.
5. Press Esc to exit drawing mode.
6. Press Ctrl+Alt+C to take a screenshot.
7. Press Ctrl+Alt+P, select an area, and try Live Pinned Lens.
8. Press Ctrl+Alt+H to hide an area with Region Mask.
9. Press Ctrl+Alt+G to create a Screen Board, draw over the captured screen, and
   exit with Esc. The final board image is saved automatically.


22. Uninstall
-------------

FocusTool does not require installation. To remove it:

1. Close FocusTool from the tray menu or press Ctrl+Alt+Q.
2. Delete the folder with the executable.
3. Optionally delete settings:

   %APPDATA%\FocusTool

4. Optionally delete saved screenshots:

   Pictures\FocusTool