using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
using FocusTool.Win.Overlay;
using FocusTool.Win.Tray;

namespace FocusTool.Win.Services;

internal sealed class FocusToolController : IDisposable, IOverlayInputHandler
{
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan FadeInterval = TimeSpan.FromMilliseconds(16);
    // Idle cadence. In the idle path the tick only polls for the laser-hold mouse
    // button (Hold mode) or first cursor movement (Always mode) - there is no
    // animation to drive - so a slower ~8 Hz tick keeps idle CPU/battery low.
    // Worst case it adds ~120 ms before a held laser appears: imperceptible.
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(120);
    private const double MovementThresholdPixels = 0.75;
    private const string ExitVisualShortcut = "Esc";
    private const double WheelAnnotationFontSizeStep = 2;
    private const double WheelAnnotationThicknessStep = 1;
    private const double WheelFocusRadiusStep = 16;
    private const double WheelFocusZoomStep = 0.25;
    private const double WheelSpotlightOpacityStep = 0.06;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SettingsPersistenceController _settingsPersistence;
    private readonly AnnotationDocument _annotations;
    private readonly DispatcherTimer _timer;

    private OverlayManager? _overlayManager;
    private readonly AnnotationInputController _annotationInput;
    private readonly AnnotationMouseController _annotationMouse;
    private readonly PushToAnnotateController _pushToAnnotate;
    private readonly PointerVisualController _pointerVisuals;
    private readonly VisualEffectsController _visualEffects;
    private readonly SettingsCommandController _settingsCommands;
    private readonly OverlayTickController _overlayTick;
    private readonly OverlayToolbarController _toolbar;
    private readonly CaptureController _capture;
    private readonly BoardController _boards;
    private readonly PinnedLensController _pinnedLenses;
    private readonly LiveAdjustmentHudController _liveAdjustmentHud = new();
    private readonly MagnifierController _magnifier;
    private readonly GlobalHotKeyController _hotKeys;
    private readonly MouseHook _liveControlsMouseHook;
    private readonly RegionMaskController _regionMasks = new();
    private readonly RegionSpotlightController _regionSpotlights = new();
    private CaptureStageController? _captureStage;
    private readonly RectSelectionController _rectSelection;
    private readonly RectToolsInputController _rectTools;
    private readonly InteractionModeTransitionController _modeTransitions;
    private TimerController? _timerController;
    private TrayIconController? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private readonly RegionMaskContextMenuController _regionMaskContextMenu;
    private bool _disposed;
    private InteractionMode _mode = InteractionMode.Passthrough;

    public event EventHandler? StateChanged;

    public AppSettings Settings { get; private set; } = new();
    public string SettingsFilePath => _settingsPersistence.SettingsFilePath;
    public InteractionMode Mode => _mode;
    public LaserActivationMode ActivationMode => Settings.GetLaserActivationMode();
    public AnnotationTool CurrentTool => Settings.GetAnnotationTool();
    public AnnotationDocument Annotations => _annotations;
    public bool LaserVisuallyActive => _pointerVisuals.LaserVisuallyActive;
    public bool CursorHighlightEnabled => Settings.GetCursorHighlightActivationMode() == LaserActivationMode.Always;
    public bool ClickPulseEnabled => Settings.ClickPulseEnabled;
    public bool SpotlightEnabled => _visualEffects.SpotlightEnabled;
    public bool MagnifierEnabled => Settings.MagnifierEnabled;
    public bool ToolbarVisible => _toolbar.IsVisible;
    public bool ScreenBoardEnabled => _mode == InteractionMode.ScreenBoard;
    public bool BlackScreenEnabled => _mode == InteractionMode.BlackScreen;
    public bool WhiteScreenEnabled => _mode == InteractionMode.WhiteScreen;
    public bool PinnedLensActive => _pinnedLenses.HasLenses;
    public int PinnedLensCount => _pinnedLenses.Count;
    public bool PinnedLensSelectionActive => _mode == InteractionMode.PinnedLensSelect;
    public bool RegionMaskActive => _regionMasks.HasMasks;
    public int RegionMaskCount => _regionMasks.Count;
    public bool RegionMaskSelectionActive => _mode == InteractionMode.RegionMaskSelect;
    public bool RegionSpotlightActive => _regionSpotlights.HasRegions;
    public int RegionSpotlightCount => _regionSpotlights.Count;
    public bool RegionSpotlightSelectionActive => _mode == InteractionMode.RegionSpotlightSelect;
    public bool ScreenshotRegionSelectionActive => _mode == InteractionMode.ScreenshotRegionSelect;
    public bool FadingAnnotationsEnabled => Settings.FadingAnnotationsEnabled;
    public string MagnifierShortcut => Settings.Shortcuts.ToggleMagnifier;
    public string PushToAnnotateShortcut => Settings.Shortcuts.PushToAnnotate;
    public string CursorHighlightShortcut => Settings.Shortcuts.ToggleCursorHighlight;
    public string PinnedLensShortcut => Settings.Shortcuts.TogglePinnedLens;
    public string RegionMaskShortcut => Settings.Shortcuts.ToggleRegionMask;
    public string ClearRegionMasksShortcut => Settings.Shortcuts.ClearRegionMasks;
    public string FadingAnnotationsShortcut => Settings.Shortcuts.ToggleFadingAnnotations;
    public bool TimerActive => _timerController is { ActiveCount: > 0 };
    public int TimerCount => _timerController?.ActiveCount ?? 0;
    public string TimerShortcut => Settings.Shortcuts.ToggleTimer;
    public string ToolbarShortcut => Settings.Shortcuts.ToggleToolbar;
    public string ScreenshotShortcut => Settings.Shortcuts.TakeScreenshot;
    public string RegionScreenshotShortcut => Settings.Shortcuts.TakeRegionScreenshot;
    public string RegionSpotlightShortcut => Settings.Shortcuts.ToggleRegionSpotlight;
    public string ClearRegionSpotlightsShortcut => Settings.Shortcuts.ClearRegionSpotlights;
    public string ScreenBoardShortcut => Settings.Shortcuts.ToggleScreenBoard;

