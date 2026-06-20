using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
using FocusTool.Win.Services;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using MediaColor = System.Windows.Media.Color;

namespace FocusTool.Win.Overlay;

internal sealed class OverlayToolbarWindow : Window
{
    private static readonly WpfBrush PanelBrush = new SolidColorBrush(MediaColor.FromArgb(238, 30, 30, 30));
    private static readonly WpfBrush ButtonBrush = new SolidColorBrush(MediaColor.FromRgb(48, 48, 48));
    private static readonly WpfBrush ActiveBrush = new SolidColorBrush(MediaColor.FromRgb(32, 128, 255));
    private static readonly WpfBrush ToolbarBorderBrush = new SolidColorBrush(MediaColor.FromArgb(120, 255, 255, 255));
    private static readonly WpfBrush ActiveBorderBrush = new SolidColorBrush(Colors.White);
    private static readonly WpfBrush DisabledBrush = new SolidColorBrush(MediaColor.FromRgb(39, 39, 39));
    private static readonly WpfBrush LabelBrush = new SolidColorBrush(MediaColor.FromArgb(170, 255, 255, 255));

    private readonly FocusToolController _controller;
    private readonly Dictionary<AnnotationTool, WpfButton> _toolButtons = [];
    private readonly List<WpfButton> _colorButtons = [];
    private WpfButton _laserButton = null!;
    private WpfButton _screenshotButton = null!;
    private WpfButton _annotateButton = null!;
    private WpfButton _screenBoardButton = null!;
    private WpfButton _blackButton = null!;
    private WpfButton _whiteButton = null!;
    private WpfButton _spotlightButton = null!;
    private WpfButton _magnifierButton = null!;
    private WpfButton _undoButton = null!;
    private WpfButton _redoButton = null!;
    private WpfButton _clearButton = null!;
    private WpfButton _hideButton = null!;
    private WpfButton _closeButton = null!;
    private UIElement _expandedRoot = null!;
    private UIElement _collapsedRoot = null!;
    private TextBlock _thicknessText = null!;
    private bool _updating;
    private bool _collapsed;
    private bool _hasSavedPosition;
    private int _savedLeft;
    private int _savedTop;

    public OverlayToolbarWindow(FocusToolController controller)
    {
        _controller = controller;
        _controller.StateChanged += OnControllerStateChanged;

        Title = "FocusTool Toolbar";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        Topmost = true;
        MinWidth = 320;
        MaxWidth = 980;
        Focusable = true;

        Content = BuildContent();
        UpdateState();
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public void ShowNearCursor()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (_collapsed)
        {
            ExpandFromHandle();
        }

        UpdateState();
        UpdateLayout();
        PositionNearCursor();
        Activate();
        ReassertTopmost();
    }

