using System.Diagnostics;
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
    private readonly OverlayToolbarController _toolbar;
    private readonly CaptureController _capture;
    private readonly PinnedLensController _pinnedLenses;
    private readonly MagnifierController _magnifier;
    private readonly GlobalHotKeyController _hotKeys;
    private readonly RegionMaskController _regionMasks = new();
    private readonly RegionSpotlightController _regionSpotlights = new();
    private readonly RectSelectionController _rectSelection;
    private readonly RectToolsInputController _rectTools;
    private readonly InteractionModeTransitionController _modeTransitions;
    private TimerController? _timerController;
    private TrayIconController? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private ScreenBoardFrame? _screenBoardFrame;
    private readonly RegionMaskContextMenuController _regionMaskContextMenu;
    private bool _disposed;
    private InteractionMode _mode = InteractionMode.Passthrough;
    private AnnotationTool _lastStepTool = AnnotationTool.StepOval;

    public event EventHandler? StateChanged;

    public AppSettings Settings { get; private set; } = new();
    public string SettingsFilePath => _settingsPersistence.SettingsFilePath;
    public InteractionMode Mode => _mode;
    public LaserActivationMode ActivationMode => Settings.GetLaserActivationMode();
    public AnnotationTool CurrentTool => Settings.GetAnnotationTool();
    public AnnotationDocument Annotations => _annotations;
    public bool LaserVisuallyActive => _pointerVisuals.LaserVisuallyActive;
    public bool CursorHighlightEnabled => Settings.CursorHighlightEnabled;
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
        _pushToAnnotate = new PushToAnnotateController(
            () => Settings,
            () => _mode,
            SetInteractionMode,
            interval => _timer.Interval = interval,
            () => _annotations.HasTextInput,
            ActiveInterval,
            ClearAnnotations,
            SetAnnotationTool,
            SelectStepTool,
            SetAnnotationPresetColor);
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
            SetAnnotationTool,
            SelectStepTool,
            SetAnnotationPresetColor);
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
        _visualEffects = new VisualEffectsController(
            GetCursorOrNull,
            () => _overlayManager?.Invalidate(),
            (current, previous) => _overlayManager?.InvalidateForCursor(current, previous),
            MovementThresholdPixels);
        _pinnedLenses = new PinnedLensController(
            () => Settings,
            GetPinnedLensExcludedWindows,
            () => _disposed,
            CaptureController.WaitForScreenRefreshAsync,
            () => _overlayManager?.ReassertTopmost(),
            (title, text) => _trayIcon?.ShowMessage(title, text),
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
            ApplySettings,
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
            () => _overlayManager?.Hide(),
            () => _overlayManager?.Show(),
            _pinnedLenses.HideForBoard,
            _pinnedLenses.RestoreAfterBoard,
            () => IsVisualBoardMode(_mode),
            frame => _overlayManager?.CaptureScreenBoardFrame(frame),
            (title, text) => _trayIcon?.ShowMessage(title, text));
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
            SaveScreenBoardSnapshot,
            () => _screenBoardFrame = null,
            () => _overlayManager?.Show(),
            mode => _overlayManager?.SetInteractionMode(mode),
            () => _overlayManager?.Invalidate(),
            () => Settings.MagnifierEnabled,
            CloseMagnifierHost,
            () => _magnifier.RefreshFromCurrentCursor(forceCursorInvalidation: true, MovementThresholdPixels),
            _pinnedLenses.HideForBoard,
            _pinnedLenses.RestoreAfterBoard,
            _pointerVisuals.SetLaserVisualActive,
            RegisterHotKeys,
            () => StateChanged?.Invoke(this, EventArgs.Empty));
        _hotKeys = new GlobalHotKeyController(
            (title, text) => _trayIcon?.ShowMessage(title, text),
            ToggleLaserActivationMode,
            ToggleAnnotateMode,
            StartPushToAnnotate,
            ToggleCursorHighlight,
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
        CacheParsedSettings();
        if (IsStepTool(CurrentTool))
        {
            _lastStepTool = CurrentTool;
        }

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
        _overlayManager = new OverlayManager(_pointerVisuals.Trail, _annotations, () => Settings, () => _mode, NowMs, GetSpotlightPoint, _pointerVisuals.GetCursorHighlightFrame, () => _screenBoardFrame, GetRectOverlayVisual, () => _regionMasks.Masks, () => _regionMasks.SelectedMaskId, () => _regionSpotlights.Regions, () => _regionSpotlights.SelectedIndex, this, ReassertPinnedLensTopmost, ReassertFloatingChromeTopmost);
        _trayIcon = new TrayIconController(this);
        _timerController = new TimerController(NowMs, () => Settings.Timer, ApplyTimerDefaults, AddTimerLabelToHistory, OnTimerActiveCountChanged);
        RegisterHotKeys();
        _pointerVisuals.StartMouseHook();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _overlayManager.Show();
        _timer.Start();
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (_settingsPersistence.WasCreatedOnLoad)
        {
            _trayIcon.ShowMessage(
                "FocusTool is running",
                $"{Settings.Shortcuts.ToggleAnnotate} = annotate, {Settings.Shortcuts.ToggleLaserActivation} = laser mode, {Settings.Shortcuts.ToggleSpotlight} = spotlight, {Settings.Shortcuts.ToggleMagnifier} = magnifier.");
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
        SetCursorHighlightEnabled(!Settings.CursorHighlightEnabled);
    }

    public void SetCursorHighlightEnabled(bool enabled)
    {
        if (Settings.CursorHighlightEnabled == enabled)
        {
            return;
        }

        var updated = Settings.Clone();
        updated.CursorHighlightEnabled = enabled;
        ApplySettings(updated);
    }

    public void SetCursorHighlightActivationMode(LaserActivationMode mode)
    {
        if (Settings.GetCursorHighlightActivationMode() == mode)
        {
            return;
        }

        var updated = Settings.Clone();
        updated.SetCursorHighlightActivationMode(mode);
        ApplySettings(updated);
    }

    public void SetCursorHighlightPresetColor(int index)
    {
        if (index < 0 || index >= Settings.LaserColorPresets.Count)
        {
            return;
        }

        var updated = Settings.Clone();
        updated.CursorHighlightColor = Settings.LaserColorPresets[index];
        ApplySettings(updated);
    }

    public void SetClickPulseEnabled(bool enabled)
    {
        if (Settings.ClickPulseEnabled == enabled)
        {
            return;
        }

        var updated = Settings.Clone();
        updated.ClickPulseEnabled = enabled;
        ApplySettings(updated);
    }

    public void AdjustCursorHighlightRadius(double delta)
    {
        var updated = Settings.Clone();
        updated.CursorHighlightRadius += delta;
        ApplySettings(updated);
    }

    public void AdjustCursorHighlightThickness(double delta)
    {
        var updated = Settings.Clone();
        updated.CursorHighlightThickness += delta;
        ApplySettings(updated);
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
        if (_mode == InteractionMode.ScreenBoard)
        {
            SetInteractionMode(InteractionMode.Passthrough);
            return;
        }

        _ = EnterScreenBoardAsync();
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
        SetInteractionMode(_mode == InteractionMode.BlackScreen ? InteractionMode.Passthrough : InteractionMode.BlackScreen);
    }

    public void ToggleWhiteScreen()
    {
        SetInteractionMode(_mode == InteractionMode.WhiteScreen ? InteractionMode.Passthrough : InteractionMode.WhiteScreen);
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
            var updated = Settings.Clone();
            updated.SpotlightEnabled = false;
            ApplySettings(updated);
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

    private async Task EnterScreenBoardAsync()
    {
        var previousMode = _mode;
        await _capture.EnterScreenBoardAsync(
            frame =>
            {
                _screenBoardFrame = frame;
                SetInteractionMode(InteractionMode.ScreenBoard);
            },
            () => SetInteractionMode(previousMode));
    }

    private void SaveScreenBoardSnapshot()
    {
        _ = _capture.SaveScreenBoardSnapshotAsync(_screenBoardFrame);
    }

    public void SetMagnifierEnabled(bool enabled)
    {
        if (Settings.MagnifierEnabled == enabled)
        {
            return;
        }

        var updated = Settings.Clone();
        updated.MagnifierEnabled = enabled;
        ApplySettings(updated);
    }

    public void SetInteractionMode(InteractionMode mode)
    {
        _modeTransitions.SetMode(mode);
    }

    public void ApplySettings(AppSettings settings)
    {
        var previousActivationMode = ActivationMode;
        var cursorHighlightWasEnabled = Settings.CursorHighlightEnabled;
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

        if (Settings.CursorHighlightEnabled)
        {
            _timer.Interval = ActiveInterval;
            _pointerVisuals.UpdateCursorHighlight(force: true);
        }
        else if (cursorHighlightWasEnabled || _pointerVisuals.HasCursorHighlightPoint)
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

        _overlayManager?.Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPresetColor(string color)
    {
        var updated = Settings.Clone();
        updated.Color = color;
        ApplySettings(updated);
    }

    public void SetLaserPresetColor(int index)
    {
        if (index < 0 || index >= Settings.LaserColorPresets.Count)
        {
            return;
        }

        SetPresetColor(Settings.LaserColorPresets[index]);
    }

    public void SetAnnotationColor(string color)
    {
        var updated = Settings.Clone();
        updated.AnnotationColor = color;
        ApplySettings(updated);
        _annotations.ApplyColorToSelection(color);
    }

    public void SetAnnotationPresetColor(int index)
    {
        if (index < 0 || index >= Settings.AnnotationColorPresets.Count)
        {
            return;
        }

        SetAnnotationColor(Settings.AnnotationColorPresets[index]);
    }

    public void SetAnnotationTool(AnnotationTool tool)
    {
        if (_annotations.IsEditingText)
        {
            _annotations.CommitTextInput();
        }

        if (IsStepTool(tool))
        {
            _lastStepTool = tool;
        }

        var updated = Settings.Clone();
        updated.SetAnnotationTool(tool);
        ApplySettings(updated);

        if (tool != AnnotationTool.Move)
        {
            _annotations.ClearSelection();
        }
    }

    public void SelectStepTool()
    {
        SetAnnotationTool(_lastStepTool);
        if (_mode == InteractionMode.Passthrough)
        {
            SetInteractionMode(InteractionMode.Annotate);
        }
    }

    public void AdjustAnnotationThickness(double delta)
    {
        var updated = Settings.Clone();
        updated.AnnotationThickness += delta;
        ApplySettings(updated);
        _annotations.AdjustSelectedThickness(delta);
    }

    public void AdjustAnnotationFontSize(double delta)
    {
        var updated = Settings.Clone();
        updated.AnnotationFontSize += delta;
        ApplySettings(updated);
        _annotations.AdjustSelectedTextFontSize(delta);
    }

    public void AdjustLaserTrailLength(int delta)
    {
        var updated = Settings.Clone();
        updated.TrailLengthMs += delta;
        ApplySettings(updated);
    }

    public void AdjustSpotlightRadius(double delta)
    {
        var updated = Settings.Clone();
        updated.SpotlightRadius += delta;
        ApplySettings(updated);
    }

    public void AdjustSpotlightOpacity(double delta)
    {
        var updated = Settings.Clone();
        updated.SpotlightOpacity += delta;
        ApplySettings(updated);
    }

    public void AdjustMagnifierZoom(double delta)
    {
        var updated = Settings.Clone();
        updated.MagnifierZoom += delta;
        ApplySettings(updated);
    }

    public void AdjustMagnifierRadius(double delta)
    {
        var updated = Settings.Clone();
        updated.MagnifierRadius += delta;
        ApplySettings(updated);
    }

    public void AdjustPinnedLensZoom(double delta)
    {
        var updated = Settings.Clone();
        updated.PinnedLensZoom += delta;
        ApplySettings(updated);
    }

    public void AdjustPinnedLensRefreshFps(int delta)
    {
        var updated = Settings.Clone();
        updated.PinnedLensRefreshFps += delta;
        ApplySettings(updated);
    }

    public void AdjustRegionMaskOpacity(double delta)
    {
        var hasSelectedMask = _regionMasks.TryGetSelected(out var mask);
        var updated = Settings.Clone();
        updated.RegionMaskOpacity = (hasSelectedMask ? mask.Opacity : Settings.RegionMaskOpacity) + delta;
        ApplySettings(updated);
        if (hasSelectedMask)
        {
            mask.SetOpacity(Settings.RegionMaskOpacity);
            _overlayManager?.Invalidate();
        }
    }

    public void SetRegionMaskColor(string color)
    {
        var updated = Settings.Clone();
        updated.RegionMaskColor = color;
        ApplySettings(updated);
        if (_regionMasks.TryGetSelected(out var mask))
        {
            mask.SetColor(Settings.RegionMaskColor);
            _overlayManager?.Invalidate();
        }
    }

    public void SetRegionMaskPresetColor(int index)
    {
        if (index < 0 || index >= Settings.RegionMaskColorPresets.Count)
        {
            return;
        }

        SetRegionMaskColor(Settings.RegionMaskColorPresets[index]);
    }

    public void SetGlowEnabled(bool enabled)
    {
        var updated = Settings.Clone();
        updated.GlowEnabled = enabled;
        ApplySettings(updated);
    }

    public void ToggleFadingAnnotations()
    {
        SetFadingAnnotationsEnabled(!Settings.FadingAnnotationsEnabled);
    }

    public void SetFadingAnnotationsEnabled(bool enabled)
    {
        if (Settings.FadingAnnotationsEnabled == enabled)
        {
            return;
        }

        var updated = Settings.Clone();
        updated.FadingAnnotationsEnabled = enabled;
        ApplySettings(updated);
    }

    public void AdjustFadingAnnotationVisibleMs(int deltaMs)
    {
        var updated = Settings.Clone();
        updated.FadingAnnotationVisibleMs += deltaMs;
        ApplySettings(updated);
    }

    public void AdjustFadingAnnotationFadeMs(int deltaMs)
    {
        var updated = Settings.Clone();
        updated.FadingAnnotationFadeMs += deltaMs;
        ApplySettings(updated);
    }

    public void SetLaserActivationMode(LaserActivationMode mode)
    {
        if (ActivationMode == mode)
        {
            return;
        }

        var updated = Settings.Clone();
        updated.SetLaserActivationMode(mode);
        ApplySettings(updated);
    }

    public void SetSpotlightEnabled(bool enabled)
    {
        if (_visualEffects.SpotlightEnabled == enabled)
        {
            return;
        }

        if (enabled && _mode == InteractionMode.RegionSpotlightSelect)
        {
            SetInteractionMode(InteractionMode.Passthrough);
        }

        var updated = Settings.Clone();
        updated.SpotlightEnabled = enabled;
        ApplySettings(updated);
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
        settings.CursorHighlightEnabled = Settings.CursorHighlightEnabled;
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
        _hotKeys.Dispose();
        CloseMagnifierHost();
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
        if (_disposed)
        {
            return;
        }

        _modeTransitions.TrackExternalForegroundWindow();
        _pushToAnnotate.Update(CanExitPushToAnnotate());

        var fadingAnnotationsAnimating = UpdateFadingAnnotations();
        var cursorHighlightAnimating = _pointerVisuals.UpdateCursorHighlight(force: false);
        var magnifierActive = Settings.MagnifierEnabled;
        var spotlightActive = _visualEffects.IsSpotlightVisibleInMode(_mode);
        var holdActive = _pointerVisuals.IsLaserHoldActive(ActivationMode);

        _pointerVisuals.SetLaserVisualActive(holdActive || IsAnnotationMode(_mode));
        if (magnifierActive)
        {
            if (!_magnifier.IsRenderingSubscribed)
            {
                _magnifier.RefreshFromCurrentCursor(forceCursorInvalidation: false, MovementThresholdPixels);
                _overlayManager?.Invalidate();
            }
        }
        else if (spotlightActive)
        {
            _visualEffects.UpdateSpotlightCursor(force: false);
        }

        if (holdActive)
        {
            _pointerVisuals.TrackLaserWhileHeld(ActivationMode);
            return;
        }

        _pointerVisuals.FadeLaserAfterRelease();
        if (_pushToAnnotate.Active)
        {
            _timer.Interval = ActiveInterval;
        }
        else if (magnifierActive && !_magnifier.IsRenderingSubscribed)
        {
            _timer.Interval = ActiveInterval;
        }
        else if (spotlightActive)
        {
            _timer.Interval = ActiveInterval;
        }
        else if (fadingAnnotationsAnimating)
        {
            _timer.Interval = FadeInterval;
        }
        else if (cursorHighlightAnimating)
        {
            _timer.Interval = FadeInterval;
        }
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
        _toolbar.ReassertTopmost();
        _timerController?.ReassertTopmost();

        _pinnedLenses.ReassertContextMenuTopmost();
        _regionMaskContextMenu.ReassertTopmostIfVisible();
    }

    private void ReassertPinnedLensTopmost()
    {
        _pinnedLenses.ReassertTopmost();
    }

    private ScreenPoint? GetSpotlightPoint()
    {
        return _visualEffects.GetSpotlightPoint(_mode, Settings.MagnifierEnabled);
    }

    private void CacheParsedSettings()
    {
        _pointerVisuals.CacheParsedSettings();
        _pushToAnnotate.ConfigureShortcut();
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

    private static bool IsStepTool(AnnotationTool tool)
    {
        return tool is AnnotationTool.StepOval or AnnotationTool.StepRect;
    }

    private static bool IsVisualBoardMode(InteractionMode mode)
    {
        return mode is InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
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