    public FocusToolController()
    {
        _liveControlsMouseHook = new MouseHook(ex => AppLog.Error("Live controls mouse hook callback failed.", ex));
        _annotations = new AnnotationDocument(NowMs);
        _settingsPersistence = new SettingsPersistenceController(() => Settings, () => _disposed);
        _regionMaskContextMenu = new RegionMaskContextMenuController(
            _regionMasks.GetStyle,
            SetRegionMaskStyle,
            maskId => DeleteRegionMask(maskId, exitMaskMode: false));
        Settings = _settingsPersistence.Load();
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = IdleInterval
        };
        _timer.Tick += OnTimerTick;
        _visualEffects = new VisualEffectsController(
            GetCursorOrNull,
            () => _overlayManager?.Invalidate(),
            (current, previous) => _overlayManager?.InvalidateForCursor(current, previous),
            MovementThresholdPixels);
        _settingsCommands = new SettingsCommandController(
            () => Settings,
            ApplySettings,
            () => _mode,
            SetInteractionMode,
            _annotations,
            _regionMasks,
            _visualEffects,
            () => _overlayManager?.Invalidate());
        _pushToAnnotate = new PushToAnnotateController(
            () => Settings,
            () => _mode,
            SetInteractionMode,
            interval => _timer.Interval = interval,
            () => _annotations.HasTextInput,
            ActiveInterval,
            ClearAnnotations,
            _settingsCommands.SetAnnotationTool,
            _settingsCommands.SelectStepTool,
            _settingsCommands.SetAnnotationPresetColor);
        _annotationInput = new AnnotationInputController(
            _annotations,
            () => Settings,
            () => _pushToAnnotate.Active,
            () => _pushToAnnotate.Shortcut,
            TryGetCursor,
            () => SetInteractionMode(InteractionMode.Passthrough),
            TryCompletePushToAnnotateExit,
            UndoAnnotation,
            RedoAnnotation,
            ClearAnnotations,
            DeleteSelectedAnnotations,
            AdjustAnnotationThickness,
            _settingsCommands.SetAnnotationTool,
            _settingsCommands.SelectStepTool,
            _settingsCommands.SetAnnotationPresetColor);
        _annotationMouse = new AnnotationMouseController(
            _annotations,
            () => Settings,
            () => CurrentTool,
            NowMs,
            TryCompletePushToAnnotateExit,
            MovementThresholdPixels);
        _pointerVisuals = new PointerVisualController(
            () => Settings,
            NowMs,
            () => _disposed,
            TryGetCursor,
            interval => _timer.Interval = interval,
            () => _overlayManager?.Invalidate(),
            (current, previous) => _overlayManager?.InvalidateForCursor(current, previous),
            () => StateChanged?.Invoke(this, EventArgs.Empty),
            MovementThresholdPixels,
            ActiveInterval,
            FadeInterval,
            IdleInterval);
        _pinnedLenses = new PinnedLensController(
            () => Settings,
            GetPinnedLensExcludedWindows,
            () => _disposed,
            CaptureController.WaitForScreenRefreshAsync,
            () => _overlayManager?.ReassertTopmost(),
            (title, text) => _trayIcon?.ShowMessage(title, text),
            ShowPinnedLensZoomHud,
            () => StateChanged?.Invoke(this, EventArgs.Empty));
        _toolbar = new OverlayToolbarController(
            this,
            () => _disposed,
            () => _overlayManager?.ReassertTopmost(),
            () => StateChanged?.Invoke(this, EventArgs.Empty));
        _rectSelection = new RectSelectionController(
            () => _mode,
            () => _disposed,
            () => ToolbarVisible,
            _toolbar.HideTransient,
            ShowToolbar);
        _rectTools = new RectToolsInputController(
            _rectSelection,
            _regionMasks,
            _regionSpotlights,
            () => _mode,
            () => Settings,
            SetInteractionMode,
            () => _overlayManager?.Invalidate(),
            () => StateChanged?.Invoke(this, EventArgs.Empty),
            RegisterHotKeys,
            _pinnedLenses.Open,
            (rect, restoreToolbar) => _ = TakeRegionScreenshotAsync(rect, restoreToolbar),
            ShowRegionMaskContextMenu);
        _capture = new CaptureController(
            () => _disposed,
            () => ToolbarVisible,
            HideToolbar,
            ShowToolbar,
            () => Settings.MagnifierEnabled,
            CloseMagnifierHost,
            UpdateMagnifierHost,
            () => _overlayManager?.TryExcludeVisibleWindowsFromCapture(),
            _pinnedLenses.HideForBoard,
            _pinnedLenses.RestoreAfterBoard,
            () => IsVisualBoardMode(_mode),
            bounds => _overlayManager?.CaptureScreenBoardPrivacySnapshot(bounds)
                ?? new ScreenBoardPrivacySnapshot(
                    layer: null,
                    maskIds: _regionMasks.Masks
                        .Where(mask => mask.Rect.Intersects(bounds))
                        .Select(mask => mask.Id)),
            frame => _overlayManager?.CaptureScreenBoardFrame(frame),
            (title, text) => _trayIcon?.ShowMessage(title, text));
        _boards = new BoardController(
            _capture,
            () => _mode,
            SetInteractionMode);
        _magnifier = new MagnifierController(
            () => Settings,
            () => _disposed,
            () => _capture.IsCaptureInProgress,
            () => IsVisualBoardMode(_mode),
            point => _overlayManager?.GetDpiScaleForPoint(point),
            GetMagnifierExcludedWindows,
            TryGetCursor,
            ApplyMagnifierCursor,
            () => _overlayManager?.ReassertTopmost());
        _modeTransitions = new InteractionModeTransitionController(
            () => _mode,
            mode => _mode = mode,
            () => ActivationMode,
            mode => VisualEffectsController.ShouldRegisterExitVisualHotKey(mode, Settings.MagnifierEnabled, _visualEffects.SpotlightEnabled),
            _pushToAnnotate.CancelIfLeavingAnnotate,
            _annotationMouse.OnLeavingAnnotationInput,
            _rectSelection.ResetRectStateForMode,
            _rectSelection.RestoreToolbarAfterRectSelection,
            ResetSpotlightRegionEditState,
            () =>
            {
                _regionMasks.CancelEdit();
                _regionMasks.ClearSelection();
            },
            _boards.SaveSnapshot,
            _boards.ClearFrame,
            () => _overlayManager?.Show(),
            mode => _overlayManager?.SetInteractionMode(mode),
            () => _overlayManager?.Invalidate(),
            () => Settings.MagnifierEnabled,
            CloseMagnifierHost,
            () => _magnifier.RefreshFromCurrentCursor(forceCursorInvalidation: true, MovementThresholdPixels),
            _pointerVisuals.SetLaserVisualActive,
            RegisterHotKeys,
            () => StateChanged?.Invoke(this, EventArgs.Empty));
        _overlayTick = new OverlayTickController(
            () => _disposed,
            () => _mode,
            () => ActivationMode,
            () => Settings.MagnifierEnabled,
            CanExitPushToAnnotate,
            UpdateFadingAnnotations,
            _modeTransitions.TrackExternalForegroundWindow,
            _pushToAnnotate,
            _pointerVisuals,
            _magnifier,
            _visualEffects,
            () => _overlayManager?.Invalidate(),
            interval => _timer.Interval = interval,
            ActiveInterval,
            FadeInterval,
            MovementThresholdPixels);
        _hotKeys = new GlobalHotKeyController(
            (title, text) => _trayIcon?.ShowMessage(title, text),
            ToggleLaserActivationMode,
            ToggleAnnotateMode,
            StartPushToAnnotate,
            ToggleCursorHighlight,
            ToggleClickPulse,
            ToggleSpotlight,
            ToggleMagnifierMode,
            TogglePinnedLens,
            ToggleRegionMask,
            ClearRegionMasks,
            ToggleRegionSpotlight,
            ClearRegionSpotlights,
            ToggleFadingAnnotations,
            NewTimer,
            ToggleToolbar,
            TakeScreenshot,
            TakeRegionScreenshot,
            ToggleScreenBoard,
            ToggleBlackScreen,
            ToggleWhiteScreen,
            Exit,
            ExitVisualEffects);
        _liveControlsMouseHook.Wheel += OnLiveControlsMouseWheel;
        CacheParsedSettings();

