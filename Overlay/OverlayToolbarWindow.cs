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
    private static readonly WpfBrush ContextBrush = new SolidColorBrush(MediaColor.FromArgb(238, 38, 38, 38));
    private static readonly WpfBrush ButtonBrush = new SolidColorBrush(MediaColor.FromRgb(48, 48, 48));
    private static readonly WpfBrush ActiveBrush = new SolidColorBrush(MediaColor.FromRgb(32, 128, 255));
    private static readonly WpfBrush ToolbarBorderBrush = new SolidColorBrush(MediaColor.FromArgb(120, 255, 255, 255));
    private static readonly WpfBrush ActiveBorderBrush = new SolidColorBrush(Colors.White);
    private static readonly WpfBrush DisabledBrush = new SolidColorBrush(MediaColor.FromRgb(39, 39, 39));
    private static readonly WpfBrush LabelBrush = new SolidColorBrush(MediaColor.FromArgb(170, 255, 255, 255));
    private static readonly WpfBrush CaretBrush = new SolidColorBrush(MediaColor.FromArgb(130, 255, 255, 255));
    private static readonly WpfBrush CaretActiveBrush = WpfBrushes.White;

    private readonly FocusToolController _controller;
    private readonly Dictionary<AnnotationTool, WpfButton> _toolButtons = [];
    private readonly List<WpfButton> _colorButtons = [];
    private readonly List<WpfButton> _laserColorButtons = [];
    private readonly List<WpfButton> _highlightColorButtons = [];
    private readonly List<WpfButton> _maskColorButtons = [];
    private readonly Dictionary<string, UIElement> _rows = [];
    private readonly Dictionary<string, WpfButton> _carets = [];

    private WpfButton _laserButton = null!;
    private WpfButton _highlightButton = null!;
    private WpfButton _drawButton = null!;
    private WpfButton _spotButton = null!;
    private WpfButton _zoomButton = null!;
    private WpfButton _pinButton = null!;
    private WpfButton _maskButton = null!;
    private WpfButton _boardButton = null!;
    private WpfButton _timerButton = null!;

    private WpfButton _laserAlwaysButton = null!;
    private WpfButton _laserHoldButton = null!;
    private WpfButton _glowButton = null!;
    private TextBlock _trailText = null!;
    private WpfButton _highlightAlwaysButton = null!;
    private WpfButton _highlightHoldButton = null!;
    private WpfButton _highlightPulseButton = null!;
    private TextBlock _highlightRadiusText = null!;
    private TextBlock _thicknessText = null!;
    private TextBlock _fontText = null!;
    private WpfButton _stepButton = null!;
    private WpfButton _stepOptionsButton = null!;
    private UIElement _stepOptionsRow = null!;
    private WpfButton _stepOvalButton = null!;
    private WpfButton _stepRectButton = null!;
    private WpfButton _fadeButton = null!;
    private WpfButton _fadeOptionsButton = null!;
    private UIElement _fadeOptionsRow = null!;
    private TextBlock _fadeVisibleText = null!;
    private TextBlock _fadeDurationText = null!;
    private WpfButton _undoButton = null!;
    private WpfButton _redoButton = null!;
    private WpfButton _clearButton = null!;
    private TextBlock _spotRadiusText = null!;
    private TextBlock _spotDimText = null!;
    private WpfButton _spotRegionButton = null!;
    private WpfButton _clearSpotRegionsButton = null!;
    private TextBlock _zoomZoomText = null!;
    private TextBlock _zoomRadiusText = null!;
    private TextBlock _pinZoomText = null!;
    private TextBlock _pinFpsText = null!;
    private WpfButton _closePinsButton = null!;
    private TextBlock _maskOpacityText = null!;
    private WpfButton _clearMaskButton = null!;
    private WpfButton _boardScreenButton = null!;
    private WpfButton _boardBlackButton = null!;
    private WpfButton _boardWhiteButton = null!;
    private WpfButton _shotRegionButton = null!;
    private WpfButton _closeTimersButton = null!;

    private Border _contextualHost = null!;
    private string? _openRowKey;
    private bool _stepOptionsVisible;
    private bool _fadeOptionsVisible;

    private UIElement _expandedRoot = null!;
    private UIElement _collapsedRoot = null!;
    private Border _activeDot = null!;
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
        MinWidth = 280;
        MaxWidth = 1200;
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

        var stack = new StackPanel { Orientation = WpfOrientation.Vertical };

        BuildContextualRows();

        var primary = CreateRow();
        primary.Children.Add(CreateHandle());
        primary.Children.Add(CreateSeparator());

        _laserButton = AddSplitButton(primary, "Laser", "Toggle laser always/hold", 45, (_, _) => _controller.ToggleLaserActivationMode(), "laser");
        _highlightButton = AddSplitButton(primary, "Cursor", "Toggle cursor highlight", 48, (_, _) => _controller.ToggleCursorHighlight(), "highlight");
        _drawButton = AddSplitButton(primary, "Draw", "Toggle annotation mode", 42, (_, _) => ToggleMode(InteractionMode.Annotate), "draw");
        primary.Children.Add(CreateSeparator());
        _spotButton = AddSplitButton(primary, "Spot", "Toggle spotlight", 39, (_, _) => _controller.ToggleSpotlight(), "spot");
        _zoomButton = AddSplitButton(primary, "Zoom", "Toggle magnifier", 43, (_, _) => _controller.ToggleMagnifierMode(), "zoom");
        _pinButton = AddSplitButton(primary, "Pin", "Select a live pinned lens area", 34, (_, _) => _controller.TogglePinnedLens(), "pin");
        _maskButton = AddSplitButton(primary, "Mask", "Select a region to cover", 38, (_, _) => _controller.ToggleRegionMask(), "mask");
        primary.Children.Add(CreateSeparator());
        _boardButton = AddSplitButton(primary, "Board", "Screen board, black or white", 48, (_, _) => ShowContextualRow("board"), "board");
        AddSplitButton(primary, "Shot", "Screenshot current monitor", 40, (_, _) => _controller.TakeScreenshot(), "shot");
        primary.Children.Add(CreateSeparator());
        _timerButton = AddSplitButton(primary, "Timer", "New timer (multiple allowed)", 44, (_, _) => _controller.NewTimer(), "timer");
        primary.Children.Add(CreateSeparator());
        AddSplitButton(primary, "⋯", "More toolbar actions", 30, (_, _) => ShowContextualRow("more"), "more");

        _contextualHost = new Border
        {
            Background = ContextBrush,
            BorderBrush = ToolbarBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Left,
            Visibility = Visibility.Collapsed
        };

        stack.Children.Add(primary);
        stack.Children.Add(_contextualHost);
        panel.Child = stack;
        return panel;
    }

    private void BuildContextualRows()
    {
        _rows["laser"] = BuildLaserRow();
        _rows["highlight"] = BuildHighlightRow();
        _rows["draw"] = BuildDrawRow();
        _rows["spot"] = BuildSpotRow();
        _rows["zoom"] = BuildZoomRow();
        _rows["pin"] = BuildPinRow();
        _rows["mask"] = BuildMaskRow();
        _rows["board"] = BuildBoardRow();
        _rows["shot"] = BuildShotRow();
        _rows["timer"] = BuildTimerRow();
        _rows["more"] = BuildMoreRow();
    }

    private UIElement BuildLaserRow()
    {
        var row = CreateRow();
        _laserAlwaysButton = CreateButton("Always", "Laser stays on", (_, _) => _controller.SetLaserActivationMode(LaserActivationMode.Always), width: 52);
        _laserHoldButton = CreateButton("Hold", "Laser only while the hold key is pressed", (_, _) => _controller.SetLaserActivationMode(LaserActivationMode.Hold), width: 44);
        row.Children.Add(_laserAlwaysButton);
        row.Children.Add(_laserHoldButton);
        row.Children.Add(CreateSeparator());
        AddColorSwatches(row, _laserColorButtons, "Laser color", _controller.SetLaserPresetColor);
        row.Children.Add(CreateSeparator());
        _glowButton = CreateButton("Glow", "Toggle laser glow", (_, _) => _controller.SetGlowEnabled(!_controller.Settings.GlowEnabled), width: 44);
        row.Children.Add(_glowButton);
        row.Children.Add(CreateSeparator());
        _trailText = CreateStepper(row, "Trail", () => _controller.AdjustLaserTrailLength(-40), () => _controller.AdjustLaserTrailLength(40));
        return row;
    }

    private UIElement BuildHighlightRow()
    {
        var row = CreateRow();
        _highlightAlwaysButton = CreateButton("Always", "Highlight stays on", (_, _) => _controller.SetCursorHighlightActivationMode(LaserActivationMode.Always), width: 52);
        _highlightHoldButton = CreateButton("Hold", "Highlight only while the hold key is pressed", (_, _) => _controller.SetCursorHighlightActivationMode(LaserActivationMode.Hold), width: 44);
        row.Children.Add(_highlightAlwaysButton);
        row.Children.Add(_highlightHoldButton);
        row.Children.Add(CreateSeparator());
        AddColorSwatches(row, _highlightColorButtons, "Highlight color", _controller.SetCursorHighlightPresetColor);
        row.Children.Add(CreateSeparator());
        _highlightRadiusText = CreateStepper(row, "Size", () => _controller.AdjustCursorHighlightRadius(-2), () => _controller.AdjustCursorHighlightRadius(2));
        row.Children.Add(CreateSeparator());
        _highlightPulseButton = CreateButton("Pulse", "Toggle click pulse", (_, _) => _controller.SetCursorHighlightClickPulseEnabled(!_controller.Settings.CursorHighlightClickPulseEnabled), width: 44);
        row.Children.Add(_highlightPulseButton);
        return row;
    }

    private UIElement BuildDrawRow()
    {
        var stack = new StackPanel { Orientation = WpfOrientation.Vertical };
        var row = CreateRow();
        row.Children.Add(CreateToolButton(AnnotationTool.Pencil, "Pen", "Pencil", 34));
        row.Children.Add(CreateToolButton(AnnotationTool.Highlighter, "Mark", "Highlighter", 40));
        row.Children.Add(CreateToolButton(AnnotationTool.Arrow, "Arrow", "Arrow", 43));
        row.Children.Add(CreateToolButton(AnnotationTool.Line, "Line", "Line", 37));
        row.Children.Add(CreateToolButton(AnnotationTool.Rectangle, "Rect", "Rectangle", 37));
        row.Children.Add(CreateToolButton(AnnotationTool.Ellipse, "Oval", "Ellipse / Circle", 37));
        row.Children.Add(CreateToolButton(AnnotationTool.Text, "Text", "Text", 37));
        row.Children.Add(CreateToolButton(AnnotationTool.Move, "Move", "Move selection", 41));
        _stepButton = CreateButton("Step", "Numbered step marker", (_, _) => _controller.SelectStepTool(), width: 41);
        row.Children.Add(_stepButton);
        _stepOptionsButton = CreateInlineOptionsButton("Step marker shape", ToggleStepOptions);
        row.Children.Add(_stepOptionsButton);
        row.Children.Add(CreateSeparator());
        AddColorSwatches(row, _colorButtons, "Annotation color", _controller.SetAnnotationPresetColor);
        row.Children.Add(CreateSeparator());
        _thicknessText = CreateStepper(row, "Size", () => _controller.AdjustAnnotationThickness(-1), () => _controller.AdjustAnnotationThickness(1));
        _fontText = CreateStepper(row, "Text", () => _controller.AdjustAnnotationFontSize(-2), () => _controller.AdjustAnnotationFontSize(2));
        row.Children.Add(CreateSeparator());
        _fadeButton = CreateButton("Fade", "Toggle fading annotations", (_, _) => _controller.ToggleFadingAnnotations(), width: 42);
        row.Children.Add(_fadeButton);
        _fadeOptionsButton = CreateInlineOptionsButton("Fade settings", ToggleFadeOptions);
        row.Children.Add(_fadeOptionsButton);
        row.Children.Add(CreateSeparator());
        _undoButton = CreateButton("Undo", "Undo", (_, _) => _controller.UndoAnnotation(), width: 44);
        _redoButton = CreateButton("Redo", "Redo", (_, _) => _controller.RedoAnnotation(), width: 44);
        _clearButton = CreateButton("Clear", "Clear annotations", (_, _) => _controller.ClearAnnotations(), width: 44);
        row.Children.Add(_undoButton);
        row.Children.Add(_redoButton);
        row.Children.Add(_clearButton);

        _stepOptionsRow = BuildStepOptionsRow();
        _stepOptionsRow.Visibility = Visibility.Collapsed;
        _fadeOptionsRow = BuildFadeOptionsRow();
        _fadeOptionsRow.Visibility = Visibility.Collapsed;

        stack.Children.Add(row);
        stack.Children.Add(_stepOptionsRow);
        stack.Children.Add(_fadeOptionsRow);
        return stack;
    }

    private UIElement BuildStepOptionsRow()
    {
        var row = CreateRow();
        row.Margin = new Thickness(0, 5, 0, 0);
        row.Children.Add(new TextBlock
        {
            Text = "Step",
            Foreground = LabelBrush,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        _stepOvalButton = CreateToolButton(AnnotationTool.StepOval, "Oval", "Click to place numbered oval marker", 45);
        _stepRectButton = CreateToolButton(AnnotationTool.StepRect, "Rect", "Drag numbered rectangle marker", 43);
        row.Children.Add(_stepOvalButton);
        row.Children.Add(_stepRectButton);
        return row;
    }

    private UIElement BuildFadeOptionsRow()
    {
        var row = CreateRow();
        row.Margin = new Thickness(0, 5, 0, 0);
        _fadeVisibleText = CreateStepper(row, "Visible", () => _controller.AdjustFadingAnnotationVisibleMs(-500), () => _controller.AdjustFadingAnnotationVisibleMs(500));
        _fadeDurationText = CreateStepper(row, "Fade", () => _controller.AdjustFadingAnnotationFadeMs(-100), () => _controller.AdjustFadingAnnotationFadeMs(100));
        return row;
    }

    private UIElement BuildSpotRow()
    {
        var row = CreateRow();
        _spotRegionButton = CreateButton("Region", "Dim everything except selected rectangles", (_, _) => _controller.ToggleRegionSpotlight(), width: 52);
        _clearSpotRegionsButton = CreateButton("Clear", "Clear region spotlights", (_, _) => _controller.ClearRegionSpotlights(), width: 44);
        row.Children.Add(_spotRegionButton);
        row.Children.Add(_clearSpotRegionsButton);
        row.Children.Add(CreateSeparator());
        _spotRadiusText = CreateStepper(row, "Radius", () => _controller.AdjustSpotlightRadius(-16), () => _controller.AdjustSpotlightRadius(16));
        _spotDimText = CreateStepper(row, "Dim", () => _controller.AdjustSpotlightOpacity(-0.06), () => _controller.AdjustSpotlightOpacity(0.06));
        return row;
    }

    private UIElement BuildZoomRow()
    {
        var row = CreateRow();
        _zoomZoomText = CreateStepper(row, "Zoom", () => _controller.AdjustMagnifierZoom(-0.25), () => _controller.AdjustMagnifierZoom(0.25));
        _zoomRadiusText = CreateStepper(row, "Radius", () => _controller.AdjustMagnifierRadius(-16), () => _controller.AdjustMagnifierRadius(16));
        return row;
    }

    private UIElement BuildPinRow()
    {
        var row = CreateRow();
        _pinZoomText = CreateStepper(row, "Zoom", () => _controller.AdjustPinnedLensZoom(-0.25), () => _controller.AdjustPinnedLensZoom(0.25));
        _pinFpsText = CreateStepper(row, "Fps", () => _controller.AdjustPinnedLensRefreshFps(-5), () => _controller.AdjustPinnedLensRefreshFps(5));
        row.Children.Add(CreateSeparator());
        _closePinsButton = CreateButton("Close all", "Close all pinned lenses", (_, _) => _controller.ClosePinnedLenses(), width: 64);
        row.Children.Add(_closePinsButton);
        return row;
    }

    private UIElement BuildMaskRow()
    {
        var row = CreateRow();
        AddColorSwatches(row, _maskColorButtons, "Mask color", _controller.SetRegionMaskPresetColor);
        row.Children.Add(CreateSeparator());
        _maskOpacityText = CreateStepper(row, "Opacity", () => _controller.AdjustRegionMaskOpacity(-0.1), () => _controller.AdjustRegionMaskOpacity(0.1));
        row.Children.Add(CreateSeparator());
        _clearMaskButton = CreateButton("Clear", "Clear region masks", (_, _) => _controller.ClearRegionMasks(), width: 48);
        row.Children.Add(_clearMaskButton);
        return row;
    }

    private UIElement BuildBoardRow()
    {
        var row = CreateRow();
        _boardScreenButton = CreateButton("Screen", "Capture screen board", (_, _) => _controller.ToggleScreenBoard(), width: 52);
        _boardBlackButton = CreateButton("Black", "Black board", (_, _) => ToggleMode(InteractionMode.BlackScreen), width: 44);
        _boardWhiteButton = CreateButton("White", "White board", (_, _) => ToggleMode(InteractionMode.WhiteScreen), width: 45);
        row.Children.Add(_boardScreenButton);
        row.Children.Add(_boardBlackButton);
        row.Children.Add(_boardWhiteButton);
        return row;
    }

    private UIElement BuildShotRow()
    {
        var row = CreateRow();
        row.Children.Add(CreateButton("Monitor", "Screenshot current monitor", (_, _) => _controller.TakeScreenshot(), width: 58));
        _shotRegionButton = CreateButton("Region", "Screenshot selected region", (_, _) => _controller.TakeRegionScreenshot(), width: 52);
        row.Children.Add(_shotRegionButton);
        return row;
    }

    private UIElement BuildTimerRow()
    {
        var row = CreateRow();
        row.Children.Add(CreateButton("New timer", "Create a new timer", (_, _) => _controller.NewTimer(), width: 76));
        row.Children.Add(CreateSeparator());
        _closeTimersButton = CreateButton("Close all", "Close all timers", (_, _) => _controller.CloseAllTimers(), width: 64);
        row.Children.Add(_closeTimersButton);
        return row;
    }

    private UIElement BuildMoreRow()
    {
        var row = CreateRow();
        row.Children.Add(CreateButton("Hide", "Collapse toolbar to a small grip", (_, _) => CollapseToHandle(), width: 44));
        row.Children.Add(CreateButton("Close", "Close the toolbar (reopen from the tray menu or the toolbar hotkey)", (_, _) => _controller.HideToolbar(), width: 46));
        row.Children.Add(CreateSeparator());
        row.Children.Add(CreateButton("Settings", "Open settings", (_, _) => _controller.ShowSettingsWindow(), width: 60));
        return row;
    }

    private UIElement BuildCollapsedContent()
    {
        var grip = new Border
        {
            Background = PanelBrush,
            BorderBrush = ToolbarBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Width = 28,
            Height = 28,
            Cursor = WpfCursors.SizeAll,
            ToolTip = "Drag to move, click to expand"
        };

        var content = new Grid();
        content.Children.Add(new TextBlock
        {
            Text = "FT",
            Foreground = WpfBrushes.White,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        });

        _activeDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = ActiveBrush,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(_activeDot);
        grip.Child = content;

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

        CloseContextualRow();
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

    // Adds a split-button (label = action, "v" caret = open/close its contextual row)
    // to the primary row and returns the main button so its active state can be updated.
    private WpfButton AddSplitButton(StackPanel parent, string label, string tooltip, double width, RoutedEventHandler onBody, string? rowKey)
    {
        var container = new StackPanel
        {
            Orientation = WpfOrientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 1, 0)
        };

        var main = CreateButton(label, tooltip, onBody, width);
        main.Margin = new Thickness(0);
        main.Height = 24;
        container.Children.Add(main);

        if (rowKey is not null)
        {
            var caret = new WpfButton
            {
                Content = "˅",
                ToolTip = "Show options",
                Width = width,
                Height = 12,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 1, 0, 0),
                Background = WpfBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = CaretBrush,
                Cursor = WpfCursors.Hand,
                FontSize = 9,
                HorizontalContentAlignment = WpfHorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            caret.Click += (_, _) => ShowContextualRow(rowKey);
            container.Children.Add(caret);
            _carets[rowKey] = caret;
        }
        else
        {
            container.Children.Add(new Border { Height = 12, Margin = new Thickness(0, 1, 0, 0) });
        }

        parent.Children.Add(container);
        return main;
    }

    private TextBlock CreateStepper(StackPanel parent, string label, Action onMinus, Action onPlus)
    {
        var group = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 1, 0)
        };

        group.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = LabelBrush,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 3, 0)
        });
        group.Children.Add(CreateStepperButton("-", $"Decrease {label}", (_, _) => onMinus()));
        var value = new TextBlock
        {
            Foreground = WpfBrushes.White,
            MinWidth = 30,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Margin = new Thickness(1, 0, 1, 0)
        };
        group.Children.Add(value);
        group.Children.Add(CreateStepperButton("+", $"Increase {label}", (_, _) => onPlus()));
        parent.Children.Add(group);
        return value;
    }

    private static WpfButton CreateStepperButton(string text, string tooltip, RoutedEventHandler onClick)
    {
        var button = CreateButton(text, tooltip, onClick, width: 22);
        button.Height = 24;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(0);
        return button;
    }

    private static WpfButton CreateInlineOptionsButton(string tooltip, Action onClick)
    {
        var button = CreateButton("v", tooltip, (_, _) => onClick(), width: 18);
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(0, 0, 1, 0);
        return button;
    }

    private void AddColorSwatches(StackPanel parent, List<WpfButton> store, string tooltipPrefix, Action<int> onPick)
    {
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            var button = CreateButton(string.Empty, $"{tooltipPrefix} {i + 1}", (_, _) => onPick(index), width: 25);
            button.Height = 24;
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(1, 0, 1, 0);
            store.Add(button);
            parent.Children.Add(button);
        }
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
            Height = 24,
            Background = new SolidColorBrush(MediaColor.FromArgb(80, 255, 255, 255)),
            Margin = new Thickness(5, 0, 5, 0)
        };
    }

    private void ShowContextualRow(string key)
    {
        if (_openRowKey == key)
        {
            CloseContextualRow();
            return;
        }

        if (!_rows.TryGetValue(key, out var row))
        {
            return;
        }

        if (key != "draw")
        {
            HideStepOptions();
            HideFadeOptions();
        }

        _openRowKey = key;
        _contextualHost.Child = row;
        _contextualHost.Visibility = Visibility.Visible;
        UpdateState();
        UpdateLayout();
        ClampOntoMonitor();
        ReassertTopmost();
    }

    private void CloseContextualRow()
    {
        if (_openRowKey is null)
        {
            return;
        }

        _openRowKey = null;
        HideStepOptions();
        HideFadeOptions();
        _contextualHost.Child = null;
        _contextualHost.Visibility = Visibility.Collapsed;
        foreach (var caret in _carets.Values)
        {
            caret.Foreground = CaretBrush;
        }

        UpdateLayout();
        ReassertTopmost();
    }

    private void ToggleStepOptions()
    {
        _stepOptionsVisible = !_stepOptionsVisible;
        if (_stepOptionsVisible)
        {
            HideFadeOptions();
        }

        _stepOptionsRow.Visibility = _stepOptionsVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdateState();
        UpdateLayout();
        ClampOntoMonitor();
        ReassertTopmost();
    }

    private void HideStepOptions()
    {
        if (_stepOptionsRow is null)
        {
            return;
        }

        _stepOptionsVisible = false;
        _stepOptionsRow.Visibility = Visibility.Collapsed;
    }

    private void ToggleFadeOptions()
    {
        _fadeOptionsVisible = !_fadeOptionsVisible;
        if (_fadeOptionsVisible)
        {
            HideStepOptions();
        }

        _fadeOptionsRow.Visibility = _fadeOptionsVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdateState();
        UpdateLayout();
        ClampOntoMonitor();
        ReassertTopmost();
    }

    private void HideFadeOptions()
    {
        if (_fadeOptionsRow is null)
        {
            return;
        }

        _fadeOptionsVisible = false;
        _fadeOptionsRow.Visibility = Visibility.Collapsed;
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
            var settings = _controller.Settings;

            SetButtonActive(_laserButton, _controller.ActivationMode == LaserActivationMode.Always);
            SetButtonActive(_highlightButton, _controller.CursorHighlightEnabled);
            SetButtonActive(_drawButton, _controller.Mode == InteractionMode.Annotate);
            SetButtonActive(_spotButton, _controller.SpotlightEnabled || _controller.RegionSpotlightActive || _controller.RegionSpotlightSelectionActive);
            SetButtonActive(_zoomButton, _controller.MagnifierEnabled);
            SetButtonActive(_pinButton, _controller.PinnedLensSelectionActive || _controller.PinnedLensActive);
            SetButtonActive(_maskButton, _controller.RegionMaskSelectionActive || _controller.RegionMaskActive);
            SetButtonActive(_boardButton, _controller.Mode is InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen);

            foreach (var (key, caret) in _carets)
            {
                caret.Foreground = key == _openRowKey ? CaretActiveBrush : CaretBrush;
            }

            SetButtonActive(_laserAlwaysButton, _controller.ActivationMode == LaserActivationMode.Always);
            SetButtonActive(_laserHoldButton, _controller.ActivationMode == LaserActivationMode.Hold);
            SetButtonActive(_glowButton, settings.GlowEnabled);
            _trailText.Text = $"{settings.TrailLengthMs:0}";
            UpdateColorSwatches(_laserColorButtons, settings.LaserColorPresets, settings.Color);

            SetButtonActive(_highlightAlwaysButton, settings.GetCursorHighlightActivationMode() == LaserActivationMode.Always);
            SetButtonActive(_highlightHoldButton, settings.GetCursorHighlightActivationMode() == LaserActivationMode.Hold);
            UpdateColorSwatches(_highlightColorButtons, settings.LaserColorPresets, settings.CursorHighlightColor);
            SetButtonActive(_highlightPulseButton, settings.CursorHighlightClickPulseEnabled);
            _highlightRadiusText.Text = $"{settings.CursorHighlightRadius:0}";

            foreach (var (tool, button) in _toolButtons)
            {
                SetButtonActive(button, _controller.CurrentTool == tool);
            }

            SetButtonActive(_stepButton, IsStepTool(_controller.CurrentTool));
            SetButtonActive(_stepOptionsButton, _stepOptionsVisible);
            SetButtonActive(_stepOvalButton, _controller.CurrentTool == AnnotationTool.StepOval);
            SetButtonActive(_stepRectButton, _controller.CurrentTool == AnnotationTool.StepRect);

            UpdateColorSwatches(_colorButtons, settings.AnnotationColorPresets, settings.AnnotationColor);
            _thicknessText.Text = $"{settings.AnnotationThickness:0}";
            _fontText.Text = $"{settings.AnnotationFontSize:0}";
            SetButtonActive(_fadeButton, _controller.FadingAnnotationsEnabled);
            SetButtonActive(_fadeOptionsButton, _fadeOptionsVisible);
            _fadeVisibleText.Text = FormatDurationMs(settings.FadingAnnotationVisibleMs);
            _fadeDurationText.Text = FormatDurationMs(settings.FadingAnnotationFadeMs);
            _undoButton.IsEnabled = _controller.Annotations.CanUndo;
            _redoButton.IsEnabled = _controller.Annotations.CanRedo;
            _clearButton.IsEnabled = _controller.Annotations.Shapes.Count > 0 || _controller.Annotations.Draft is not null;
            SetButtonEnabled(_undoButton, _undoButton.IsEnabled);
            SetButtonEnabled(_redoButton, _redoButton.IsEnabled);
            SetButtonEnabled(_clearButton, _clearButton.IsEnabled);

            _spotRadiusText.Text = $"{settings.SpotlightRadius:0}";
            _spotDimText.Text = $"{settings.SpotlightOpacity * 100:0}%";
            SetButtonActive(_spotRegionButton, _controller.RegionSpotlightSelectionActive || _controller.RegionSpotlightActive);
            _clearSpotRegionsButton.IsEnabled = _controller.RegionSpotlightActive;
            SetButtonEnabled(_clearSpotRegionsButton, _clearSpotRegionsButton.IsEnabled);
            _zoomZoomText.Text = $"{settings.MagnifierZoom:0.##}x";
            _zoomRadiusText.Text = $"{settings.MagnifierRadius:0}";
            _pinZoomText.Text = $"{settings.PinnedLensZoom:0.##}x";
            _pinFpsText.Text = $"{settings.PinnedLensRefreshFps:0}";
            _closePinsButton.IsEnabled = _controller.PinnedLensActive;
            SetButtonEnabled(_closePinsButton, _closePinsButton.IsEnabled);

            UpdateColorSwatches(_maskColorButtons, settings.RegionMaskColorPresets, settings.RegionMaskColor);
            _maskOpacityText.Text = $"{settings.RegionMaskOpacity * 100:0}%";
            _clearMaskButton.IsEnabled = _controller.RegionMaskActive;
            SetButtonEnabled(_clearMaskButton, _clearMaskButton.IsEnabled);

            SetButtonActive(_boardScreenButton, _controller.Mode == InteractionMode.ScreenBoard);
            SetButtonActive(_boardBlackButton, _controller.Mode == InteractionMode.BlackScreen);
            SetButtonActive(_boardWhiteButton, _controller.Mode == InteractionMode.WhiteScreen);
            SetButtonActive(_shotRegionButton, _controller.ScreenshotRegionSelectionActive);

            SetButtonActive(_timerButton, _controller.TimerActive);
            _closeTimersButton.IsEnabled = _controller.TimerActive;
            SetButtonEnabled(_closeTimersButton, _closeTimersButton.IsEnabled);

            _activeDot.Visibility = AnyToolActive() ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _updating = false;
        }
    }

    private bool AnyToolActive()
    {
        return _controller.ActivationMode == LaserActivationMode.Always
            || _controller.CursorHighlightEnabled
            || _controller.SpotlightEnabled
            || _controller.RegionSpotlightActive
            || _controller.RegionSpotlightSelectionActive
            || _controller.MagnifierEnabled
            || _controller.PinnedLensActive
            || _controller.PinnedLensSelectionActive
            || _controller.RegionMaskActive
            || _controller.RegionMaskSelectionActive
            || _controller.ScreenshotRegionSelectionActive
            || _controller.Mode is InteractionMode.Annotate or InteractionMode.ScreenBoard
                or InteractionMode.BlackScreen or InteractionMode.WhiteScreen
            || _controller.Annotations.Shapes.Count > 0
            || _controller.TimerActive;
    }

    private static string FormatDurationMs(int ms)
    {
        var seconds = ms / 1000.0;
        return $"{seconds:0.#}s";
    }

    private static bool IsStepTool(AnnotationTool tool)
    {
        return tool is AnnotationTool.StepOval or AnnotationTool.StepRect;
    }

    private static void UpdateColorSwatches(List<WpfButton> buttons, IReadOnlyList<string> presets, string currentColor)
    {
        for (var i = 0; i < buttons.Count; i++)
        {
            var colorText = i < presets.Count ? presets[i] : "#FFFFFFFF";
            if (AppSettings.TryParseColor(colorText, out var color))
            {
                buttons[i].Background = new SolidColorBrush(color);
            }

            var selected = string.Equals(colorText, currentColor, StringComparison.OrdinalIgnoreCase);
            buttons[i].BorderBrush = selected ? ActiveBorderBrush : ToolbarBorderBrush;
            buttons[i].BorderThickness = selected ? new Thickness(2) : new Thickness(1);
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
            var fallback = GetCursorScreen().WorkingArea;
            Left = fallback.Left + 8;
            Top = fallback.Top + 18;
            return;
        }

        int left, top;
        if (_hasSavedPosition)
        {
            (left, top) = ClampToWorkingArea(_savedLeft, _savedTop);
        }
        else
        {
            var area = GetCursorScreen().WorkingArea;
            var scale = GetCursorMonitorScale();
            var width = (int)Math.Round((ActualWidth > 1 ? ActualWidth : Width) * scale);
            left = area.Left + (area.Width - width) / 2;
            top = area.Top + (int)Math.Round(18 * scale);
            (left, top) = ClampToWorkingArea(left, top);
        }

        MoveWindowPhysical(left, top);
    }

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
