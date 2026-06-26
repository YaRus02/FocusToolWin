using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
using FocusTool.Win.Overlay;
using FocusTool.Win.Tray;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;
using Shortcut = FocusTool.Win.Native.Shortcut;

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
    private readonly HashSet<string> _pushToAnnotatePolledShortcutDown = new(StringComparer.Ordinal);

    private OverlayManager? _overlayManager;
    private readonly PointerVisualController _pointerVisuals;
    private readonly OverlayToolbarController _toolbar;
    private readonly CaptureController _capture;
    private readonly PinnedLensController _pinnedLenses;
    private readonly MagnifierController _magnifier;
    private readonly GlobalHotKeyController _hotKeys;
    private readonly RegionMaskController _regionMasks = new();
    private readonly RegionSpotlightController _regionSpotlights = new();
    private TimerController? _timerController;
    private TrayIconController? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private ScreenBoardFrame? _screenBoardFrame;
    private Shortcut _pushToAnnotateShortcut;
    private ScreenPoint _lastSelectionMovePoint;
    private ScreenPoint _lastTextClickPoint;
    private ScreenPoint _lastObjectClickPoint;
    private ScreenPoint _pendingTextEditMovePoint;
    private readonly RectSelectionSession _rectSelection = new();
    private bool _hasLastTextClick;
    private bool _hasLastObjectClick;
    private double _lastTextClickMs = double.NegativeInfinity;
    private double _lastObjectClickMs = double.NegativeInfinity;
    private bool _drawing;
    private bool _movingSelection;
    private bool _draggingAnnotationEditHandle;
    private bool _pendingTextEditMove;
    private bool _pushToAnnotateActive;
    private bool _pushToAnnotateExitPending;
    private bool _restoreToolbarAfterRectSelection;
    private ScreenRect? _pendingScreenshotRegion;
    private bool _screenshotRegionToolbarRestorePending;
    private readonly RectEditSession _screenshotRegionEdit = new();
    private readonly RegionMaskContextMenuController _regionMaskContextMenu;
    private bool _disposed;
    private bool _spotlightEnabled;
    private bool _hasSpotlightCursor;
    private ScreenPoint _spotlightCursor;
    private IntPtr _previousForegroundWindow = IntPtr.Zero;
    private IntPtr _lastExternalForegroundWindow = IntPtr.Zero;
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
    public bool SpotlightEnabled => _spotlightEnabled;
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
            () => StateChanged?.Invoke(this, EventArgs.Empty));
        _toolbar = new OverlayToolbarController(
            this,
            () => _disposed,
            () => _overlayManager?.ReassertTopmost(),
            () => StateChanged?.Invoke(this, EventArgs.Empty));
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
        _spotlightEnabled = false;

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
        if (_disposed
            || _pushToAnnotateActive
            || IsAnnotationMode(_mode)
            || _mode != InteractionMode.Passthrough
            || ShortcutSettings.IsShortcutDisabled(Settings.Shortcuts.PushToAnnotate))
        {
            return;
        }

        _pushToAnnotateActive = true;
        _pushToAnnotateExitPending = false;
        _pushToAnnotatePolledShortcutDown.Clear();
        _timer.Interval = ActiveInterval;
        SetInteractionMode(InteractionMode.Annotate);
    }

    public void ToggleLaserActivationMode()
    {
        SetLaserActivationMode(ActivationMode == LaserActivationMode.Always
            ? LaserActivationMode.Hold
            : LaserActivationMode.Always);
    }

    public void ToggleSpotlight()
    {
        SetSpotlightEnabled(!_spotlightEnabled);
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
        _rectSelection.Cancel();
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
        _rectSelection.Cancel();
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

    private bool TryHitRegionMask(ScreenPoint point, out RegionMask mask)
    {
        return _regionMasks.TryHit(point, out mask);
    }

    private bool TryGetSelectedRegionMask(out RegionMask mask)
    {
        return _regionMasks.TryGetSelected(out mask);
    }

    private bool TryHitRegionMaskResizeHandle(ScreenPoint point, out RegionMask mask, out RectResizeHandle handle)
    {
        return _regionMasks.TryHitResizeHandle(point, out mask, out handle);
    }

    private bool TryHitSpotlightRegionResizeHandle(ScreenPoint point, out int index, out RectResizeHandle handle)
    {
        return _regionSpotlights.TryHitResizeHandle(point, out index, out handle);
    }

    private bool TryHitSpotlightRegion(ScreenPoint point, out int index)
    {
        return _regionSpotlights.TryHit(point, out index);
    }

    private void DeleteRegionMask(int maskId, bool exitMaskMode)
    {
        if (!_regionMasks.Delete(maskId))
        {
            return;
        }

        _rectSelection.Cancel();
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

    private void DeleteSelectedRegionMask()
    {
        if (_regionMasks.SelectedMaskId < 0)
        {
            return;
        }

        DeleteRegionMask(_regionMasks.SelectedMaskId, exitMaskMode: false);
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
        _pendingScreenshotRegion = null;
        _screenshotRegionToolbarRestorePending = false;
        _screenshotRegionEdit.Cancel();
        BeginRectSelectionMode(InteractionMode.ScreenshotRegionSelect);
    }

    private void BeginRegionSpotlightSelection()
    {
        if (_spotlightEnabled)
        {
            var updated = Settings.Clone();
            updated.SpotlightEnabled = false;
            ApplySettings(updated);
        }

        _regionSpotlights.SelectLast();
        BeginRectSelectionMode(InteractionMode.RegionSpotlightSelect);
        if (_regionSpotlights.HasRegions)
        {
            RestoreToolbarAfterRectSelection();
        }
    }

    private void BeginRectSelectionMode(InteractionMode mode)
    {
        if (!IsRectSelectionMode(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Mode is not a rectangle selection mode.");
        }

        if (IsRectSelectionMode(_mode) && _mode != mode)
        {
            SetInteractionMode(InteractionMode.Passthrough);
        }

        _rectSelection.Cancel();
        _regionMasks.CancelEdit();
        _screenshotRegionEdit.Cancel();
        _regionSpotlights.CancelEdit();
        _restoreToolbarAfterRectSelection = ToolbarVisible;
        if (_restoreToolbarAfterRectSelection)
        {
            _toolbar.HideTransient();
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
        if (_mode == mode)
        {
            return;
        }

        if (_pushToAnnotateActive && mode != InteractionMode.Annotate)
        {
            _pushToAnnotateActive = false;
            _pushToAnnotateExitPending = false;
        }

        var leavingAnnotationInput = IsAnnotationMode(_mode) && !IsAnnotationMode(mode);
        var enteringAnnotationInput = !IsAnnotationMode(_mode) && IsAnnotationMode(mode);
        var leavingRectSelection = IsRectSelectionMode(_mode) && _mode != mode;
        var leavingRegionMaskSelection = _mode == InteractionMode.RegionMaskSelect && mode != InteractionMode.RegionMaskSelect;
        var leavingScreenBoard = _mode == InteractionMode.ScreenBoard && mode != InteractionMode.ScreenBoard;
        var leavingVisualBoard = IsVisualBoardMode(_mode);
        var enteringVisualBoard = IsVisualBoardMode(mode);
        var exitVisualHotKeyWasNeeded = ShouldRegisterExitVisualHotKey(
            _mode,
            Settings.MagnifierEnabled,
            HasExitVisualSpotlightEffect());

        if (enteringAnnotationInput)
        {
            // At this instant the foreground may already be our own toolbar/overlay
            // (e.g. annotate was toggled by a toolbar-button click), so fall back to
            // the last tracked external window to restore focus correctly on exit.
            var foreground = NativeMethods.GetForegroundWindow();
            _ = NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
            _previousForegroundWindow = processId != 0 && processId != (uint)Environment.ProcessId
                ? foreground
                : _lastExternalForegroundWindow;
        }

        if (leavingAnnotationInput)
        {
            if (_annotations.HasTextInput)
            {
                _annotations.CommitTextInput();
            }
            else
            {
                _annotations.CancelDraft();
            }

            _drawing = false;
            _movingSelection = false;
            _annotations.EndSelectionMove();
        }

        if (leavingRectSelection)
        {
            _rectSelection.Cancel();
            if (_mode == InteractionMode.ScreenshotRegionSelect)
            {
                ResetScreenshotRegionEditState(restoreToolbar: true);
            }

            if (_mode == InteractionMode.RegionSpotlightSelect)
            {
                ResetSpotlightRegionEditState();
            }

            RestoreToolbarAfterRectSelection();
        }

        if (leavingRegionMaskSelection)
        {
            _regionMasks.CancelEdit();
            _regionMasks.ClearSelection();
        }

        if (leavingScreenBoard)
        {
            SaveScreenBoardSnapshot();
        }

        _mode = mode;
        if (_mode != InteractionMode.ScreenBoard)
        {
            _screenBoardFrame = null;
        }

        _overlayManager?.Show();
        _overlayManager?.SetInteractionMode(_mode);
        if (Settings.MagnifierEnabled)
        {
            if (enteringVisualBoard)
            {
                CloseMagnifierHost();
            }
            else if (leavingVisualBoard)
            {
                _magnifier.RefreshFromCurrentCursor(forceCursorInvalidation: true, MovementThresholdPixels);
            }
        }

        // Boards are a clean canvas: hide pinned lenses while a board is shown and
        // restore them on the way out. Timers intentionally stay visible.
        if (enteringVisualBoard)
        {
            _pinnedLenses.HideForBoard();
        }
        else if (leavingVisualBoard)
        {
            _pinnedLenses.RestoreAfterBoard();
        }

        _overlayManager?.Invalidate();

        if (leavingAnnotationInput && _previousForegroundWindow != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(_previousForegroundWindow);
            _previousForegroundWindow = IntPtr.Zero;
        }

        _pointerVisuals.SetLaserVisualActive(ActivationMode == LaserActivationMode.Always || IsAnnotationMode(_mode));
        var exitVisualHotKeyIsNeeded = ShouldRegisterExitVisualHotKey(
            _mode,
            Settings.MagnifierEnabled,
            HasExitVisualSpotlightEffect());
        if (exitVisualHotKeyWasNeeded != exitVisualHotKeyIsNeeded)
        {
            RegisterHotKeys();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplySettings(AppSettings settings)
    {
        var previousActivationMode = ActivationMode;
        var cursorHighlightWasEnabled = Settings.CursorHighlightEnabled;
        var clickPulseWasEnabled = Settings.ClickPulseEnabled;
        var magnifierWasEnabled = Settings.MagnifierEnabled;
        var globalHotKeysChanged = !GlobalHotKeyController.HaveSameGlobalHotKeys(Settings.Shortcuts, settings.Shortcuts);
        var exitVisualHotKeyWasNeeded = ShouldRegisterExitVisualHotKey(
            _mode,
            Settings.MagnifierEnabled,
            HasExitVisualSpotlightEffect());
        var magnifierVisualChanged = Math.Abs(Settings.MagnifierRadius - settings.MagnifierRadius) > 0.001
            || Math.Abs(Settings.MagnifierZoom - settings.MagnifierZoom) > 0.001;
        Settings.CopyFrom(settings);
        CacheParsedSettings();
        _pinnedLenses.UpdateRefreshInterval();
        _spotlightEnabled = Settings.SpotlightEnabled;
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
            UpdateSpotlightCursor(force: true);
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
            if (!_spotlightEnabled)
            {
                _hasSpotlightCursor = false;
            }
        }

        if (IsSpotlightVisibleInMode(_mode) || Settings.MagnifierEnabled)
        {
            UpdateSpotlightCursor(force: true);
            _timer.Interval = ActiveInterval;
        }
        else if (_hasSpotlightCursor)
        {
            _hasSpotlightCursor = false;
        }

        _settingsPersistence.SaveDebounced();
        var exitVisualHotKeyIsNeeded = ShouldRegisterExitVisualHotKey(
            _mode,
            Settings.MagnifierEnabled,
            HasExitVisualSpotlightEffect());
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
        var hasSelectedMask = TryGetSelectedRegionMask(out var mask);
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
        if (TryGetSelectedRegionMask(out var mask))
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
        if (_spotlightEnabled == enabled)
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
        settings.SpotlightEnabled = _spotlightEnabled;
        settings.MagnifierEnabled = Settings.MagnifierEnabled;
        ApplySettings(settings);
    }

    public void Exit()
    {
        System.Windows.Application.Current.Shutdown();
    }

    public void HandleOverlayMouseDown(ScreenPoint point, MouseButton button, ModifierKeys modifiers)
    {
        if (_mode == InteractionMode.PinnedLensSelect)
        {
            if (button != MouseButton.Left)
            {
                return;
            }

            _rectSelection.Begin(point);
            _overlayManager?.Invalidate();
            return;
        }

        if (_mode == InteractionMode.ScreenshotRegionSelect)
        {
            if (button != MouseButton.Left)
            {
                return;
            }

            if (_pendingScreenshotRegion is { } pending)
            {
                if (RectGeometry.TryHitResizeHandle(pending, point, out var handle))
                {
                    _screenshotRegionEdit.BeginResize(pending, handle);
                    _rectSelection.Cancel();
                    return;
                }

                if (pending.Contains(point))
                {
                    _screenshotRegionEdit.BeginMove(point);
                    _rectSelection.Cancel();
                    return;
                }

                _pendingScreenshotRegion = null;
                _screenshotRegionEdit.Cancel();
            }

            _rectSelection.Begin(point);
            _overlayManager?.Invalidate();
            return;
        }

        if (_mode == InteractionMode.RegionSpotlightSelect)
        {
            if (button != MouseButton.Left)
            {
                return;
            }

            if (TryHitSpotlightRegionResizeHandle(point, out var resizeIndex, out var resizeHandle))
            {
                _regionSpotlights.BeginResize(resizeIndex, resizeHandle);
                _rectSelection.Cancel();
                _overlayManager?.Invalidate();
                return;
            }

            if (TryHitSpotlightRegion(point, out var moveIndex))
            {
                _regionSpotlights.BeginMove(moveIndex, point);
                _rectSelection.Cancel();
                _overlayManager?.Invalidate();
                return;
            }

            _regionSpotlights.ClearSelection();
            _rectSelection.Begin(point);
            _overlayManager?.Invalidate();
            return;
        }

        if (_mode == InteractionMode.RegionMaskSelect)
        {
            if (button == MouseButton.Right)
            {
                if (TryHitRegionMask(point, out var mask))
                {
                    _regionMasks.Select(mask.Id);
                    _overlayManager?.Invalidate();
                    ShowRegionMaskContextMenu(point, mask.Id);
                }

                return;
            }

            if (button != MouseButton.Left)
            {
                return;
            }

            if (TryHitRegionMaskResizeHandle(point, out var resizeMask, out var resizeHandle))
            {
                _regionMasks.BeginResize(resizeMask, resizeHandle);
                _rectSelection.Cancel();
                _overlayManager?.Invalidate();
                return;
            }

            if (TryHitRegionMask(point, out var existingMask))
            {
                _regionMasks.BeginMove(existingMask, point);
                _rectSelection.Cancel();
                _overlayManager?.Invalidate();
                return;
            }

            _regionMasks.ClearSelection();
            _regionMasks.CancelEdit();
            _rectSelection.Begin(point);
            _overlayManager?.Invalidate();
            return;
        }

        if (!IsAnnotationMode(_mode) || button != MouseButton.Left)
        {
            return;
        }

        if (HandleTextObjectClick(point))
        {
            return;
        }

        if (HandleObjectEditClick(point))
        {
            return;
        }

        if (IsStepTool(CurrentTool) && _annotations.HitTestStep(point))
        {
            return;
        }

        if (CurrentTool == AnnotationTool.StepOval)
        {
            _annotations.AddPointShape(AnnotationTool.StepOval, point, Settings);
            return;
        }

        if (CurrentTool == AnnotationTool.Text)
        {
            if (_annotations.HasDraftText)
            {
                _annotations.CommitTextDraft();
                TryCompletePushToAnnotateExit();
                return;
            }

            _annotations.BeginText(point, Settings);
            return;
        }

        if (CurrentTool == AnnotationTool.Move)
        {
            if (_annotations.BeginSelectionMove(point))
            {
                _movingSelection = true;
                _lastSelectionMovePoint = point;
                return;
            }

            _drawing = true;
            _annotations.BeginSelection(point);
            return;
        }

        _drawing = true;
        _annotations.BeginStroke(CurrentTool, point, Settings);
    }

    public void HandleOverlayMouseMove(ScreenPoint point, ModifierKeys modifiers)
    {
        if (_mode == InteractionMode.PinnedLensSelect)
        {
            if (_rectSelection.Update(point))
            {
                _overlayManager?.Invalidate();
            }

            return;
        }

        if (_mode == InteractionMode.ScreenshotRegionSelect)
        {
            if (_pendingScreenshotRegion is { } pending && _screenshotRegionEdit.IsResizing)
            {
                _pendingScreenshotRegion = _screenshotRegionEdit.Resize(point);
                _overlayManager?.Invalidate();
                return;
            }

            if (_pendingScreenshotRegion is { } movingPending && _screenshotRegionEdit.IsMoving)
            {
                _pendingScreenshotRegion = _screenshotRegionEdit.Move(movingPending, point);
                _overlayManager?.Invalidate();
                return;
            }

            if (_rectSelection.Update(point))
            {
                _overlayManager?.Invalidate();
            }

            return;
        }

        if (_mode == InteractionMode.RegionSpotlightSelect)
        {
            if (_regionSpotlights.UpdateEdit(point))
            {
                _overlayManager?.Invalidate();
                return;
            }

            if (_rectSelection.Update(point))
            {
                _overlayManager?.Invalidate();
            }

            return;
        }

        if (_mode == InteractionMode.RegionMaskSelect)
        {
            if (_regionMasks.UpdateEdit(point))
            {
                _overlayManager?.Invalidate();
                return;
            }

            if (_rectSelection.Update(point))
            {
                _overlayManager?.Invalidate();
            }

            return;
        }

        if (!IsAnnotationMode(_mode))
        {
            return;
        }

        if (_pendingTextEditMove)
        {
            if (point.DistanceTo(_pendingTextEditMovePoint) < MovementThresholdPixels * 4)
            {
                return;
            }

            _annotations.CommitTextInput();
            if (_annotations.BeginSelectionMove(_pendingTextEditMovePoint))
            {
                _movingSelection = true;
                _lastSelectionMovePoint = _pendingTextEditMovePoint;
                _annotations.MoveSelectionBy(point.X - _lastSelectionMovePoint.X, point.Y - _lastSelectionMovePoint.Y);
                _lastSelectionMovePoint = point;
            }

            _pendingTextEditMove = false;
            return;
        }

        if (_draggingAnnotationEditHandle)
        {
            _annotations.UpdateObjectEditHandleDrag(point, (modifiers & ModifierKeys.Shift) != 0);
            return;
        }

        if (_movingSelection)
        {
            _annotations.MoveSelectionBy(point.X - _lastSelectionMovePoint.X, point.Y - _lastSelectionMovePoint.Y);
            _lastSelectionMovePoint = point;
            return;
        }

        if (!_drawing)
        {
            return;
        }

        if (CurrentTool == AnnotationTool.Move)
        {
            _annotations.UpdateSelection(point);
            return;
        }

        _annotations.UpdateStroke(point, (modifiers & ModifierKeys.Shift) != 0);
    }

    public void HandleOverlayMouseUp(ScreenPoint point, MouseButton button, ModifierKeys modifiers)
    {
        if (_mode == InteractionMode.PinnedLensSelect)
        {
            if (button != MouseButton.Left)
            {
                return;
            }

            var sourceRect = _rectSelection.Complete(point);
            if (sourceRect is null)
            {
                return;
            }

            var completedSourceRect = sourceRect.Value;
            if (completedSourceRect.Width >= 16 && completedSourceRect.Height >= 16)
            {
                SetInteractionMode(InteractionMode.Passthrough);
                _pinnedLenses.Open(completedSourceRect);
            }
            else
            {
                SetInteractionMode(InteractionMode.Passthrough);
            }

            return;
        }

        if (_mode == InteractionMode.ScreenshotRegionSelect)
        {
            if (button != MouseButton.Left)
            {
                return;
            }

            if (_screenshotRegionEdit.IsResizing)
            {
                _screenshotRegionEdit.EndPointerAction();
                _overlayManager?.Invalidate();
                return;
            }

            if (_screenshotRegionEdit.IsMoving)
            {
                _screenshotRegionEdit.EndPointerAction();
                _overlayManager?.Invalidate();
                return;
            }

            var sourceRect = _rectSelection.Complete(point);
            if (sourceRect is null)
            {
                return;
            }

            var completedSourceRect = sourceRect.Value;
            if (RectGeometry.IsLargeEnough(completedSourceRect))
            {
                _pendingScreenshotRegion = completedSourceRect;
                _screenshotRegionToolbarRestorePending = _restoreToolbarAfterRectSelection;
                _restoreToolbarAfterRectSelection = false;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }

            _overlayManager?.Invalidate();
            return;
        }

        if (_mode == InteractionMode.RegionSpotlightSelect)
        {
            if (button != MouseButton.Left)
            {
                return;
            }

            if (_regionSpotlights.IsResizing)
            {
                _regionSpotlights.EndPointerAction();
                _overlayManager?.Invalidate();
                return;
            }

            if (_regionSpotlights.IsMoving)
            {
                _regionSpotlights.EndPointerAction();
                _overlayManager?.Invalidate();
                return;
            }

            var sourceRect = _rectSelection.Complete(point);
            if (sourceRect is null)
            {
                return;
            }

            var completedSourceRect = sourceRect.Value;
            if (RectGeometry.IsLargeEnough(completedSourceRect))
            {
                var hadSpotlightRegions = _regionSpotlights.HasRegions;
                _regionSpotlights.Add(completedSourceRect);
                if (!hadSpotlightRegions)
                {
                    RegisterHotKeys();
                }
            }

            RestoreToolbarAfterRectSelection();
            _overlayManager?.Invalidate();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_mode == InteractionMode.RegionMaskSelect)
        {
            if (_regionMasks.IsResizing && button == MouseButton.Left)
            {
                _regionMasks.EndPointerAction();
                _overlayManager?.Invalidate();
                return;
            }

            if (_regionMasks.IsMoving && button == MouseButton.Left)
            {
                _regionMasks.EndPointerAction();
                _overlayManager?.Invalidate();
                return;
            }

            if (button != MouseButton.Left)
            {
                return;
            }

            var maskRect = _rectSelection.Complete(point);
            if (maskRect is null)
            {
                return;
            }

            var completedMaskRect = maskRect.Value;
            if (RectGeometry.IsLargeEnough(completedMaskRect))
            {
                _regionMasks.Add(completedMaskRect, Settings);
            }

            RestoreToolbarAfterRectSelection();
            _overlayManager?.Invalidate();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!IsAnnotationMode(_mode) || button != MouseButton.Left)
        {
            return;
        }

        if (_pendingTextEditMove)
        {
            _pendingTextEditMove = false;
            return;
        }

        if (_draggingAnnotationEditHandle)
        {
            _annotations.EndObjectEditHandleDrag();
            _draggingAnnotationEditHandle = false;
            TryCompletePushToAnnotateExit();
            return;
        }

        if (_movingSelection)
        {
            _annotations.EndSelectionMove();
            _movingSelection = false;
            TryCompletePushToAnnotateExit();
            return;
        }

        if (!_drawing)
        {
            return;
        }

        if (CurrentTool == AnnotationTool.Move)
        {
            _annotations.UpdateSelection(point);
            _annotations.CommitSelection();
            _drawing = false;
            TryCompletePushToAnnotateExit();
            return;
        }

        _annotations.UpdateStroke(point, (modifiers & ModifierKeys.Shift) != 0);
        _annotations.CommitStroke();
        _drawing = false;
        TryCompletePushToAnnotateExit();
    }

    public bool HandleOverlayMouseWheel(ScreenPoint point, int delta, ModifierKeys modifiers)
    {
        if (_mode != InteractionMode.RegionMaskSelect
            || delta == 0
            || (modifiers & ModifierKeys.Control) == 0
            || (modifiers & ~(ModifierKeys.Control | ModifierKeys.Shift)) != 0)
        {
            return false;
        }

        if (!_regionMasks.TryGetSelectedOrHit(point, out var mask))
        {
            return false;
        }

        var step = (modifiers & ModifierKeys.Shift) != 0 ? 0.01 : 0.05;
        var nextOpacity = Math.Clamp(mask.Opacity + Math.Sign(delta) * step, 0.1, 1.0);
        if (Math.Abs(mask.Opacity - nextOpacity) < 0.001)
        {
            return true;
        }

        var updated = Settings.Clone();
        updated.RegionMaskOpacity = nextOpacity;
        ApplySettings(updated);
        mask.SetOpacity(Settings.RegionMaskOpacity);
        _overlayManager?.Invalidate();
        return true;
    }

    public void HandleOverlayCaptureLost()
    {
        if (_mode == InteractionMode.PinnedLensSelect)
        {
            SetInteractionMode(InteractionMode.Passthrough);
            return;
        }

        if (_mode == InteractionMode.ScreenshotRegionSelect)
        {
            _screenshotRegionEdit.Cancel();
            if (_rectSelection.IsActive)
            {
                _rectSelection.Cancel();
            }

            _overlayManager?.Invalidate();
            return;
        }

        if (_mode == InteractionMode.RegionSpotlightSelect)
        {
            _regionSpotlights.CancelEdit();
            if (_rectSelection.IsActive)
            {
                _rectSelection.Cancel();
            }

            _overlayManager?.Invalidate();
            return;
        }

        if (_mode == InteractionMode.RegionMaskSelect)
        {
            if (_regionMasks.IsMoving || _regionMasks.IsResizing)
            {
                _regionMasks.CancelEdit();
                _overlayManager?.Invalidate();
            }

            if (_rectSelection.IsActive)
            {
                _rectSelection.Cancel();
                _overlayManager?.Invalidate();
            }

            return;
        }

        if (_movingSelection)
        {
            _annotations.EndSelectionMove();
            _movingSelection = false;
        }

        _pendingTextEditMove = false;

        if (_draggingAnnotationEditHandle)
        {
            _annotations.EndObjectEditHandleDrag();
            _draggingAnnotationEditHandle = false;
        }

        if (_drawing)
        {
            _annotations.CancelDraft();
            _drawing = false;
        }

        TryCompletePushToAnnotateExit();
    }

    public bool HandleOverlayKeyDown(Key key, ModifierKeys modifiers)
    {
        if (IsRectSelectionMode(_mode))
        {
            if (Matches(key, modifiers, ExitVisualShortcut) || Matches(key, modifiers, Settings.Shortcuts.ExitAnnotate))
            {
                SetInteractionMode(InteractionMode.Passthrough);
                return true;
            }

            if (_mode == InteractionMode.RegionMaskSelect)
            {
                if ((key == Key.Back || key == Key.Delete) && modifiers == ModifierKeys.None)
                {
                    DeleteSelectedRegionMask();
                    return true;
                }

                return false;
            }

            if (_mode == InteractionMode.ScreenshotRegionSelect)
            {
                if (key == Key.Enter && modifiers == ModifierKeys.None)
                {
                    CommitPendingScreenshotRegion();
                    return true;
                }

                if (TryNudgeScreenshotRegion(key, modifiers))
                {
                    return true;
                }

                return false;
            }

            if (_mode == InteractionMode.RegionSpotlightSelect)
            {
                if (key == Key.Back && modifiers == ModifierKeys.None)
                {
                    DeleteSelectedSpotlightRegion();
                    return true;
                }

                if (key == Key.Enter && modifiers == ModifierKeys.None)
                {
                    SetInteractionMode(InteractionMode.Passthrough);
                    return true;
                }

                if (TryNudgeSelectedSpotlightRegion(key, modifiers))
                {
                    return true;
                }

                return false;
            }

            return false;
        }

        if (!IsAnnotationMode(_mode))
        {
            return false;
        }

        var shortcuts = Settings.Shortcuts;

        if (key == Key.V && modifiers == ModifierKeys.Control)
        {
            return TryPasteClipboardAnnotation();
        }

        var annotationModifiers = GetAnnotationShortcutModifiers(modifiers);

        if (_annotations.HasTextInput)
        {
            if (key == Key.Enter && annotationModifiers == ModifierKeys.Shift)
            {
                _annotations.AppendText("\n");
                return true;
            }

            if (MatchesAnnotationShortcut(key, modifiers, "Enter"))
            {
                _annotations.CommitTextInput();
                TryCompletePushToAnnotateExit();
                return true;
            }

            if (MatchesAnnotationShortcut(key, modifiers, "Back"))
            {
                _annotations.BackspaceText();
                return true;
            }

            if (key == Key.Delete && annotationModifiers == ModifierKeys.None)
            {
                _annotations.DeleteText();
                return true;
            }

            if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ExitAnnotate))
            {
                if (_annotations.IsEditingText)
                {
                    _annotations.CancelTextEdit();
                    TryCompletePushToAnnotateExit();
                }
                else
                {
                    SetInteractionMode(InteractionMode.Passthrough);
                }

                return true;
            }

            return false;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ExitAnnotate))
        {
            if (_annotations.IsObjectEditing)
            {
                _annotations.EndObjectEdit();
                TryCompletePushToAnnotateExit();
                return true;
            }

            SetInteractionMode(InteractionMode.Passthrough);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Undo))
        {
            UndoAnnotation();
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Redo))
        {
            RedoAnnotation();
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.DeleteSelection))
        {
            DeleteSelectedAnnotations();
            TryCompletePushToAnnotateExit();
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Clear) || MatchesAnnotationShortcut(key, modifiers, shortcuts.ClearAlternate))
        {
            ClearAnnotations();
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ThicknessDown))
        {
            AdjustAnnotationThickness(-1);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ThicknessUp))
        {
            AdjustAnnotationThickness(1);
            return true;
        }

        if (TrySelectTool(key, modifiers) || TrySelectColor(key, modifiers))
        {
            return true;
        }

        return false;
    }

    private void DeleteSelectedSpotlightRegion()
    {
        var hadRegions = _regionSpotlights.HasRegions;
        if (!_regionSpotlights.DeleteSelected())
        {
            return;
        }

        if (hadRegions && !_regionSpotlights.HasRegions)
        {
            RegisterHotKeys();
        }

        _overlayManager?.Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryPasteClipboardAnnotation()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                var image = System.Windows.Clipboard.GetImage();
                if (image is null)
                {
                    return false;
                }

                var frozen = FreezeClipboardImage(image);
                var rect = CreatePastedImageRect(frozen, GetPasteAnchorPoint());
                return _annotations.AddPastedImage(frozen, rect);
            }

            if (!System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.UnicodeText))
            {
                return false;
            }

            var text = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (_annotations.HasTextInput)
            {
                _annotations.AppendText(text.Replace("\r\n", "\n").Replace('\r', '\n'));
                return true;
            }

            return _annotations.AddPastedText(text, GetPasteAnchorPoint(), Settings);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException or InvalidOperationException)
        {
            AppLog.Error("Could not paste clipboard annotation.", ex);
            return false;
        }
    }

    private static BitmapSource FreezeClipboardImage(BitmapSource image)
    {
        if (image.IsFrozen)
        {
            return image;
        }

        var clone = image.Clone();
        clone.Freeze();
        return clone;
    }

    private static ScreenPoint GetPasteAnchorPoint()
    {
        if (TryGetCursor(out var cursor))
        {
            return cursor;
        }

        var screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        return new ScreenPoint(
            screen.Bounds.Left + screen.Bounds.Width / 2.0,
            screen.Bounds.Top + screen.Bounds.Height / 2.0);
    }

    private static ScreenRect CreatePastedImageRect(BitmapSource image, ScreenPoint anchor)
    {
        var screen = Forms.Screen.FromPoint(new DrawingPoint((int)Math.Round(anchor.X), (int)Math.Round(anchor.Y)));
        var maxWidth = Math.Max(160, screen.Bounds.Width * 0.62);
        var maxHeight = Math.Max(120, screen.Bounds.Height * 0.62);
        var width = Math.Max(1, image.PixelWidth);
        var height = Math.Max(1, image.PixelHeight);
        var scale = Math.Min(1, Math.Min(maxWidth / width, maxHeight / height));
        var displayWidth = Math.Max(1, width * scale);
        var displayHeight = Math.Max(1, height * scale);
        var left = anchor.X - displayWidth / 2;
        var top = anchor.Y - displayHeight / 2;

        return new ScreenRect(left, top, left + displayWidth, top + displayHeight);
    }

    private bool HandleTextObjectClick(ScreenPoint point)
    {
        if (_annotations.IsEditingText)
        {
            if (!_annotations.TextEditContains(point))
            {
                _annotations.CommitTextInput();
                _pendingTextEditMove = false;
                ResetTextClickTracking();
                return true;
            }

            _pendingTextEditMove = true;
            _pendingTextEditMovePoint = point;
            ResetTextClickTracking();
            return true;
        }

        if (!_annotations.HitTestText(point))
        {
            ResetTextClickTracking();
            return false;
        }

        if (IsTextDoubleClick(point))
        {
            _annotations.CancelDraft();
            _annotations.TryBeginTextEditAt(point);
            ResetTextClickTracking();
            return true;
        }

        _lastTextClickPoint = point;
        _lastTextClickMs = NowMs();
        _hasLastTextClick = true;
        ResetObjectClickTracking();
        return true;
    }

    private bool HandleObjectEditClick(ScreenPoint point)
    {
        if (_annotations.IsObjectEditing)
        {
            if (_annotations.TryHitObjectEditHandle(point, out var handle)
                && _annotations.BeginObjectEditHandleDrag(handle))
            {
                _draggingAnnotationEditHandle = true;
                ResetObjectClickTracking();
                return true;
            }

            if (!_annotations.ObjectEditContains(point))
            {
                _annotations.EndObjectEdit();
                ResetObjectClickTracking();
                return true;
            }

            if (_annotations.BeginSelectionMove(point))
            {
                _movingSelection = true;
                _lastSelectionMovePoint = point;
                ResetObjectClickTracking();
                return true;
            }
        }

        if (CurrentTool == AnnotationTool.Move && _annotations.BeginSelectionMove(point))
        {
            _movingSelection = true;
            _lastSelectionMovePoint = point;
            ResetObjectClickTracking();
            return true;
        }

        if (!_annotations.HitTestShape(point))
        {
            ResetObjectClickTracking();
            return false;
        }

        if (IsObjectDoubleClick(point))
        {
            _annotations.CancelDraft();
            _annotations.TryBeginObjectEditAt(point);
            ResetObjectClickTracking();
            return true;
        }

        _lastObjectClickPoint = point;
        _lastObjectClickMs = NowMs();
        _hasLastObjectClick = true;
        ResetTextClickTracking();
        return false;
    }

    private bool IsTextDoubleClick(ScreenPoint point)
    {
        if (!_hasLastTextClick)
        {
            return false;
        }

        var elapsedMs = NowMs() - _lastTextClickMs;
        var size = Forms.SystemInformation.DoubleClickSize;
        return elapsedMs >= 0
            && elapsedMs <= Forms.SystemInformation.DoubleClickTime
            && Math.Abs(point.X - _lastTextClickPoint.X) <= size.Width / 2.0
            && Math.Abs(point.Y - _lastTextClickPoint.Y) <= size.Height / 2.0;
    }

    private bool IsObjectDoubleClick(ScreenPoint point)
    {
        if (!_hasLastObjectClick)
        {
            return false;
        }

        var elapsedMs = NowMs() - _lastObjectClickMs;
        var size = Forms.SystemInformation.DoubleClickSize;
        return elapsedMs >= 0
            && elapsedMs <= Forms.SystemInformation.DoubleClickTime
            && Math.Abs(point.X - _lastObjectClickPoint.X) <= size.Width / 2.0
            && Math.Abs(point.Y - _lastObjectClickPoint.Y) <= size.Height / 2.0;
    }

    private void ResetTextClickTracking()
    {
        _hasLastTextClick = false;
        _lastTextClickMs = double.NegativeInfinity;
    }

    private void ResetObjectClickTracking()
    {
        _hasLastObjectClick = false;
        _lastObjectClickMs = double.NegativeInfinity;
    }

    private void CommitPendingScreenshotRegion()
    {
        if (_pendingScreenshotRegion is not { } rect)
        {
            return;
        }

        var restoreToolbar = _screenshotRegionToolbarRestorePending;
        ResetScreenshotRegionEditState(restoreToolbar: false);
        _restoreToolbarAfterRectSelection = false;
        SetInteractionMode(InteractionMode.Passthrough);
        _ = TakeRegionScreenshotAsync(rect, restoreToolbar);
    }

    private bool TryNudgeScreenshotRegion(Key key, ModifierKeys modifiers)
    {
        if (_pendingScreenshotRegion is not { } rect || !TryGetNudgeDelta(key, modifiers, out var dx, out var dy))
        {
            return false;
        }

        _pendingScreenshotRegion = rect.Offset(dx, dy);
        _overlayManager?.Invalidate();
        return true;
    }

    private bool TryNudgeSelectedSpotlightRegion(Key key, ModifierKeys modifiers)
    {
        if (!TryGetNudgeDelta(key, modifiers, out var dx, out var dy))
        {
            return false;
        }

        if (!_regionSpotlights.NudgeSelected(dx, dy))
        {
            return false;
        }

        _overlayManager?.Invalidate();
        return true;
    }

    private static bool TryGetNudgeDelta(Key key, ModifierKeys modifiers, out double dx, out double dy)
    {
        dx = 0;
        dy = 0;

        if ((modifiers & ~ModifierKeys.Shift) != 0)
        {
            return false;
        }

        var step = (modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        switch (key)
        {
            case Key.Left:
                dx = -step;
                return true;
            case Key.Right:
                dx = step;
                return true;
            case Key.Up:
                dy = -step;
                return true;
            case Key.Down:
                dy = step;
                return true;
            default:
                return false;
        }
    }

    public void HandleOverlayTextInput(string text)
    {
        if (IsAnnotationMode(_mode) && _annotations.HasTextInput)
        {
            _annotations.AppendText(text);
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

        TrackExternalForegroundWindow();
        UpdatePushToAnnotate();

        var fadingAnnotationsAnimating = UpdateFadingAnnotations();
        var cursorHighlightAnimating = _pointerVisuals.UpdateCursorHighlight(force: false);
        var magnifierActive = Settings.MagnifierEnabled;
        var spotlightActive = IsSpotlightVisibleInMode(_mode);
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
            UpdateSpotlightCursor(force: false);
        }

        if (holdActive)
        {
            _pointerVisuals.TrackLaserWhileHeld(ActivationMode);
            return;
        }

        _pointerVisuals.FadeLaserAfterRelease();
        if (_pushToAnnotateActive)
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

    private void UpdatePushToAnnotate()
    {
        if (!_pushToAnnotateActive)
        {
            return;
        }

        if (!_pushToAnnotateExitPending)
        {
            PollPushToAnnotateShortcuts();
        }

        if (!_pushToAnnotateExitPending && _pushToAnnotateShortcut.IsPressed())
        {
            _timer.Interval = ActiveInterval;
            return;
        }

        _pushToAnnotatePolledShortcutDown.Clear();
        _pushToAnnotateExitPending = true;
        TryCompletePushToAnnotateExit();
        if (_pushToAnnotateActive)
        {
            _timer.Interval = ActiveInterval;
        }
    }

    private void TryCompletePushToAnnotateExit()
    {
        if (!_pushToAnnotateActive || !_pushToAnnotateExitPending || !CanExitPushToAnnotate())
        {
            return;
        }

        _pushToAnnotateActive = false;
        _pushToAnnotateExitPending = false;
        _pushToAnnotatePolledShortcutDown.Clear();
        if (_mode == InteractionMode.Annotate)
        {
            SetInteractionMode(InteractionMode.Passthrough);
        }
    }

    private bool CanExitPushToAnnotate()
    {
        return _mode == InteractionMode.Annotate
            && !_drawing
            && !_movingSelection
            && !_draggingAnnotationEditHandle
            && !_annotations.HasTextInput;
    }

    private void PollPushToAnnotateShortcuts()
    {
        if (_mode != InteractionMode.Annotate || _annotations.HasTextInput)
        {
            _pushToAnnotatePolledShortcutDown.Clear();
            return;
        }

        var shortcuts = Settings.Shortcuts;
        PollPushToAnnotateShortcut("clear-alt", shortcuts.ClearAlternate, ClearAnnotations);
        PollPushToAnnotateShortcut("tool-arrow", shortcuts.ToolArrow, () => SetAnnotationTool(AnnotationTool.Arrow));
        PollPushToAnnotateShortcut("tool-rectangle", shortcuts.ToolRectangle, () => SetAnnotationTool(AnnotationTool.Rectangle));
        PollPushToAnnotateShortcut("tool-ellipse", shortcuts.ToolEllipse, () => SetAnnotationTool(AnnotationTool.Ellipse));
        PollPushToAnnotateShortcut("tool-line", shortcuts.ToolLine, () => SetAnnotationTool(AnnotationTool.Line));
        PollPushToAnnotateShortcut("tool-pencil", shortcuts.ToolPencil, () => SetAnnotationTool(AnnotationTool.Pencil));
        PollPushToAnnotateShortcut("tool-highlighter", shortcuts.ToolHighlighter, () => SetAnnotationTool(AnnotationTool.Highlighter));
        PollPushToAnnotateShortcut("tool-text", shortcuts.ToolText, () => SetAnnotationTool(AnnotationTool.Text));
        PollPushToAnnotateShortcut("tool-move", shortcuts.ToolMove, () => SetAnnotationTool(AnnotationTool.Move));
        PollPushToAnnotateShortcut("tool-step", shortcuts.ToolStep, SelectStepTool);
        PollPushToAnnotateShortcut("color-1", shortcuts.Color1, () => SetAnnotationPresetColor(0));
        PollPushToAnnotateShortcut("color-2", shortcuts.Color2, () => SetAnnotationPresetColor(1));
        PollPushToAnnotateShortcut("color-3", shortcuts.Color3, () => SetAnnotationPresetColor(2));
        PollPushToAnnotateShortcut("color-4", shortcuts.Color4, () => SetAnnotationPresetColor(3));
        PollPushToAnnotateShortcut("color-5", shortcuts.Color5, () => SetAnnotationPresetColor(4));
    }

    private void PollPushToAnnotateShortcut(string id, string shortcutText, Action action)
    {
        if (ShortcutSettings.IsShortcutDisabled(shortcutText) || !Shortcut.TryParse(shortcutText, out var shortcut))
        {
            _pushToAnnotatePolledShortcutDown.Remove(id);
            return;
        }

        if (!shortcut.IsPressed())
        {
            _pushToAnnotatePolledShortcutDown.Remove(id);
            return;
        }

        if (_pushToAnnotatePolledShortcutDown.Add(id))
        {
            action();
        }
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
        if (_hasSpotlightCursor)
        {
            _magnifier.UpdateHost(_spotlightCursor);
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
        _spotlightCursor = cursor;
        _hasSpotlightCursor = true;
        if (force)
        {
            _overlayManager?.Invalidate();
        }
        else
        {
            _overlayManager?.InvalidateForCursor(cursor, previousPoint);
        }
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

    private void RestoreToolbarAfterRectSelection()
    {
        if (!_restoreToolbarAfterRectSelection)
        {
            return;
        }

        _restoreToolbarAfterRectSelection = false;
        if (!_disposed)
        {
            ShowToolbar();
        }
    }

    private void RestoreScreenshotRegionToolbarIfNeeded()
    {
        if (!_screenshotRegionToolbarRestorePending)
        {
            return;
        }

        _screenshotRegionToolbarRestorePending = false;
        if (!_disposed)
        {
            ShowToolbar();
        }
    }

    private void ResetScreenshotRegionEditState(bool restoreToolbar)
    {
        _pendingScreenshotRegion = null;
        _screenshotRegionEdit.Cancel();
        if (restoreToolbar)
        {
            RestoreScreenshotRegionToolbarIfNeeded();
        }
        else
        {
            _screenshotRegionToolbarRestorePending = false;
        }
    }

    private void ResetSpotlightRegionEditState()
    {
        _regionSpotlights.ResetEditState();
    }

    private RectOverlayVisual? GetRectOverlayVisual()
    {
        if (_rectSelection.Draft is { } draft)
        {
            return new RectOverlayVisual(
                draft,
                IsDraft: true,
                ShowHandles: false,
                ShowReadout: _mode == InteractionMode.ScreenshotRegionSelect);
        }

        if (_mode == InteractionMode.ScreenshotRegionSelect && _pendingScreenshotRegion is { } pending)
        {
            return new RectOverlayVisual(
                pending,
                IsDraft: false,
                ShowHandles: true,
                ShowReadout: true);
        }

        return null;
    }

    private void ExitVisualEffects()
    {
        if (!Settings.MagnifierEnabled && !_spotlightEnabled)
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
            ShouldRegisterExitVisualHotKey(_mode, Settings.MagnifierEnabled, HasExitVisualSpotlightEffect()),
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

    private bool HasExitVisualSpotlightEffect()
    {
        return _spotlightEnabled;
    }

    private static bool ShouldRegisterExitVisualHotKey(
        InteractionMode mode,
        bool magnifierEnabled,
        bool spotlightEnabled)
    {
        return !IsAnnotationMode(mode) && (magnifierEnabled || spotlightEnabled);
    }

    private ScreenPoint? GetSpotlightPoint()
    {
        return (IsSpotlightVisibleInMode(_mode) || Settings.MagnifierEnabled && !IsVisualBoardMode(_mode)) && _hasSpotlightCursor
            ? _spotlightCursor
            : null;
    }

    private void UpdateSpotlightCursor(bool force)
    {
        if (!TryGetCursor(out var cursor))
        {
            return;
        }

        if (!force && _hasSpotlightCursor && cursor.DistanceTo(_spotlightCursor) < MovementThresholdPixels)
        {
            return;
        }

        var previous = _hasSpotlightCursor ? _spotlightCursor : cursor;
        _spotlightCursor = cursor;
        _hasSpotlightCursor = true;
        if (force)
        {
            _overlayManager?.Invalidate();
        }
        else
        {
            _overlayManager?.InvalidateForCursor(cursor, previous);
        }
    }

    private void CacheParsedSettings()
    {
        _pointerVisuals.CacheParsedSettings();

        if (ShortcutSettings.IsShortcutDisabled(Settings.Shortcuts.PushToAnnotate))
        {
            _pushToAnnotateShortcut = default;
        }
        else if (!Shortcut.TryParse(Settings.Shortcuts.PushToAnnotate, out _pushToAnnotateShortcut)
            || _pushToAnnotateShortcut.IsMouseButton)
        {
            Settings.Shortcuts.PushToAnnotate = "Ctrl+Space";
            Shortcut.TryParse(Settings.Shortcuts.PushToAnnotate, out _pushToAnnotateShortcut);
        }
    }

    private bool TrySelectTool(Key key, ModifierKeys modifiers)
    {
        var shortcuts = Settings.Shortcuts;
        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolArrow))
        {
            SetAnnotationTool(AnnotationTool.Arrow);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolRectangle))
        {
            SetAnnotationTool(AnnotationTool.Rectangle);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolEllipse))
        {
            SetAnnotationTool(AnnotationTool.Ellipse);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolLine))
        {
            SetAnnotationTool(AnnotationTool.Line);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolPencil))
        {
            SetAnnotationTool(AnnotationTool.Pencil);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolHighlighter))
        {
            SetAnnotationTool(AnnotationTool.Highlighter);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolText))
        {
            SetAnnotationTool(AnnotationTool.Text);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolMove))
        {
            SetAnnotationTool(AnnotationTool.Move);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.ToolStep))
        {
            SelectStepTool();
            return true;
        }

        return false;
    }

    private bool TrySelectColor(Key key, ModifierKeys modifiers)
    {
        var shortcuts = Settings.Shortcuts;
        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Color1))
        {
            SetAnnotationPresetColor(0);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Color2))
        {
            SetAnnotationPresetColor(1);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Color3))
        {
            SetAnnotationPresetColor(2);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Color4))
        {
            SetAnnotationPresetColor(3);
            return true;
        }

        if (MatchesAnnotationShortcut(key, modifiers, shortcuts.Color5))
        {
            SetAnnotationPresetColor(4);
            return true;
        }

        return false;
    }

    private static bool Matches(Key key, ModifierKeys modifiers, string shortcutText)
    {
        return Shortcut.TryParse(shortcutText, out var shortcut) && shortcut.Matches(key, modifiers);
    }

    private bool MatchesAnnotationShortcut(Key key, ModifierKeys modifiers, string shortcutText)
    {
        if (Matches(key, modifiers, shortcutText))
        {
            return true;
        }

        if (!_pushToAnnotateActive || _pushToAnnotateShortcut.Modifiers == ModifierKeys.None)
        {
            return false;
        }

        var strippedModifiers = GetAnnotationShortcutModifiers(modifiers);
        return strippedModifiers != modifiers && Matches(key, strippedModifiers, shortcutText);
    }

    private ModifierKeys GetAnnotationShortcutModifiers(ModifierKeys modifiers)
    {
        return _pushToAnnotateActive
            ? modifiers & ~_pushToAnnotateShortcut.Modifiers
            : modifiers;
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

    private bool IsSpotlightVisibleInMode(InteractionMode mode)
    {
        return _spotlightEnabled && !IsVisualBoardMode(mode);
    }

    private static bool IsVisualBoardMode(InteractionMode mode)
    {
        return mode is InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
    }

    private double NowMs() => _clock.Elapsed.TotalMilliseconds;

    // Continuously remember the last foreground window that is not one of ours, so
    // entering Annotate via a toolbar-button click (where our own window is briefly
    // foreground) can still restore focus to the real underlying app on exit.
    private void TrackExternalForegroundWindow()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return;
        }

        _ = NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (processId != 0 && processId != (uint)Environment.ProcessId)
        {
            _lastExternalForegroundWindow = foreground;
        }
    }

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

}