    public void ReassertTopmost()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }

    protected override void OnClosed(EventArgs e)
    {
        _controller.StateChanged -= OnControllerStateChanged;
        base.OnClosed(e);
    }

    private UIElement BuildContent()
    {
        var root = new Grid();
        _expandedRoot = BuildExpandedContent();
        _collapsedRoot = BuildCollapsedContent();
        _collapsedRoot.Visibility = Visibility.Collapsed;
        root.Children.Add(_expandedRoot);
        root.Children.Add(_collapsedRoot);
        return root;
    }

    private UIElement BuildExpandedContent()
    {
        var panel = new Border
        {
            Background = PanelBrush,
            BorderBrush = ToolbarBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            SnapsToDevicePixels = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 2,
                Opacity = 0.28
            }
        };

        var rows = new StackPanel
        {
            Orientation = WpfOrientation.Vertical
        };

        _laserButton = CreateButton("Laser", "Toggle laser always/hold", (_, _) => _controller.ToggleLaserActivationMode(), width: 45);
        _screenshotButton = CreateButton("Shot", "Screenshot current monitor", (_, _) => _controller.TakeScreenshot(), width: 40);
        _annotateButton = CreateButton("Draw", "Toggle annotation mode", (_, _) => ToggleMode(InteractionMode.Annotate), width: 42);
        _screenBoardButton = CreateButton("Screen", "Capture screen board", (_, _) => _controller.ToggleScreenBoard(), width: 52);
        _blackButton = CreateButton("Black", "Black board", (_, _) => ToggleMode(InteractionMode.BlackScreen), width: 44);
        _whiteButton = CreateButton("White", "White board", (_, _) => ToggleMode(InteractionMode.WhiteScreen), width: 45);
        _spotlightButton = CreateButton("Spot", "Toggle spotlight", (_, _) => _controller.ToggleSpotlight(), width: 39);
        _magnifierButton = CreateButton("Zoom", "Toggle magnifier", (_, _) => _controller.ToggleMagnifierMode(), width: 43);

        var row1 = CreateRow();
        row1.Children.Add(CreateHandle());
        row1.Children.Add(CreateSeparator());
        row1.Children.Add(CreateLabeledGroup("Modes", _laserButton, _screenshotButton, _annotateButton, _spotlightButton, _magnifierButton));
        row1.Children.Add(CreateSeparator());
        row1.Children.Add(CreateLabeledGroup("Board", _screenBoardButton, _blackButton, _whiteButton));
        row1.Children.Add(CreateSeparator());
        row1.Children.Add(CreateLabeledGroup("Color", CreateColorGroup()));

        var row2 = CreateRow();
        row2.Margin = new Thickness(0, 4, 0, 0);
        row2.Children.Add(CreateLabeledGroup("Size", CreateThicknessGroup()));
        row2.Children.Add(CreateSeparator());
        row2.Children.Add(CreateLabeledGroup(
            "Tools",
            CreateToolButton(AnnotationTool.Pencil, "Pen", "Pencil", width: 34),
            CreateToolButton(AnnotationTool.Highlighter, "Mark", "Highlighter", width: 40),
            CreateToolButton(AnnotationTool.Arrow, "Arrow", "Arrow", width: 43),
            CreateToolButton(AnnotationTool.Line, "Line", "Line", width: 37),
            CreateToolButton(AnnotationTool.Rectangle, "Rect", "Rectangle", width: 37),
            CreateToolButton(AnnotationTool.Ellipse, "Oval", "Ellipse / Circle", width: 37),
            CreateToolButton(AnnotationTool.Text, "Text", "Text", width: 37),
            CreateToolButton(AnnotationTool.Move, "Move", "Move selection", width: 41)));
        row2.Children.Add(CreateSeparator());

        _undoButton = CreateButton("Undo", "Undo", (_, _) => _controller.UndoAnnotation(), width: 44);
        _redoButton = CreateButton("Redo", "Redo", (_, _) => _controller.RedoAnnotation(), width: 44);
        _clearButton = CreateButton("Clear", "Clear annotations", (_, _) => _controller.ClearAnnotations(), width: 44);
        row2.Children.Add(CreateLabeledGroup("Edit", _undoButton, _redoButton, _clearButton));
        row2.Children.Add(CreateSeparator());

        _hideButton = CreateButton("Hide", "Collapse toolbar to a small grip", (_, _) => CollapseToHandle(), width: 40);
        _closeButton = CreateButton("Close", "Close the toolbar (reopen from the tray menu or the toolbar hotkey)", (_, _) => _controller.HideToolbar(), width: 44);
        row2.Children.Add(CreateLabeledGroup("Toolbar", _hideButton, _closeButton));

        rows.Children.Add(row1);
        rows.Children.Add(row2);
        panel.Child = rows;
        return panel;
    }

    private UIElement BuildCollapsedContent()
    {
        // Compact 28px grip: drag to move, click (without dragging) to expand.
        var grip = new Border
        {
            Background = PanelBrush,
            BorderBrush = ToolbarBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Width = 28,
            Height = 28,
            Cursor = WpfCursors.SizeAll,
            ToolTip = "Drag to move, click to expand",
            Child = new TextBlock
            {
                Text = "FT",
                Foreground = WpfBrushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            }
        };

        grip.MouseLeftButtonDown += OnCollapsedMouseDown;
        return grip;
    }

    private void OnCollapsedMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        e.Handled = true;

        // DragMove blocks until the button is released. Compare the window position
        // before/after to tell a real drag from a plain click (which should expand).
        var handle = new WindowInteropHelper(this).Handle;
        var moved = false;
        if (handle != IntPtr.Zero && NativeMethods.GetWindowRect(handle, out var before))
        {
            DragMove();
            moved = NativeMethods.GetWindowRect(handle, out var after)
                && (Math.Abs(after.Left - before.Left) > 3 || Math.Abs(after.Top - before.Top) > 3);
        }
        else
        {
            DragMove();
        }

        SaveCurrentPosition();
        if (!moved)
        {
            ExpandFromHandle();
        }
    }

    private UIElement CreateHandle()
    {
        var text = new TextBlock
        {
            Text = "FocusTool",
            Foreground = WpfBrushes.White,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            MinHeight = 38,
            Margin = new Thickness(3, 0, 6, 0),
            Cursor = WpfCursors.SizeAll
        };

        text.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                SaveCurrentPosition();
            }
        };

        return text;
    }

    private static StackPanel CreateRow()
    {
        return new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void CollapseToHandle()
    {
        if (_collapsed)
        {
            return;
        }

        _collapsed = true;
        _expandedRoot.Visibility = Visibility.Collapsed;
        _collapsedRoot.Visibility = Visibility.Visible;
        UpdateLayout();
        ReassertTopmost();
    }

    private void ExpandFromHandle()
    {
        if (!_collapsed)
        {
            return;
        }

        _collapsed = false;
        _collapsedRoot.Visibility = Visibility.Collapsed;
        _expandedRoot.Visibility = Visibility.Visible;
        UpdateState();
        UpdateLayout();
        ClampOntoMonitor();
        ReassertTopmost();
    }

    private static UIElement CreateLabeledGroup(string label, params UIElement[] children)
    {
        var container = new StackPanel
        {
            Orientation = WpfOrientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center
        };

        container.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = LabelBrush,
            FontSize = 8,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 0, 2)
        });
        container.Children.Add(CreateGroup(children));
        return container;
    }

    private static UIElement CreateGroup(params UIElement[] children)
    {
        var group = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0)
        };

        foreach (var child in children)
        {
            group.Children.Add(child);
        }

        return group;
    }

    private UIElement CreateColorGroup()
    {
        var group = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0)
        };

        for (var i = 0; i < 5; i++)
        {
            var index = i;
            var button = CreateButton(string.Empty, $"Annotation color {i + 1}", (_, _) => _controller.SetAnnotationPresetColor(index), width: 25);
            button.Height = 26;
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(1, 0, 1, 0);
            _colorButtons.Add(button);
            group.Children.Add(button);
        }

        return group;
    }

    private UIElement CreateThicknessGroup()
    {
        var group = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0)
        };

        group.Children.Add(CreateButton("-", "Decrease line thickness", (_, _) => _controller.AdjustAnnotationThickness(-1), width: 29));
        _thicknessText = new TextBlock
        {
            Foreground = WpfBrushes.White,
            MinWidth = 28,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(1, 0, 1, 0)
        };
        group.Children.Add(_thicknessText);
        group.Children.Add(CreateButton("+", "Increase line thickness", (_, _) => _controller.AdjustAnnotationThickness(1), width: 29));
        return group;
    }

    private WpfButton CreateToolButton(AnnotationTool tool, string text, string tooltip, double width)
    {
        var button = CreateButton(text, tooltip, (_, _) =>
        {
            _controller.SetAnnotationTool(tool);
            if (_controller.Mode == InteractionMode.Passthrough)
            {
                _controller.SetInteractionMode(InteractionMode.Annotate);
            }
        }, width);

        _toolButtons[tool] = button;
        return button;
    }

    private static WpfButton CreateButton(string text, string tooltip, RoutedEventHandler onClick, double width = 50)
    {
        var button = new WpfButton
        {
            Content = text,
            ToolTip = tooltip,
            Width = width,
            Height = 26,
            Margin = new Thickness(1, 0, 1, 0),
            Padding = new Thickness(4, 0, 4, 0),
            Background = ButtonBrush,
            BorderBrush = ToolbarBorderBrush,
            BorderThickness = new Thickness(1),
            Foreground = WpfBrushes.White,
            Cursor = WpfCursors.Hand,
            FontSize = 11,
            HorizontalContentAlignment = WpfHorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        button.Click += onClick;
        return button;
    }

    private static UIElement CreateSeparator()
    {
        return new Border
        {
            Width = 1,
            Height = 38,
            Background = new SolidColorBrush(MediaColor.FromArgb(80, 255, 255, 255)),
            Margin = new Thickness(5, 0, 5, 0)
        };
    }

    private void ToggleMode(InteractionMode mode)
    {
        _controller.SetInteractionMode(_controller.Mode == mode ? InteractionMode.Passthrough : mode);
    }

    private void OnControllerStateChanged(object? sender, EventArgs e)
    {
        if (IsVisible)
        {
            UpdateState();
            ReassertTopmost();
        }
    }

    private void UpdateState()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        try
        {
            SetButtonActive(_laserButton, _controller.ActivationMode == LaserActivationMode.Always);
            SetButtonActive(_annotateButton, _controller.Mode == InteractionMode.Annotate);
            SetButtonActive(_screenBoardButton, _controller.Mode == InteractionMode.ScreenBoard);
            SetButtonActive(_blackButton, _controller.Mode == InteractionMode.BlackScreen);
            SetButtonActive(_whiteButton, _controller.Mode == InteractionMode.WhiteScreen);
            SetButtonActive(_spotlightButton, _controller.SpotlightEnabled);
            SetButtonActive(_magnifierButton, _controller.MagnifierEnabled);

            foreach (var (tool, button) in _toolButtons)
            {
                SetButtonActive(button, _controller.CurrentTool == tool);
            }

            UpdateColors();
            _thicknessText.Text = $"{_controller.Settings.AnnotationThickness:0}";

            _undoButton.IsEnabled = _controller.Annotations.CanUndo;
            _redoButton.IsEnabled = _controller.Annotations.CanRedo;
            _clearButton.IsEnabled = _controller.Annotations.Shapes.Count > 0 || _controller.Annotations.Draft is not null;
            SetButtonEnabled(_undoButton, _undoButton.IsEnabled);
            SetButtonEnabled(_redoButton, _redoButton.IsEnabled);
            SetButtonEnabled(_clearButton, _clearButton.IsEnabled);
        }
        finally
        {
            _updating = false;
        }
    }

    private void UpdateColors()
    {
        var presets = _controller.Settings.AnnotationColorPresets;
        for (var i = 0; i < _colorButtons.Count; i++)
        {
            var colorText = i < presets.Count ? presets[i] : "#FFFFFFFF";
            var selected = string.Equals(_controller.Settings.AnnotationColor, colorText, StringComparison.OrdinalIgnoreCase);
            if (AppSettings.TryParseColor(colorText, out var color))
            {
                _colorButtons[i].Background = new SolidColorBrush(color);
            }

            _colorButtons[i].BorderBrush = selected ? ActiveBorderBrush : ToolbarBorderBrush;
            _colorButtons[i].BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        }
    }

    private static void SetButtonActive(WpfButton button, bool active)
    {
        button.Background = active ? ActiveBrush : ButtonBrush;
        button.BorderBrush = active ? ActiveBorderBrush : ToolbarBorderBrush;
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private static void SetButtonEnabled(WpfButton button, bool enabled)
    {
        button.Background = enabled ? ButtonBrush : DisabledBrush;
        button.Foreground = enabled ? WpfBrushes.White : new SolidColorBrush(MediaColor.FromRgb(140, 140, 140));
    }

    private void PositionNearCursor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            // Native handle not realized yet (hit at most once before the first
            // Show); fall back to WPF placement, correct at 100% scale.
            var fallback = GetCursorScreen().WorkingArea;
            Left = fallback.Left + 8;
            Top = fallback.Top + 18;
            return;
        }

        int left, top;
        if (_hasSavedPosition)
        {
            // Restore where the user last dropped it instead of re-centering on every
            // re-show; clamp onto a monitor that still exists.
            (left, top) = ClampToWorkingArea(_savedLeft, _savedTop);
        }
        else
        {
            // First show: centre near the top of the cursor's monitor. Window.Left/Top
            // live in WPF DIPs but Forms.Screen.WorkingArea is physical pixels, so place
            // natively in physical pixels, like the overlay/magnifier do.
            var area = GetCursorScreen().WorkingArea;
            var scale = GetCursorMonitorScale();
            var width = (int)Math.Round((ActualWidth > 1 ? ActualWidth : Width) * scale);
            left = area.Left + (area.Width - width) / 2;
            top = area.Top + (int)Math.Round(18 * scale);
            (left, top) = ClampToWorkingArea(left, top);
        }

        MoveWindowPhysical(left, top);
    }

    // Records the toolbar's current physical position so a later re-show restores it
    // rather than snapping back to the centre.
    private void SaveCurrentPosition()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && NativeMethods.GetWindowRect(handle, out var rect))
        {
            _savedLeft = rect.Left;
            _savedTop = rect.Top;
            _hasSavedPosition = true;
        }
    }

    // Re-clamp onto the current monitor (used after expanding, which can grow a grip
    // parked near an edge off-screen).
    private void ClampOntoMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var rect))
        {
            return;
        }

        var (left, top) = ClampToWorkingArea(rect.Left, rect.Top);
        if (left != rect.Left || top != rect.Top)
        {
            MoveWindowPhysical(left, top);
        }
    }

    private (int Left, int Top) ClampToWorkingArea(int left, int top)
    {
        var scale = GetMonitorScale(left, top);
        var width = (int)Math.Round((ActualWidth > 1 ? ActualWidth : Width) * scale);
        var height = (int)Math.Round((ActualHeight > 1 ? ActualHeight : Height) * scale);
        var area = Forms.Screen.FromPoint(new DrawingPoint(left, top)).WorkingArea;
        var clampedLeft = Math.Clamp(left, area.Left + 8, Math.Max(area.Left + 8, area.Right - width - 8));
        var clampedTop = Math.Clamp(top, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - height - 8));
        return (clampedLeft, clampedTop);
    }

    private void MoveWindowPhysical(int left, int top)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            left,
            top,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }

    private static double GetCursorMonitorScale()
    {
        return NativeMethods.GetCursorPos(out var point) ? GetMonitorScale(point.X, point.Y) : 1.0;
    }

    private static double GetMonitorScale(int x, int y)
    {
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point { X = x, Y = y }, NativeMethods.MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero
            && NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out var dpiX, out _) == 0
            && dpiX > 0)
        {
            return dpiX / 96.0;
        }

        return 1.0;
    }

    private static Forms.Screen GetCursorScreen()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            return Forms.Screen.FromPoint(new DrawingPoint(point.X, point.Y));
        }

        return Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    }
}
