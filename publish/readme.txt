FocusTool
=========

FocusTool is a lightweight Windows utility for presentations, online lessons,
screen recordings, remote support, and quick visual explanations.

It provides an always-on-top overlay with a laser pointer, drawing tools,
spotlight mode, magnifier, screen board, black/white boards, screenshots, a tray
menu, and a compact floating toolbar.


Installation
------------

FocusTool runs on Windows 10 / 11.

Release packages:

- self-contained.zip
  Recommended for most users. It includes the required runtime and does not
  require a separate .NET installation.

- framework-dependent.zip
  Smaller package, but requires the .NET 10 Desktop Runtime.

Unzip the package and run FocusTool.exe. The app starts in the system tray.


Main Modes
----------

Laser
  A virtual laser pointer with a smooth trail. The color, size, glow, trail
  length, and fade timing can be configured in Settings.

Spotlight
  Dims the screen while keeping a circular area around the cursor visible.

Magnifier
  Magnifies the area under the cursor using the Windows Magnification API.

Annotations
  Draw over the screen with pen, marker, arrow, line, rectangle, oval, text,
  selection, and move tools.

Screen board
  Freezes the current screen as a board background so you can draw over it.

Black board / White board
  Clean full-screen drawing backgrounds.

Screenshot
  Captures the current monitor, saves the image, and copies it to the clipboard.


Overlay Toolbar
---------------

The toolbar can be shown or hidden with a hotkey. It contains compact controls
for modes, boards, drawing tools, colors, line size, undo/redo, clear, and hide.

The toolbar is useful when the computer is controlled with a mouse, touch input,
graphics tablet, interactive display, or electronic whiteboard.


Default Global Hotkeys
----------------------

Ctrl+Alt+D  Toggle annotation mode
Ctrl+Alt+L  Toggle laser mode
Ctrl+Alt+S  Toggle spotlight
Ctrl+Alt+M  Toggle magnifier
Ctrl+Alt+T  Toggle overlay toolbar
Ctrl+Alt+C  Screenshot current monitor
Ctrl+Alt+G  Screen board
Ctrl+Alt+B  Black board
Ctrl+Alt+W  White board
Ctrl+Alt+Q  Exit FocusTool
XButton2    Hold laser, when laser activation is set to hold mode


Annotation Hotkeys
------------------

A          Arrow
R          Rectangle
C          Oval / circle
L          Line
P          Pen
H          Marker
T          Text
M          Move/select
1-5        Colors
[ / ]      Line thickness
Ctrl+Z     Undo
Ctrl+Y     Redo
Backspace  Delete selected object
Delete/E   Clear all annotations
Esc        Exit visual mode

Hotkeys can be changed or disabled in Settings from the tray menu.


Recording and Streaming
-----------------------

FocusTool draws its overlays above the desktop as separate top-level windows.
Recording a single application window may not include the overlay.

For OBS and similar tools, use Display Capture to capture the full monitor
together with the overlay. Crop the capture source if you need only a selected
area.


Data Locations
--------------

Settings:
  %APPDATA%\FocusTool\settings.json

Log file:
  %APPDATA%\FocusTool\log.txt

Screenshots and board captures:
  Pictures\FocusTool


License
-------

MIT License
Copyright (c) 2026 YaRus