        Settings.SpotlightEnabled = false;
        Settings.MagnifierEnabled = false;
        _visualEffects.SpotlightEnabled = false;

        _annotations.Changed += OnAnnotationsChanged;
        _annotations.DraftProgressed += OnAnnotationDraftProgressed;
    }

    public void NewTimer()
    {
        _timerController?.NewTimer();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CloseAllTimers()
    {
        _timerController?.CloseAll();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTimerActiveCountChanged()
    {
        if (!_disposed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // A timer's mode/duration/style become the default for the next new timer, while the
    // accumulated label history is preserved.
    private void ApplyTimerDefaults(TimerSettings defaults)
    {
        defaults.LabelHistory = [.. Settings.Timer.LabelHistory];
        defaults.Normalize();
        Settings.Timer = defaults;
        if (!_disposed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        _settingsPersistence.SaveDebounced();
    }

    private void AddTimerLabelToHistory(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        var history = Settings.Timer.LabelHistory;
        history.RemoveAll(item => string.Equals(item, label, StringComparison.OrdinalIgnoreCase));
        history.Insert(0, label);
        if (history.Count > 10)
        {
            history.RemoveRange(10, history.Count - 10);
        }

        _settingsPersistence.SaveDebounced();
    }

    public void Start()
    {
        _overlayManager = new OverlayManager(_pointerVisuals.Trail, _annotations, () => Settings, () => _mode, NowMs, GetSpotlightPoint, _pointerVisuals.GetCursorHighlightFrame, () => _boards.Frame, GetRectOverlayVisual, () => _regionMasks.Masks, () => _regionMasks.SelectedMaskId, () => _regionSpotlights.Regions, () => _regionSpotlights.SelectedIndex, GetLiveAdjustmentHudFrame, this, ReassertPinnedLensTopmost, ReassertFloatingChromeTopmost);
        _captureStage = new CaptureStageController(
            CaptureOverlaySnapshot,
            () => new OverlaySnapshotRevision(
                _overlayManager?.CaptureRevision ?? 0,
                _timerController?.CaptureRevision ?? 0));
        _trayIcon = new TrayIconController(this);
        _timerController = new TimerController(NowMs, () => Settings.Timer, ApplyTimerDefaults, AddTimerLabelToHistory, OnTimerActiveCountChanged);
        RegisterHotKeys();
        _pointerVisuals.StartMouseHook();
        UpdateLiveControlsMouseHook();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _overlayManager.Show();
        _timer.Start();
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (_settingsPersistence.WasCreatedOnLoad)
        {
            _trayIcon.ShowMessage(
                "FocusTool is running",
                $"Hold {Settings.Shortcuts.PushToAnnotate} = draw, {Settings.LaserHoldShortcut} = laser, {Settings.CursorHighlightHoldShortcut} = cursor; {Settings.Shortcuts.ToggleClickPulse} = click pulse.");
        }
    }

    public void ToggleAnnotateMode()
    {
        SetInteractionMode(IsAnnotationMode(_mode) ? InteractionMode.Passthrough : InteractionMode.Annotate);
    }

    public void StartPushToAnnotate()
    {
        _pushToAnnotate.Start(_disposed);
    }

    public void ToggleLaserActivationMode()
    {
        SetLaserActivationMode(ActivationMode == LaserActivationMode.Always
            ? LaserActivationMode.Hold
            : LaserActivationMode.Always);
    }

    public void ToggleSpotlight()
    {
        SetSpotlightEnabled(!_visualEffects.SpotlightEnabled);
    }

    public void ToggleMagnifierMode()
    {
        SetMagnifierEnabled(!Settings.MagnifierEnabled);
    }

    public void ToggleCursorHighlight()
    {
        SetCursorHighlightActivationMode(Settings.GetCursorHighlightActivationMode() == LaserActivationMode.Always
            ? LaserActivationMode.Hold
            : LaserActivationMode.Always);
    }

    public void SetCursorHighlightEnabled(bool enabled)
    {
        _settingsCommands.SetCursorHighlightEnabled(enabled);
    }

    public void SetCursorHighlightActivationMode(LaserActivationMode mode)
    {
        _settingsCommands.SetCursorHighlightActivationMode(mode);
    }

    public void SetCursorHighlightPresetColor(int index)
    {
        _settingsCommands.SetCursorHighlightPresetColor(index);
    }

    public void SetClickPulseEnabled(bool enabled)
    {
        _settingsCommands.SetClickPulseEnabled(enabled);
    }

    public void ToggleClickPulse()
    {
        SetClickPulseEnabled(!Settings.ClickPulseEnabled);
    }

    public void AdjustCursorHighlightRadius(double delta)
    {
        _settingsCommands.AdjustCursorHighlightRadius(delta);
    }

    public void AdjustCursorHighlightThickness(double delta)
    {
        _settingsCommands.AdjustCursorHighlightThickness(delta);
    }

    public void TogglePinnedLens()
    {
        if (_mode == InteractionMode.PinnedLensSelect)
        {
            SetInteractionMode(InteractionMode.Passthrough);
            return;
        }

        BeginPinnedLensSelection();
    }

    public void ToggleRegionMask()
    {
        if (_mode == InteractionMode.RegionMaskSelect)
        {
            SetInteractionMode(InteractionMode.Passthrough);
            return;
        }

        BeginRegionMaskSelection();
    }

    public void TakeRegionScreenshot()
    {
        if (_mode == InteractionMode.ScreenshotRegionSelect)
        {
            SetInteractionMode(InteractionMode.Passthrough);
            return;
        }

        BeginScreenshotRegionSelection();
    }

    public void ToggleRegionSpotlight()
    {
        if (_mode == InteractionMode.RegionSpotlightSelect)
        {
            SetInteractionMode(InteractionMode.Passthrough);
            return;
        }

        BeginRegionSpotlightSelection();
    }

    public void ClearRegionSpotlights()
    {
        var hadRegions = _regionSpotlights.HasRegions;
        var wasSelecting = _mode == InteractionMode.RegionSpotlightSelect;
        if (!hadRegions && !wasSelecting)
        {
            return;
        }

        _regionSpotlights.Clear();
        _rectSelection.CancelDraft();
        if (hadRegions)
        {
            RegisterHotKeys();
        }

        if (wasSelecting)
        {
            SetInteractionMode(InteractionMode.Passthrough);
        }
        else
        {
            _overlayManager?.Invalidate();
        }

        if (!_disposed && !wasSelecting)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearRegionMasks()
    {
        var hadMasks = _regionMasks.HasMasks;
        var wasSelecting = _mode == InteractionMode.RegionMaskSelect;
        if (!hadMasks && !wasSelecting)
        {
            return;
        }

        _regionMasks.Clear();
        _rectSelection.CancelDraft();
        if (wasSelecting)
        {
            SetInteractionMode(InteractionMode.Passthrough);
        }
        else
        {
            _overlayManager?.Invalidate();
        }

        if (!_disposed && !wasSelecting)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DeleteRegionMask(int maskId, bool exitMaskMode)
    {
        if (!_regionMasks.Delete(maskId))
        {
            return;
        }

        _rectSelection.CancelDraft();
        _overlayManager?.Invalidate();

        if (exitMaskMode && _mode == InteractionMode.RegionMaskSelect)
        {
            SetInteractionMode(InteractionMode.Passthrough);
            return;
        }

        if (!_disposed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetRegionMaskStyle(int maskId, RegionMaskStyle style)
    {
        if (!string.Equals(Settings.RegionMaskStyle, style.ToString(), StringComparison.Ordinal))
        {
            var updated = Settings.Clone();
            updated.RegionMaskStyle = style.ToString();
            ApplySettings(updated);
        }

        if (_regionMasks.SetStyle(maskId, style))
        {
            _overlayManager?.Invalidate();
        }
    }

    private void ShowRegionMaskContextMenu(ScreenPoint point, int maskId)
    {
        _regionMaskContextMenu.Show(point, maskId);
    }

    public void TakeScreenshot()
    {
        _ = TakeScreenshotAsync();
    }

    public void ToggleScreenBoard()
    {
        _boards.ToggleScreenBoard();
    }

    public void ToggleToolbar()
    {
        _toolbar.Toggle();
    }

    public void ShowToolbar()
    {
        _toolbar.Show();
    }

    public void HideToolbar()
    {
        _toolbar.Hide();
    }

    public void ToggleBlackScreen()
    {
        _boards.ToggleBlackScreen();
    }

    public void ToggleWhiteScreen()
    {
        _boards.ToggleWhiteScreen();
    }

    private void BeginPinnedLensSelection()
    {
        BeginRectSelectionMode(InteractionMode.PinnedLensSelect);
    }

    private void BeginRegionMaskSelection()
    {
        BeginRectSelectionMode(InteractionMode.RegionMaskSelect);
    }

    private void BeginScreenshotRegionSelection()
    {
        BeginRectSelectionMode(InteractionMode.ScreenshotRegionSelect, screenshotRegion: true);
    }

    private void BeginRegionSpotlightSelection()
    {
        if (_visualEffects.SpotlightEnabled)
        {
            _settingsCommands.SetSpotlightEnabled(false);
        }

        _regionSpotlights.SelectLast();
        BeginRectSelectionMode(InteractionMode.RegionSpotlightSelect);
        if (_regionSpotlights.HasRegions)
        {
            _rectSelection.RestoreToolbarAfterRectSelection();
        }
    }

    private void BeginRectSelectionMode(InteractionMode mode, bool screenshotRegion = false)
    {
        if (!IsRectSelectionMode(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Mode is not a rectangle selection mode.");
        }

        if (IsRectSelectionMode(_mode) && _mode != mode)
        {
            SetInteractionMode(InteractionMode.Passthrough);
        }

        _regionMasks.CancelEdit();
        _regionSpotlights.CancelEdit();
        if (screenshotRegion)
        {
            _rectSelection.BeginScreenshotMode();
        }
        else
        {
            _rectSelection.BeginMode(mode);
        }

        SetInteractionMode(mode);
    }

    private async Task TakeScreenshotAsync()
    {
        await _capture.TakeScreenshotAsync();
    }

    private async Task TakeRegionScreenshotAsync(ScreenRect sourceRect, bool restoreToolbar)
    {
        await _capture.TakeRegionScreenshotAsync(sourceRect, restoreToolbar);
    }

    public void SetMagnifierEnabled(bool enabled)
    {
        _settingsCommands.SetMagnifierEnabled(enabled);
    }

    public void SetInteractionMode(InteractionMode mode)
    {
        _modeTransitions.SetMode(mode);
    }

    public void ApplySettings(AppSettings settings)
    {
        var previousActivationMode = ActivationMode;
        var previousCursorHighlightActivationMode = Settings.GetCursorHighlightActivationMode();
        var clickPulseWasEnabled = Settings.ClickPulseEnabled;
        var magnifierWasEnabled = Settings.MagnifierEnabled;
        var globalHotKeysChanged = !GlobalHotKeyController.HaveSameGlobalHotKeys(Settings.Shortcuts, settings.Shortcuts);
        var exitVisualHotKeyWasNeeded = VisualEffectsController.ShouldRegisterExitVisualHotKey(
            _mode,
            Settings.MagnifierEnabled,
            _visualEffects.SpotlightEnabled);
        var magnifierVisualChanged = Math.Abs(Settings.MagnifierRadius - settings.MagnifierRadius) > 0.001
            || Math.Abs(Settings.MagnifierZoom - settings.MagnifierZoom) > 0.001;
        Settings.CopyFrom(settings);
        CacheParsedSettings();
        _pinnedLenses.UpdateRefreshInterval();
        _visualEffects.SpotlightEnabled = Settings.SpotlightEnabled;
        _pointerVisuals.UpdateMouseHook();

        if (Settings.GetCursorHighlightActivationMode() == LaserActivationMode.Always)
        {
            _timer.Interval = ActiveInterval;
            _pointerVisuals.UpdateCursorHighlight(force: true);
        }
        else if (previousCursorHighlightActivationMode == LaserActivationMode.Always || _pointerVisuals.HasCursorHighlightPoint)
        {
            _pointerVisuals.ClearCursorHighlightPoint();
        }

        if (!Settings.ClickPulseEnabled && (clickPulseWasEnabled || _pointerVisuals.HasCursorClickPulses))
        {
            _pointerVisuals.ClearCursorClickPulses();
        }

        if (Settings.MagnifierEnabled)
        {
            SubscribeMagnifierRendering();
            _visualEffects.UpdateSpotlightCursor(force: true);
            if (!magnifierWasEnabled || magnifierVisualChanged)
            {
                _magnifier.RefreshFromCurrentCursor(forceCursorInvalidation: true, MovementThresholdPixels);
            }

            _timer.Interval = ActiveInterval;
        }
        else if (magnifierWasEnabled)
        {
            UnsubscribeMagnifierRendering();
            CloseMagnifierHost();
            if (!_visualEffects.SpotlightEnabled)
            {
                _visualEffects.ClearSpotlightCursor();
            }
        }

        if (_visualEffects.IsSpotlightVisibleInMode(_mode) || Settings.MagnifierEnabled)
        {
            _visualEffects.UpdateSpotlightCursor(force: true);
            _timer.Interval = ActiveInterval;
        }
        else if (_visualEffects.HasSpotlightCursor)
        {
            _visualEffects.ClearSpotlightCursor();
        }

        _settingsPersistence.SaveDebounced();
        var exitVisualHotKeyIsNeeded = VisualEffectsController.ShouldRegisterExitVisualHotKey(
            _mode,
            Settings.MagnifierEnabled,
            _visualEffects.SpotlightEnabled);
        if (globalHotKeysChanged || exitVisualHotKeyWasNeeded != exitVisualHotKeyIsNeeded)
        {
            RegisterHotKeys();
        }

        _pointerVisuals.RefreshLaserAfterSettingsApplied(previousActivationMode);
        UpdateLiveControlsMouseHook();

        _overlayManager?.Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPresetColor(string color)
    {
        _settingsCommands.SetPresetColor(color);
    }

    public void SetLaserPresetColor(int index)
    {
        _settingsCommands.SetLaserPresetColor(index);
    }

    public void SetAnnotationColor(string color)
    {
        _settingsCommands.SetAnnotationColor(color);
    }

    public void SetAnnotationPresetColor(int index)
    {
        _settingsCommands.SetAnnotationPresetColor(index);
    }

    public void SetAnnotationTool(AnnotationTool tool)
    {
        _settingsCommands.SetAnnotationTool(tool);
    }

    public void SelectStepTool()
    {
        _settingsCommands.SelectStepTool();
    }

    public void AdjustAnnotationThickness(double delta)
    {
        _settingsCommands.AdjustAnnotationThickness(delta);
    }

    public void AdjustAnnotationFontSize(double delta)
    {
        _settingsCommands.AdjustAnnotationFontSize(delta);
    }

    public void AdjustLaserTrailLength(int delta)
    {
        _settingsCommands.AdjustLaserTrailLength(delta);
    }

    public void AdjustSpotlightRadius(double delta)
    {
        _settingsCommands.AdjustSpotlightRadius(delta);
    }

    public void AdjustSpotlightOpacity(double delta)
    {
        _settingsCommands.AdjustSpotlightOpacity(delta);
    }

    public void AdjustMagnifierZoom(double delta)
    {
        _settingsCommands.AdjustMagnifierZoom(delta);
    }

    public void AdjustMagnifierRadius(double delta)
    {
        _settingsCommands.AdjustMagnifierRadius(delta);
    }

    public void AdjustPinnedLensZoom(double delta)
    {
        _settingsCommands.AdjustPinnedLensZoom(delta);
    }

    public void AdjustPinnedLensRefreshFps(int delta)
    {
        _settingsCommands.AdjustPinnedLensRefreshFps(delta);
    }

    public void AdjustRegionMaskOpacity(double delta)
    {
        _settingsCommands.AdjustRegionMaskOpacity(delta);
    }

    public void SetRegionMaskColor(string color)
    {
        _settingsCommands.SetRegionMaskColor(color);
    }

    public void SetRegionMaskPresetColor(int index)
    {
        _settingsCommands.SetRegionMaskPresetColor(index);
    }

    public void SetGlowEnabled(bool enabled)
    {
        _settingsCommands.SetGlowEnabled(enabled);
    }

    public void ToggleFadingAnnotations()
    {
        SetFadingAnnotationsEnabled(!Settings.FadingAnnotationsEnabled);
    }

    public void SetFadingAnnotationsEnabled(bool enabled)
    {
        _settingsCommands.SetFadingAnnotationsEnabled(enabled);
    }

    public void AdjustFadingAnnotationVisibleMs(int deltaMs)
    {
        _settingsCommands.AdjustFadingAnnotationVisibleMs(deltaMs);
    }

    public void AdjustFadingAnnotationFadeMs(int deltaMs)
    {
        _settingsCommands.AdjustFadingAnnotationFadeMs(deltaMs);
    }

    public void SetLaserActivationMode(LaserActivationMode mode)
    {
        _settingsCommands.SetLaserActivationMode(mode);
    }

    public void SetSpotlightEnabled(bool enabled)
    {
        _settingsCommands.SetSpotlightEnabled(enabled);
    }

    public void UndoAnnotation()
    {
        _annotations.Undo();
    }

    public void RedoAnnotation()
    {
        _annotations.Redo();
    }

    public void ClearAnnotations()
    {
        _annotations.Clear();
    }

    public void DeleteSelectedAnnotations()
    {
        _annotations.DeleteSelection();
    }

    public void ShowSettingsWindow()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            if (_settingsWindow.WindowState == System.Windows.WindowState.Minimized)
            {
                _settingsWindow.WindowState = System.Windows.WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(Settings.Clone(), ApplyPersistentSettings, SettingsFilePath);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplyPersistentSettings(AppSettings settings)
    {
        settings.SpotlightEnabled = _visualEffects.SpotlightEnabled;
        settings.MagnifierEnabled = Settings.MagnifierEnabled;
        ApplySettings(settings);
    }

    public void Exit()
    {
        System.Windows.Application.Current.Shutdown();
    }

    public void HandleOverlayMouseDown(ScreenPoint point, MouseButton button, ModifierKeys modifiers)
    {
        if (IsRectSelectionMode(_mode))
        {
            _rectTools.HandleMouseDown(point, button);
            return;
        }

        if (!IsAnnotationMode(_mode) || button != MouseButton.Left)
        {
            return;
        }

        _annotationMouse.HandleMouseDown(point);
    }

    public void HandleOverlayMouseMove(ScreenPoint point, ModifierKeys modifiers)
    {
        if (IsRectSelectionMode(_mode))
        {
            _rectTools.HandleMouseMove(point);
            return;
        }

        if (!IsAnnotationMode(_mode))
        {
            return;
        }

        _annotationMouse.HandleMouseMove(point, modifiers);
    }

    public void HandleOverlayMouseUp(ScreenPoint point, MouseButton button, ModifierKeys modifiers)
    {
        if (IsRectSelectionMode(_mode))
        {
            _rectTools.HandleMouseUp(point, button);
            return;
        }

        if (!IsAnnotationMode(_mode) || button != MouseButton.Left)
        {
            return;
        }

        _annotationMouse.HandleMouseUp(point, modifiers);
    }

    public bool HandleOverlayMouseWheel(ScreenPoint point, int delta, ModifierKeys modifiers)
    {
        if (TryHandleLiveControlMouseWheel(point, delta, modifiers))
        {
            return true;
        }

        if (IsRectSelectionMode(_mode))
        {
            return _rectTools.HandleMouseWheel(point, delta, modifiers);
        }

        return false;
    }

    public void HandleOverlayCaptureLost()
    {
        if (IsRectSelectionMode(_mode))
        {
            _rectTools.HandleCaptureLost();
            return;
        }

        _annotationMouse.HandleCaptureLost();
    }

    public bool HandleOverlayKeyDown(Key key, ModifierKeys modifiers)
    {
        if (IsRectSelectionMode(_mode))
        {
            return _rectTools.HandleKeyDown(key, modifiers);
        }

        if (!IsAnnotationMode(_mode))
        {
            return false;
        }

        return _annotationInput.HandleKeyDown(key, modifiers);
    }

    public void HandleOverlayTextInput(string text)
    {
        if (IsAnnotationMode(_mode) && _annotations.HasTextInput)
        {
            _annotationInput.HandleTextInput(text);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;

        _pointerVisuals.Dispose();
        UnsubscribeMagnifierRendering();
        _annotations.Changed -= OnAnnotationsChanged;
        _annotations.DraftProgressed -= OnAnnotationDraftProgressed;
        _regionMaskContextMenu.Dispose();
        _liveControlsMouseHook.Wheel -= OnLiveControlsMouseWheel;
        _liveControlsMouseHook.Dispose();
        _hotKeys.Dispose();
        CloseMagnifierHost();
        _captureStage?.Dispose();
        _pinnedLenses.Dispose();
        _timerController?.Dispose();
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        _toolbar.Close();
        _overlayManager?.Dispose();
        _settingsPersistence.Flush();
        _settingsPersistence.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _overlayTick.Tick();
        UpdateLiveAdjustmentHud();
    }

    private LiveAdjustmentHudFrame? GetLiveAdjustmentHudFrame()
    {
        return _liveAdjustmentHud.GetFrame(NowMs());
    }

    private void ShowLiveAdjustmentHud(ScreenPoint point, string text)
    {
        _liveAdjustmentHud.Show(text, point, NowMs());
        _timer.Interval = FadeInterval;
        _overlayManager?.Invalidate();
    }

    private void ShowPinnedLensZoomHud(ScreenPoint point, double zoom)
    {
        ShowLiveAdjustmentHud(point, $"Lens {FormatZoom(zoom)}");
    }

    private void UpdateLiveAdjustmentHud()
    {
        var nowMs = NowMs();
        if (_liveAdjustmentHud.IsVisible(nowMs))
        {
            _timer.Interval = FadeInterval;
            _overlayManager?.Invalidate();
            return;
        }

        if (_liveAdjustmentHud.ClearExpired(nowMs))
        {
            _overlayManager?.Invalidate();
        }
    }

    private void OnLiveControlsMouseWheel(object? sender, MouseHookWheelEventArgs e)
    {
        if (e.Delta == 0)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            e.Handled = dispatcher.Invoke(() => TryHandleLiveControlMouseWheel(e.Point, e.Delta, Keyboard.Modifiers));
            return;
        }

        e.Handled = TryHandleLiveControlMouseWheel(e.Point, e.Delta, Keyboard.Modifiers);
    }

    private bool TryHandleLiveControlMouseWheel(ScreenPoint point, int delta, ModifierKeys modifiers)
    {
        if (_disposed || delta == 0 || !IsLiveControlWheelModifiers(modifiers))
        {
            return false;
        }

        if (_pinnedLenses.HasLiveControlTargetAt(point))
        {
            if (_pinnedLenses.TryAdjustZoomAt(point, delta, modifiers, out var pinnedLensZoom))
            {
                ShowPinnedLensZoomHud(point, pinnedLensZoom);
                return true;
            }

            return (modifiers & ModifierKeys.Shift) != 0;
        }

        if (IsRectSelectionMode(_mode))
        {
            return false;
        }

        if (IsAnnotationMode(_mode))
        {
            return TryHandleAnnotationLiveControlMouseWheel(point, delta, modifiers);
        }

        var direction = Math.Sign(delta);
        if (Settings.MagnifierEnabled)
        {
            if ((modifiers & ModifierKeys.Shift) != 0)
            {
                AdjustMagnifierRadius(direction * WheelFocusRadiusStep);
                ShowLiveAdjustmentHud(point, $"Radius {FormatPixels(Settings.MagnifierRadius)}");
            }
            else
            {
                AdjustMagnifierZoom(direction * WheelFocusZoomStep);
                ShowLiveAdjustmentHud(point, $"Zoom {FormatZoom(Settings.MagnifierZoom)}");
            }

            return true;
        }

        if (_visualEffects.SpotlightEnabled)
        {
            if ((modifiers & ModifierKeys.Shift) != 0)
            {
                AdjustSpotlightRadius(direction * WheelFocusRadiusStep);
                ShowLiveAdjustmentHud(point, $"Radius {FormatPixels(Settings.SpotlightRadius)}");
            }
            else
            {
                AdjustSpotlightOpacity(direction * WheelSpotlightOpacityStep);
                ShowLiveAdjustmentHud(point, $"Dim {FormatPercent(Settings.SpotlightOpacity)}");
            }

            return true;
        }

        return false;
    }

    private bool TryHandleAnnotationLiveControlMouseWheel(ScreenPoint point, int delta, ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Shift) != 0
            || _annotations.HasTextInput
            || !_annotations.HasSelection)
        {
            return false;
        }

        var direction = Math.Sign(delta);
        var tool = CurrentTool;
        if (tool == AnnotationTool.Text)
        {
            var amount = direction * WheelAnnotationFontSizeStep;
            if (!_annotations.AdjustSelectedTextFontSize(amount))
            {
                return false;
            }

            ShowLiveAdjustmentHud(point, FormatAnnotationFontSizeHud(amount));
            return true;
        }

        if (IsThicknessLiveControlTool(tool))
        {
            var amount = direction * WheelAnnotationThicknessStep;
            if (!_annotations.AdjustSelectedThickness(amount))
            {
                return false;
            }

            ShowLiveAdjustmentHud(point, FormatAnnotationThicknessHud(tool, amount));
            return true;
        }

        return false;
    }

    private string FormatAnnotationFontSizeHud(double delta)
    {
        if (!_annotations.TryGetSelectedTextFontSizeSummary(out var fontSize, out var mixedValue, out var singleTool))
        {
            return $"Text {FormatSignedPixels(delta)}";
        }

        var label = FormatAnnotationHudLabel(singleTool, AnnotationTool.Text);
        return mixedValue
            ? $"{label} {FormatSignedPixels(delta)}"
            : $"{label} {FormatPixels(fontSize)}";
    }

    private string FormatAnnotationThicknessHud(AnnotationTool fallbackTool, double delta)
    {
        if (!_annotations.TryGetSelectedThicknessSummary(out var thickness, out var mixedValue, out var singleTool))
        {
            return $"{FormatAnnotationHudLabel(null, fallbackTool)} {FormatSignedPixels(delta)}";
        }

        var label = FormatAnnotationHudLabel(singleTool, fallbackTool);
        return mixedValue
            ? $"{label} {FormatSignedPixels(delta)}"
            : $"{label} {FormatPixels(thickness)}";
    }

    private static string FormatAnnotationHudLabel(AnnotationTool? selectedTool, AnnotationTool fallbackTool)
    {
        if (selectedTool is null)
        {
            return "Selection";
        }

        var tool = selectedTool.Value;
        return tool switch
        {
            AnnotationTool.Pencil => "Pencil",
            AnnotationTool.Highlighter => "Highlighter",
            AnnotationTool.Arrow => "Arrow",
            AnnotationTool.Line => "Line",
            AnnotationTool.Rectangle => "Rectangle",
            AnnotationTool.Ellipse => "Ellipse",
            AnnotationTool.Text => "Text",
            AnnotationTool.StepOval or AnnotationTool.StepRect => "Step",
            _ => fallbackTool.ToString()
        };
    }

    private static string FormatZoom(double zoom)
    {
        return zoom.ToString("0.##", CultureInfo.InvariantCulture) + "x";
    }

    private static string FormatPixels(double value)
    {
        return value.ToString("0", CultureInfo.InvariantCulture) + " px";
    }

    private static string FormatSignedPixels(double value)
    {
        var sign = value > 0 ? "+" : string.Empty;
        return sign + value.ToString("0", CultureInfo.InvariantCulture) + " px";
    }

    private static string FormatPercent(double value)
    {
        return (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    private void UpdateLiveControlsMouseHook()
    {
        if (_disposed)
        {
            return;
        }

        if (Settings.MagnifierEnabled || _visualEffects.SpotlightEnabled)
        {
            if (!_liveControlsMouseHook.Install())
            {
                AppLog.Error("Could not install low-level mouse hook for live controls.");
            }

            return;
        }

        _liveControlsMouseHook.Uninstall();
    }

    private void TryCompletePushToAnnotateExit()
    {
        _pushToAnnotate.TryCompleteExit(CanExitPushToAnnotate());
    }

    private bool CanExitPushToAnnotate()
    {
        return _mode == InteractionMode.Annotate
            && !_annotationMouse.HasActiveOperation
            && !_annotations.HasTextInput;
    }

    private bool UpdateFadingAnnotations()
    {
        var nowMs = NowMs();
        var fadingAnnotationsAnimating = _annotations.HasFadingTemporaryAnnotations(nowMs);
        var removedExpired = _annotations.RemoveExpiredTemporaryAnnotations(nowMs);
        if (fadingAnnotationsAnimating && !removedExpired)
        {
            _overlayManager?.Invalidate();
        }

        return _annotations.HasFadingTemporaryAnnotations(nowMs);
    }

    private void OnAnnotationsChanged(object? sender, EventArgs e)
    {
        if (_annotations.HasFadingTemporaryAnnotations(NowMs()))
        {
            _timer.Interval = FadeInterval;
        }

        _overlayManager?.Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnAnnotationDraftProgressed(object? sender, EventArgs e)
    {
        // An in-progress stroke/drag gained a sample: repaint the overlay but skip
        // the full UI sync - no toolbar/tray state changes mid-draft, so firing
        // StateChanged here (100-200x/sec) would be pure waste.
        _overlayManager?.Invalidate();
    }

    private void UpdateMagnifierHost()
    {
        if (_visualEffects.TryGetSpotlightCursor(out var cursor))
        {
            _magnifier.UpdateHost(cursor);
        }
    }

    // The cursor-following magnifier intentionally keeps the screen overlay in
    // its source so region masks remain a privacy layer inside the magnified
    // image. Only floating chrome is excluded to avoid self-capture.
    private IReadOnlyList<IntPtr> GetMagnifierExcludedWindows()
    {
        var handles = new List<IntPtr>();
        if (_toolbar.TryGetVisibleHandle(out var toolbarHandle))
        {
            handles.Add(toolbarHandle);
        }

        foreach (var pinnedLensHost in _pinnedLenses.Hosts)
        {
            if (pinnedLensHost.Handle != IntPtr.Zero)
            {
                handles.Add(pinnedLensHost.Handle);
            }
        }

        return handles;
    }

    private IReadOnlyList<IntPtr> GetPinnedLensExcludedWindows()
    {
        var handles = new List<IntPtr>();
        if (_toolbar.TryGetVisibleHandle(out var toolbarHandle))
        {
            handles.Add(toolbarHandle);
        }

        foreach (var pinnedLensHost in _pinnedLenses.Hosts)
        {
            if (pinnedLensHost.Handle != IntPtr.Zero)
            {
                handles.Add(pinnedLensHost.Handle);
            }
        }

        return handles;
    }

    private void SubscribeMagnifierRendering()
    {
        _magnifier.SubscribeRendering();
    }

    private void UnsubscribeMagnifierRendering()
    {
        _magnifier.UnsubscribeRendering();
    }

    private void CloseMagnifierHost()
    {
        _magnifier.CloseHost();
    }

    private void ApplyMagnifierCursor(ScreenPoint cursor, ScreenPoint? previous, bool force)
    {
        var previousPoint = previous ?? cursor;
        _visualEffects.SetSpotlightCursor(cursor, previousPoint, force);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => OnDisplaySettingsChanged(sender, e));
            return;
        }

        // OverlayManager rebuilds the per-monitor overlays itself; here we rescue the
        // floating windows a removed/rearranged monitor would otherwise strand
        // off-screen, by clamping them back onto the nearest surviving monitor.
        _pinnedLenses.ReconcileToWorkingArea();
        _timerController?.ReconcileToWorkingArea();
    }

    public void ClosePinnedLenses()
    {
        _pinnedLenses.CloseAll();
    }

    public bool HasCaptureStages => _captureStage?.HasStages == true;

    public void StartCaptureStageForLastWindow()
    {
        var source = _modeTransitions.LastExternalForegroundWindow;
        if (source == IntPtr.Zero)
        {
            return;
        }

        _captureStage?.StartForWindow(source);
    }

    public Task StartCaptureStageWithPickerAsync()
    {
        return _captureStage?.StartWithPickerAsync() ?? Task.CompletedTask;
    }

    public void CloseCaptureStages()
    {
        _captureStage?.CloseAll();
    }

    private OverlaySnapshotData CaptureOverlaySnapshot(ScreenRect rect)
    {
        var surface = _overlayManager?.CaptureOverlayLayer(rect);
        var sprites = _timerController?.CaptureSprites() ?? [];
        return new OverlaySnapshotData(surface, sprites);
    }

    private void ResetSpotlightRegionEditState()
    {
        _regionSpotlights.ResetEditState();
    }

    private RectOverlayVisual? GetRectOverlayVisual()
    {
        return _rectSelection.GetOverlayVisual();
    }

    private void ExitVisualEffects()
    {
        if (!Settings.MagnifierEnabled && !_visualEffects.SpotlightEnabled)
        {
            return;
        }

        var updated = Settings.Clone();
        updated.MagnifierEnabled = false;
        updated.SpotlightEnabled = false;
        ApplySettings(updated);
    }

    private void RegisterHotKeys()
    {
        _hotKeys.Register(
            Settings.Shortcuts,
            VisualEffectsController.ShouldRegisterExitVisualHotKey(_mode, Settings.MagnifierEnabled, _visualEffects.SpotlightEnabled),
            ExitVisualShortcut);
    }

    private void ReassertFloatingChromeTopmost()
    {
        if (IsVisualBoardMode(_mode))
        {
            _pinnedLenses.ReassertTopmost();
        }

        _toolbar.ReassertTopmost();
        _timerController?.ReassertTopmost();

        _pinnedLenses.ReassertContextMenuTopmost();
        _regionMaskContextMenu.ReassertTopmostIfVisible();
        WpfTopmostToolTipHelper.ReassertOpen();
    }

    private void ReassertPinnedLensTopmost()
    {
        if (!IsVisualBoardMode(_mode))
        {
            _pinnedLenses.ReassertTopmost();
        }
    }

    private ScreenPoint? GetSpotlightPoint()
    {
        return _visualEffects.GetSpotlightPoint(_mode, Settings.MagnifierEnabled);
    }

    private void CacheParsedSettings()
    {
        _pointerVisuals.CacheParsedSettings();
        _pushToAnnotate.ConfigureShortcut();
        _visualEffects.ConfigureSpotlightHoldShortcut(Settings.Shortcuts.HoldSpotlight);
    }

    private static bool IsAnnotationMode(InteractionMode mode)
    {
        return mode is InteractionMode.Annotate or InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
    }

    private static bool IsRectSelectionMode(InteractionMode mode)
    {
        return mode is InteractionMode.PinnedLensSelect
            or InteractionMode.RegionMaskSelect
            or InteractionMode.ScreenshotRegionSelect
            or InteractionMode.RegionSpotlightSelect;
    }

    private static bool IsVisualBoardMode(InteractionMode mode)
    {
        return mode is InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
    }

    private static bool IsLiveControlWheelModifiers(ModifierKeys modifiers)
    {
        return (modifiers & ModifierKeys.Control) != 0
            && (modifiers & ~(ModifierKeys.Control | ModifierKeys.Shift)) == 0;
    }

    private static bool IsThicknessLiveControlTool(AnnotationTool tool)
    {
        return tool is AnnotationTool.Arrow
            or AnnotationTool.Rectangle
            or AnnotationTool.Ellipse
            or AnnotationTool.Line
            or AnnotationTool.Pencil
            or AnnotationTool.Highlighter
            or AnnotationTool.StepOval
            or AnnotationTool.StepRect;
    }

    private double NowMs() => _clock.Elapsed.TotalMilliseconds;

    private static bool TryGetCursor(out ScreenPoint point)
    {
        if (NativeMethods.GetCursorPos(out var nativePoint))
        {
            point = new ScreenPoint(nativePoint.X, nativePoint.Y);
            return true;
        }

        point = default;
        _ = Marshal.GetLastWin32Error();
        return false;
    }

    private static ScreenPoint? GetCursorOrNull()
    {
        return TryGetCursor(out var point) ? point : null;
    }

}
