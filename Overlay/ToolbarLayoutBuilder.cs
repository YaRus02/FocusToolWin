using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FocusTool.Win.Models;
using FocusTool.Win.Services;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace FocusTool.Win.Overlay;

// Window behavior the toolbar buttons trigger, supplied by the window so the
// builder can wire clicks without depending on the window directly.
internal sealed class ToolbarCallbacks
{
    public required Action<InteractionMode> ToggleMode { get; init; }
    public required Action<string> ShowContextualRow { get; init; }
    public required Action ToggleStepOptions { get; init; }
    public required Action ToggleFadeOptions { get; init; }
    public required Action TogglePinOptions { get; init; }
    public required Action CollapseToHandle { get; init; }
    public required Action BeginDragFromHandle { get; init; }
    public required MouseButtonEventHandler CollapsedMouseDown { get; init; }
}

// Builds the entire toolbar visual tree and records the created controls into a
// ToolbarControls holder. Has no runtime/state responsibilities.
internal sealed class ToolbarLayoutBuilder
{
    private readonly FocusToolController _controller;
    private readonly ToolbarCallbacks _callbacks;
    private readonly ToolbarControls _controls;

    public ToolbarLayoutBuilder(FocusToolController controller, ToolbarCallbacks callbacks, ToolbarControls controls)
    {
        _controller = controller;
        _callbacks = callbacks;
        _controls = controls;
    }

    public UIElement Build()
    {
        var root = new Grid();
        _controls.ExpandedRoot = BuildExpandedContent();
        _controls.CollapsedRoot = BuildCollapsedContent();
        _controls.CollapsedRoot.Visibility = Visibility.Collapsed;
        root.Children.Add(_controls.ExpandedRoot);
        root.Children.Add(_controls.CollapsedRoot);
        return root;
    }

