using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
using FocusTool.Win.Services;

namespace FocusTool.Win.Overlay;

internal sealed class OverlayToolbarWindow : Window
{
    private readonly FocusToolController _controller;
    private readonly ToolbarPositioner _positioner;
    private readonly ToolbarControls _controls = new();

    private string? _openRowKey;
    private bool _stepOptionsVisible;
    private bool _fadeOptionsVisible;
    private bool _pinOptionsVisible;
    private bool _updating;
    private bool _collapsed;

    public OverlayToolbarWindow(FocusToolController controller)
    {
        _controller = controller;
        _positioner = new ToolbarPositioner(this);
        _controller.StateChanged += OnControllerStateChanged;

        Title = "FocusTool Toolbar";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        Topmost = true;
        MinWidth = 280;
        MaxWidth = 1200;
        Focusable = true;

        Content = new ToolbarLayoutBuilder(_controller, CreateCallbacks(), _controls).Build();
        UpdateState();
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    private ToolbarCallbacks CreateCallbacks() => new()
    {
        ToggleMode = ToggleMode,
        ShowContextualRow = ShowContextualRow,
        ToggleStepOptions = ToggleStepOptions,
        ToggleFadeOptions = ToggleFadeOptions,
        TogglePinOptions = TogglePinOptions,
        CollapseToHandle = CollapseToHandle,
        BeginDragFromHandle = BeginDragFromHandle,
        CollapsedMouseDown = OnCollapsedMouseDown
    };

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
        _positioner.PositionNearCursor();
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

    private void BeginDragFromHandle()
    {
        DragMove();
        _positioner.SaveCurrentPosition();
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

        _positioner.SaveCurrentPosition();
        if (!moved)
        {
            ExpandFromHandle();
        }
    }

    private void CollapseToHandle()
    {
        if (_collapsed)
        {
            return;
        }

        CloseContextualRow();
        _collapsed = true;
        _controls.ExpandedRoot.Visibility = Visibility.Collapsed;
        _controls.CollapsedRoot.Visibility = Visibility.Visible;
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
        _controls.CollapsedRoot.Visibility = Visibility.Collapsed;
        _controls.ExpandedRoot.Visibility = Visibility.Visible;
        UpdateState();
        UpdateLayout();
        _positioner.ClampOntoMonitor();
        ReassertTopmost();
    }

    private void ShowContextualRow(string key)
    {
        if (_openRowKey == key)
        {
            CloseContextualRow();
            return;
        }

        if (!_controls.Rows.TryGetValue(key, out var row))
        {
            return;
        }

        if (key != "draw")
        {
            HideStepOptions();
            HideFadeOptions();
        }

        if (key != "zoom")
        {
            HidePinOptions();
        }

        _openRowKey = key;
        _controls.ContextualHost.Child = row;
        _controls.ContextualHost.Visibility = Visibility.Visible;
        UpdateState();
        UpdateLayout();
        _positioner.ClampOntoMonitor();
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
        HidePinOptions();
        _controls.ContextualHost.Child = null;
        _controls.ContextualHost.Visibility = Visibility.Collapsed;
        foreach (var caret in _controls.Carets.Values)
        {
            caret.Foreground = ToolbarStyles.CaretBrush;
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

        _controls.StepOptionsRow.Visibility = _stepOptionsVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdateState();
        UpdateLayout();
        _positioner.ClampOntoMonitor();
        ReassertTopmost();
    }

    private void HideStepOptions()
    {
        if (_controls.StepOptionsRow is null)
        {
            return;
        }

        _stepOptionsVisible = false;
        _controls.StepOptionsRow.Visibility = Visibility.Collapsed;
    }

    private void ToggleFadeOptions()
    {
        _fadeOptionsVisible = !_fadeOptionsVisible;
        if (_fadeOptionsVisible)
        {
            HideStepOptions();
        }

        _controls.FadeOptionsRow.Visibility = _fadeOptionsVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdateState();
        UpdateLayout();
        _positioner.ClampOntoMonitor();
        ReassertTopmost();
    }

    private void HideFadeOptions()
    {
        if (_controls.FadeOptionsRow is null)
        {
            return;
        }

        _fadeOptionsVisible = false;
        _controls.FadeOptionsRow.Visibility = Visibility.Collapsed;
    }

    private void TogglePinOptions()
    {
        _pinOptionsVisible = !_pinOptionsVisible;
        _controls.PinOptionsRow.Visibility = _pinOptionsVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdateState();
        UpdateLayout();
        _positioner.ClampOntoMonitor();
        ReassertTopmost();
    }

    private void HidePinOptions()
    {
        if (_controls.PinOptionsRow is null)
        {
            return;
        }

        _pinOptionsVisible = false;
        _controls.PinOptionsRow.Visibility = Visibility.Collapsed;
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

            ToolbarStyles.SetButtonActive(_controls.LaserButton, _controller.ActivationMode == LaserActivationMode.Always);
            ToolbarStyles.SetButtonActive(_controls.HighlightButton, _controller.CursorHighlightEnabled);
            ToolbarStyles.SetButtonActive(_controls.DrawButton, _controller.Mode == InteractionMode.Annotate);
            ToolbarStyles.SetButtonActive(_controls.SpotButton, _controller.SpotlightEnabled || _controller.RegionSpotlightActive || _controller.RegionSpotlightSelectionActive);
            ToolbarStyles.SetButtonActive(_controls.ZoomButton, _controller.MagnifierEnabled || _controller.PinnedLensSelectionActive || _controller.PinnedLensActive);
            ToolbarStyles.SetButtonActive(_controls.PinButton, _controller.PinnedLensSelectionActive || _controller.PinnedLensActive);
            ToolbarStyles.SetButtonActive(_controls.MaskButton, _controller.RegionMaskSelectionActive || _controller.RegionMaskActive);
            ToolbarStyles.SetButtonActive(_controls.BoardButton, _controller.Mode is InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen);

            foreach (var (key, caret) in _controls.Carets)
            {
                caret.Foreground = key == _openRowKey ? ToolbarStyles.CaretActiveBrush : ToolbarStyles.CaretBrush;
            }

            ToolbarStyles.SetButtonActive(_controls.LaserAlwaysButton, _controller.ActivationMode == LaserActivationMode.Always);
            ToolbarStyles.SetButtonActive(_controls.LaserHoldButton, _controller.ActivationMode == LaserActivationMode.Hold);
            ToolbarStyles.SetButtonActive(_controls.GlowButton, settings.GlowEnabled);
            _controls.TrailText.Text = $"{settings.TrailLengthMs:0}";
            ToolbarStyles.UpdateColorSwatches(_controls.LaserColorButtons, settings.LaserColorPresets, settings.Color);

            ToolbarStyles.SetButtonActive(_controls.HighlightAlwaysButton, settings.GetCursorHighlightActivationMode() == LaserActivationMode.Always);
            ToolbarStyles.SetButtonActive(_controls.HighlightHoldButton, settings.GetCursorHighlightActivationMode() == LaserActivationMode.Hold);
            ToolbarStyles.UpdateColorSwatches(_controls.HighlightColorButtons, settings.CursorHighlightColorPresets, settings.CursorHighlightColor);
            ToolbarStyles.SetButtonActive(_controls.HighlightPulseButton, settings.ClickPulseEnabled);
            _controls.HighlightRadiusText.Text = $"{settings.CursorHighlightRadius:0}";

            foreach (var (tool, button) in _controls.ToolButtons)
            {
                ToolbarStyles.SetButtonActive(button, _controller.CurrentTool == tool);
            }

            ToolbarStyles.SetButtonActive(_controls.StepButton, IsStepTool(_controller.CurrentTool));
            ToolbarStyles.SetButtonActive(_controls.StepOptionsButton, _stepOptionsVisible);
            ToolbarStyles.SetButtonActive(_controls.StepOvalButton, _controller.CurrentTool == AnnotationTool.StepOval);
            ToolbarStyles.SetButtonActive(_controls.StepRectButton, _controller.CurrentTool == AnnotationTool.StepRect);

            ToolbarStyles.UpdateColorSwatches(_controls.ColorButtons, settings.AnnotationColorPresets, settings.AnnotationColor);
            _controls.ThicknessText.Text = $"{settings.AnnotationThickness:0}";
            _controls.FontText.Text = $"{settings.AnnotationFontSize:0}";
            ToolbarStyles.SetButtonActive(_controls.FadeButton, _controller.FadingAnnotationsEnabled);
            ToolbarStyles.SetButtonActive(_controls.FadeOptionsButton, _fadeOptionsVisible);
            _controls.FadeVisibleText.Text = FormatDurationMs(settings.FadingAnnotationVisibleMs);
            _controls.FadeDurationText.Text = FormatDurationMs(settings.FadingAnnotationFadeMs);
            _controls.UndoButton.IsEnabled = _controller.Annotations.CanUndo;
            _controls.RedoButton.IsEnabled = _controller.Annotations.CanRedo;
            _controls.ClearButton.IsEnabled = _controller.Annotations.Shapes.Count > 0 || _controller.Annotations.Draft is not null;
            ToolbarStyles.SetButtonEnabled(_controls.UndoButton, _controls.UndoButton.IsEnabled);
            ToolbarStyles.SetButtonEnabled(_controls.RedoButton, _controls.RedoButton.IsEnabled);
            ToolbarStyles.SetButtonEnabled(_controls.ClearButton, _controls.ClearButton.IsEnabled);

            _controls.SpotRadiusText.Text = $"{settings.SpotlightRadius:0}";
            _controls.SpotDimText.Text = $"{settings.SpotlightOpacity * 100:0}%";
            ToolbarStyles.SetButtonActive(_controls.SpotRegionButton, _controller.RegionSpotlightSelectionActive || _controller.RegionSpotlightActive);
            _controls.ClearSpotRegionsButton.IsEnabled = _controller.RegionSpotlightActive;
            ToolbarStyles.SetButtonEnabled(_controls.ClearSpotRegionsButton, _controls.ClearSpotRegionsButton.IsEnabled);
            _controls.ZoomZoomText.Text = $"{settings.MagnifierZoom:0.##}x";
            _controls.ZoomRadiusText.Text = $"{settings.MagnifierRadius:0}";
            ToolbarStyles.SetButtonActive(_controls.PinOptionsButton, _pinOptionsVisible);
            _controls.PinZoomText.Text = $"{settings.PinnedLensZoom:0.##}x";
            _controls.PinFpsText.Text = $"{settings.PinnedLensRefreshFps:0}";
            _controls.ClosePinsButton.IsEnabled = _controller.PinnedLensActive;
            ToolbarStyles.SetButtonEnabled(_controls.ClosePinsButton, _controls.ClosePinsButton.IsEnabled);

            ToolbarStyles.UpdateColorSwatches(_controls.MaskColorButtons, settings.RegionMaskColorPresets, settings.RegionMaskColor);
            _controls.MaskOpacityText.Text = $"{settings.RegionMaskOpacity * 100:0}%";
            _controls.ClearMaskButton.IsEnabled = _controller.RegionMaskActive;
            ToolbarStyles.SetButtonEnabled(_controls.ClearMaskButton, _controls.ClearMaskButton.IsEnabled);

            ToolbarStyles.SetButtonActive(_controls.BoardScreenButton, _controller.Mode == InteractionMode.ScreenBoard);
            ToolbarStyles.SetButtonActive(_controls.BoardBlackButton, _controller.Mode == InteractionMode.BlackScreen);
            ToolbarStyles.SetButtonActive(_controls.BoardWhiteButton, _controller.Mode == InteractionMode.WhiteScreen);
            ToolbarStyles.SetButtonActive(_controls.ShotRegionButton, _controller.ScreenshotRegionSelectionActive);

            ToolbarStyles.SetButtonActive(_controls.TimerButton, _controller.TimerActive);
            _controls.CloseTimersButton.IsEnabled = _controller.TimerActive;
            ToolbarStyles.SetButtonEnabled(_controls.CloseTimersButton, _controls.CloseTimersButton.IsEnabled);

            _controls.ActiveDot.Visibility = AnyToolActive() ? Visibility.Visible : Visibility.Collapsed;
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
            || _controller.ClickPulseEnabled
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
}