    private UIElement BuildExpandedContent()
    {
        var panel = new Border
        {
            Background = ToolbarStyles.PanelBrush,
            BorderBrush = ToolbarStyles.ToolbarBorderBrush,
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

        var primary = ToolbarStyles.CreateRow();
        primary.Children.Add(CreateHandle());
        primary.Children.Add(ToolbarStyles.CreateSeparator());

        _controls.LaserButton = AddSplitButton(primary, "Laser", "Switch laser between Always and Hold", 45, (_, _) => _controller.ToggleLaserActivationMode(), "laser");
        _controls.HighlightButton = AddSplitButton(primary, "Cursor", "Switch cursor highlight between Always and Hold", 48, (_, _) => _controller.ToggleCursorHighlight(), "highlight");
        _controls.SpotButton = AddSplitButton(primary, "Spot", "Toggle spotlight", 39, (_, _) => _controller.ToggleSpotlight(), "spot");
        _controls.ZoomButton = AddSplitButton(primary, "Zoom", "Toggle zoom", 43, (_, _) => _controller.ToggleMagnifierMode(), "zoom");
        primary.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.DrawButton = AddSplitButton(primary, "Draw", "Toggle annotation mode", 42, (_, _) => _callbacks.ToggleMode(InteractionMode.Annotate), "draw");
        primary.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.MaskButton = AddSplitButton(primary, "Mask", "Select regions to cover", 38, (_, _) => _controller.ToggleRegionMask(), "mask");
        _controls.BoardButton = AddSplitButton(primary, "Board", "Screen board, black or white", 48, (_, _) => _callbacks.ShowContextualRow("board"), "board");
        AddSplitButton(primary, "Shot", "Screenshot current monitor", 40, (_, _) => _controller.TakeScreenshot(), "shot");
        primary.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.TimerButton = AddSplitButton(primary, "Timer", "New timer (multiple allowed)", 44, (_, _) => _controller.NewTimer(), "timer");
        primary.Children.Add(ToolbarStyles.CreateSeparator());
        AddSplitButton(primary, "...", "More toolbar actions", 30, (_, _) => _callbacks.ShowContextualRow("more"), "more");

        _controls.ContextualHost = new Border
        {
            Background = ToolbarStyles.ContextBrush,
            BorderBrush = ToolbarStyles.ToolbarBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Left,
            Visibility = Visibility.Collapsed
        };

        stack.Children.Add(primary);
        stack.Children.Add(_controls.ContextualHost);
        panel.Child = stack;
        return panel;
    }

    private void BuildContextualRows()
    {
        _controls.Rows["laser"] = BuildLaserRow();
        _controls.Rows["highlight"] = BuildHighlightRow();
        _controls.Rows["draw"] = BuildDrawRow();
        _controls.Rows["spot"] = BuildSpotRow();
        _controls.Rows["zoom"] = BuildZoomRow();
        _controls.Rows["mask"] = BuildMaskRow();
        _controls.Rows["board"] = BuildBoardRow();
        _controls.Rows["shot"] = BuildShotRow();
        _controls.Rows["timer"] = BuildTimerRow();
        _controls.Rows["more"] = BuildMoreRow();
    }

    private UIElement BuildLaserRow()
    {
        var row = ToolbarStyles.CreateRow();
        _controls.LaserAlwaysButton = ToolbarStyles.CreateButton("Always", "Laser stays on", (_, _) => _controller.SetLaserActivationMode(LaserActivationMode.Always), width: 52);
        _controls.LaserHoldButton = ToolbarStyles.CreateButton("Hold", "Laser only while the hold key is pressed", (_, _) => _controller.SetLaserActivationMode(LaserActivationMode.Hold), width: 44);
        row.Children.Add(_controls.LaserAlwaysButton);
        row.Children.Add(_controls.LaserHoldButton);
        row.Children.Add(ToolbarStyles.CreateSeparator());
        AddColorSwatches(row, _controls.LaserColorButtons, "Laser color", _controller.SetLaserPresetColor);
        row.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.TrailText = CreateStepper(row, "Trail", () => _controller.AdjustLaserTrailLength(-40), () => _controller.AdjustLaserTrailLength(40));
        row.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.GlowButton = ToolbarStyles.CreateButton("Glow", "Toggle laser glow", (_, _) => _controller.SetGlowEnabled(!_controller.Settings.GlowEnabled), width: 44);
        row.Children.Add(_controls.GlowButton);
        return row;
    }

    private UIElement BuildHighlightRow()
    {
        var row = ToolbarStyles.CreateRow();
        _controls.HighlightAlwaysButton = ToolbarStyles.CreateButton("Always", "Highlight stays on", (_, _) => _controller.SetCursorHighlightActivationMode(LaserActivationMode.Always), width: 52);
        _controls.HighlightHoldButton = ToolbarStyles.CreateButton("Hold", "Highlight only while the hold key is pressed", (_, _) => _controller.SetCursorHighlightActivationMode(LaserActivationMode.Hold), width: 44);
        row.Children.Add(_controls.HighlightAlwaysButton);
        row.Children.Add(_controls.HighlightHoldButton);
        row.Children.Add(ToolbarStyles.CreateSeparator());
        AddColorSwatches(row, _controls.HighlightColorButtons, "Highlight color", _controller.SetCursorHighlightPresetColor);
        row.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.HighlightRadiusText = CreateStepper(row, "Size", () => _controller.AdjustCursorHighlightRadius(-2), () => _controller.AdjustCursorHighlightRadius(2));
        row.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.HighlightPulseButton = ToolbarStyles.CreateButton("Pulse", "Toggle click pulse", (_, _) => _controller.SetClickPulseEnabled(!_controller.ClickPulseEnabled), width: 44);
        row.Children.Add(_controls.HighlightPulseButton);
        return row;
    }

    private UIElement BuildDrawRow()
    {
        var stack = new StackPanel { Orientation = WpfOrientation.Vertical };
        var row = ToolbarStyles.CreateRow();
        row.Children.Add(CreateToolButton(AnnotationTool.Pencil, "Pen", "Pencil", 34));
        row.Children.Add(CreateToolButton(AnnotationTool.Highlighter, "Mark", "Highlighter", 40));
        row.Children.Add(ToolbarStyles.CreateSeparator());
        row.Children.Add(CreateToolButton(AnnotationTool.Arrow, "Arrow", "Arrow", 43));
        row.Children.Add(CreateToolButton(AnnotationTool.Line, "Line", "Line", 37));
        row.Children.Add(CreateToolButton(AnnotationTool.Rectangle, "Rect", "Rectangle", 37));
        row.Children.Add(CreateToolButton(AnnotationTool.Ellipse, "Oval", "Ellipse / Circle", 37));
        row.Children.Add(ToolbarStyles.CreateSeparator());
        row.Children.Add(CreateToolButton(AnnotationTool.Text, "Text", "Text", 37));
        _controls.StepButton = ToolbarStyles.CreateButton("Step", "Numbered step marker", (_, _) => _controller.SelectStepTool(), width: 41);
        row.Children.Add(_controls.StepButton);
        _controls.StepOptionsButton = ToolbarStyles.CreateInlineOptionsButton("Step marker shape", _callbacks.ToggleStepOptions);
        row.Children.Add(_controls.StepOptionsButton);
        row.Children.Add(CreateToolButton(AnnotationTool.Move, "Move", "Move selection", 41));
        row.Children.Add(ToolbarStyles.CreateSeparator());
        AddColorSwatches(row, _controls.ColorButtons, "Annotation color", _controller.SetAnnotationPresetColor);
        row.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.ThicknessText = CreateStepper(row, "Size", () => _controller.AdjustAnnotationThickness(-1), () => _controller.AdjustAnnotationThickness(1));
        _controls.FontText = CreateStepper(row, "Text", () => _controller.AdjustAnnotationFontSize(-2), () => _controller.AdjustAnnotationFontSize(2));
        row.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.FadeButton = ToolbarStyles.CreateButton("Fade", "Toggle fading annotations", (_, _) => _controller.ToggleFadingAnnotations(), width: 42);
        row.Children.Add(_controls.FadeButton);
        _controls.FadeOptionsButton = ToolbarStyles.CreateInlineOptionsButton("Fade settings", _callbacks.ToggleFadeOptions);
        row.Children.Add(_controls.FadeOptionsButton);
        row.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.UndoButton = ToolbarStyles.CreateButton("Undo", "Undo", (_, _) => _controller.UndoAnnotation(), width: 44);
        _controls.RedoButton = ToolbarStyles.CreateButton("Redo", "Redo", (_, _) => _controller.RedoAnnotation(), width: 44);
        _controls.ClearButton = ToolbarStyles.CreateButton("Clear", "Clear annotations", (_, _) => _controller.ClearAnnotations(), width: 44);
        row.Children.Add(_controls.UndoButton);
        row.Children.Add(_controls.RedoButton);
        row.Children.Add(_controls.ClearButton);

        _controls.StepOptionsRow = BuildStepOptionsRow();
        _controls.StepOptionsRow.Visibility = Visibility.Collapsed;
        _controls.FadeOptionsRow = BuildFadeOptionsRow();
        _controls.FadeOptionsRow.Visibility = Visibility.Collapsed;

        stack.Children.Add(row);
        stack.Children.Add(_controls.StepOptionsRow);
        stack.Children.Add(_controls.FadeOptionsRow);
        return stack;
    }

    private UIElement BuildStepOptionsRow()
    {
        var row = ToolbarStyles.CreateRow();
        row.Margin = new Thickness(0, 5, 0, 0);
        row.Children.Add(new TextBlock
        {
            Text = "Step",
            Foreground = ToolbarStyles.LabelBrush,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        _controls.StepOvalButton = CreateToolButton(AnnotationTool.StepOval, "Oval", "Click to place numbered oval marker", 45);
        _controls.StepRectButton = CreateToolButton(AnnotationTool.StepRect, "Rect", "Drag numbered rectangle marker", 43);
        row.Children.Add(_controls.StepOvalButton);
        row.Children.Add(_controls.StepRectButton);
        return row;
    }

    private UIElement BuildFadeOptionsRow()
    {
        var row = ToolbarStyles.CreateRow();
        row.Margin = new Thickness(0, 5, 0, 0);
        _controls.FadeVisibleText = CreateStepper(row, "Visible", () => _controller.AdjustFadingAnnotationVisibleMs(-500), () => _controller.AdjustFadingAnnotationVisibleMs(500));
        _controls.FadeDurationText = CreateStepper(row, "Fade", () => _controller.AdjustFadingAnnotationFadeMs(-100), () => _controller.AdjustFadingAnnotationFadeMs(100));
        return row;
    }

    private UIElement BuildSpotRow()
    {
        var row = ToolbarStyles.CreateRow();
        _controls.SpotRegionButton = ToolbarStyles.CreateButton("Region", "Dim everything except selected rectangles", (_, _) => _controller.ToggleRegionSpotlight(), width: 52);
        _controls.ClearSpotRegionsButton = ToolbarStyles.CreateButton("Clear", "Clear region spotlights", (_, _) => _controller.ClearRegionSpotlights(), width: 44);
        row.Children.Add(_controls.SpotRegionButton);
        row.Children.Add(_controls.ClearSpotRegionsButton);
        row.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.SpotRadiusText = CreateStepper(row, "Radius", () => _controller.AdjustSpotlightRadius(-16), () => _controller.AdjustSpotlightRadius(16));
        _controls.SpotDimText = CreateStepper(row, "Dim", () => _controller.AdjustSpotlightOpacity(-0.06), () => _controller.AdjustSpotlightOpacity(0.06));
        return row;
    }

    private UIElement BuildZoomRow()
    {
        var stack = new StackPanel { Orientation = WpfOrientation.Vertical };

        var zoomRow = ToolbarStyles.CreateRow();
        _controls.ZoomZoomText = CreateStepper(zoomRow, "Zoom", () => _controller.AdjustMagnifierZoom(-0.25), () => _controller.AdjustMagnifierZoom(0.25));
        _controls.ZoomRadiusText = CreateStepper(zoomRow, "Radius", () => _controller.AdjustMagnifierRadius(-16), () => _controller.AdjustMagnifierRadius(16));
        zoomRow.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.PinButton = ToolbarStyles.CreateButton("Pin", "Select an area for a live pinned lens", (_, _) => _controller.TogglePinnedLens(), width: 34);
        zoomRow.Children.Add(_controls.PinButton);
        _controls.PinOptionsButton = ToolbarStyles.CreateInlineOptionsButton("Pin settings", _callbacks.TogglePinOptions);
        zoomRow.Children.Add(_controls.PinOptionsButton);

        _controls.PinOptionsRow = BuildPinOptionsRow();
        _controls.PinOptionsRow.Visibility = Visibility.Collapsed;

        stack.Children.Add(zoomRow);
        stack.Children.Add(_controls.PinOptionsRow);
        return stack;
    }

    private UIElement BuildPinOptionsRow()
    {
        var pinRow = ToolbarStyles.CreateRow();
        pinRow.Margin = new Thickness(0, 5, 0, 0);
        pinRow.Children.Add(new TextBlock
        {
            Text = "Pin",
            Foreground = ToolbarStyles.LabelBrush,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });
        _controls.PinZoomText = CreateStepper(pinRow, "Zoom", () => _controller.AdjustPinnedLensZoom(-0.25), () => _controller.AdjustPinnedLensZoom(0.25));
        _controls.PinFpsText = CreateStepper(pinRow, "Fps", () => _controller.AdjustPinnedLensRefreshFps(-5), () => _controller.AdjustPinnedLensRefreshFps(5));
        pinRow.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.ClosePinsButton = ToolbarStyles.CreateButton("Close all", "Close all pinned lenses", (_, _) => _controller.ClosePinnedLenses(), width: 64);
        pinRow.Children.Add(_controls.ClosePinsButton);
        return pinRow;
    }

    private UIElement BuildMaskRow()
    {
        var row = ToolbarStyles.CreateRow();
        AddColorSwatches(row, _controls.MaskColorButtons, "Mask color", _controller.SetRegionMaskPresetColor);
        row.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.MaskOpacityText = CreateStepper(row, "Opacity", () => _controller.AdjustRegionMaskOpacity(-0.1), () => _controller.AdjustRegionMaskOpacity(0.1));
        row.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.ClearMaskButton = ToolbarStyles.CreateButton("Clear", "Clear masks", (_, _) => _controller.ClearRegionMasks(), width: 48);
        row.Children.Add(_controls.ClearMaskButton);
        return row;
    }

    private UIElement BuildBoardRow()
    {
        var row = ToolbarStyles.CreateRow();
        _controls.BoardScreenButton = ToolbarStyles.CreateButton("Screen", "Capture screen board", (_, _) => _controller.ToggleScreenBoard(), width: 52);
        _controls.BoardBlackButton = ToolbarStyles.CreateButton("Black", "Black board", (_, _) => _callbacks.ToggleMode(InteractionMode.BlackScreen), width: 44);
        _controls.BoardWhiteButton = ToolbarStyles.CreateButton("White", "White board", (_, _) => _callbacks.ToggleMode(InteractionMode.WhiteScreen), width: 45);
        row.Children.Add(_controls.BoardScreenButton);
        row.Children.Add(_controls.BoardBlackButton);
        row.Children.Add(_controls.BoardWhiteButton);
        return row;
    }

    private UIElement BuildShotRow()
    {
        var row = ToolbarStyles.CreateRow();
        row.Children.Add(ToolbarStyles.CreateButton("Monitor", "Screenshot current monitor", (_, _) => _controller.TakeScreenshot(), width: 58));
        _controls.ShotRegionButton = ToolbarStyles.CreateButton("Region", "Region screenshot", (_, _) => _controller.TakeRegionScreenshot(), width: 52);
        row.Children.Add(_controls.ShotRegionButton);
        return row;
    }

    private UIElement BuildTimerRow()
    {
        var row = ToolbarStyles.CreateRow();
        row.Children.Add(ToolbarStyles.CreateButton("New timer", "Create a new timer", (_, _) => _controller.NewTimer(), width: 76));
        row.Children.Add(ToolbarStyles.CreateSeparator());
        _controls.CloseTimersButton = ToolbarStyles.CreateButton("Close all", "Close all timers", (_, _) => _controller.CloseAllTimers(), width: 64);
        row.Children.Add(_controls.CloseTimersButton);
        return row;
    }

    private UIElement BuildMoreRow()
    {
        var row = ToolbarStyles.CreateRow();
        row.Children.Add(ToolbarStyles.CreateButton("Hide", "Collapse toolbar to a small grip", (_, _) => _callbacks.CollapseToHandle(), width: 44));
        row.Children.Add(ToolbarStyles.CreateButton("Close", "Close the toolbar (reopen from the tray menu or the toolbar hotkey)", (_, _) => _controller.HideToolbar(), width: 46));
        row.Children.Add(ToolbarStyles.CreateSeparator());
        row.Children.Add(ToolbarStyles.CreateButton("Stage", "Pick a source for Capture Stage", async (_, _) => await _controller.StartCaptureStageWithPickerAsync(), width: 48));
        row.Children.Add(ToolbarStyles.CreateSeparator());
        row.Children.Add(ToolbarStyles.CreateButton("Settings", "Open settings", (_, _) => _controller.ShowSettingsWindow(), width: 60));
        return row;
    }

    private UIElement BuildCollapsedContent()
    {
        var grip = new Border
        {
            Background = ToolbarStyles.PanelBrush,
            BorderBrush = ToolbarStyles.ToolbarBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Width = 28,
            Height = 28,
            Cursor = WpfCursors.SizeAll
        };
        ToolbarStyles.SetToolTip(grip, "Drag to move, click to expand");

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

        _controls.ActiveDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = ToolbarStyles.ActiveBrush,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(_controls.ActiveDot);
        grip.Child = content;

        grip.MouseLeftButtonDown += _callbacks.CollapsedMouseDown;
        return grip;
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
                _callbacks.BeginDragFromHandle();
            }
        };

        return text;
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

        var main = ToolbarStyles.CreateButton(label, tooltip, onBody, width);
        main.Margin = new Thickness(0);
        main.Height = 24;
        container.Children.Add(main);

        if (rowKey is not null)
        {
            var caret = new WpfButton
            {
                Content = "˅",
                Width = width,
                Height = 12,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 1, 0, 0),
                Background = WpfBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = ToolbarStyles.CaretBrush,
                Cursor = WpfCursors.Hand,
                FontSize = 9,
                HorizontalContentAlignment = WpfHorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            ToolbarStyles.SetToolTip(caret, "Show options");
            caret.Click += (_, _) => _callbacks.ShowContextualRow(rowKey);
            container.Children.Add(caret);
            _controls.Carets[rowKey] = caret;
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
            Foreground = ToolbarStyles.LabelBrush,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 3, 0)
        });
        group.Children.Add(ToolbarStyles.CreateStepperButton("-", $"Decrease {label}", (_, _) => onMinus()));
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
        group.Children.Add(ToolbarStyles.CreateStepperButton("+", $"Increase {label}", (_, _) => onPlus()));
        parent.Children.Add(group);
        return value;
    }

    private static void AddColorSwatches(StackPanel parent, List<WpfButton> store, string tooltipPrefix, Action<int> onPick)
    {
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            var button = ToolbarStyles.CreateButton(string.Empty, $"{tooltipPrefix} {i + 1}", (_, _) => onPick(index), width: 25);
            button.Height = 24;
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(1, 0, 1, 0);
            store.Add(button);
            parent.Children.Add(button);
        }
    }

    private WpfButton CreateToolButton(AnnotationTool tool, string text, string tooltip, double width)
    {
        var button = ToolbarStyles.CreateButton(text, tooltip, (_, _) =>
        {
            _controller.SetAnnotationTool(tool);
            if (_controller.Mode == InteractionMode.Passthrough)
            {
                _controller.SetInteractionMode(InteractionMode.Annotate);
            }
        }, width);

        _controls.ToolButtons[tool] = button;
        return button;
    }
}
