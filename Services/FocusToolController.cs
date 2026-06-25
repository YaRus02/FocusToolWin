using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Media;
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
    private const double RegionMaskMinSizePixels = 8;
    private const double RegionMaskResizeHitRadiusPixels = 12;
    private const string ExitVisualShortcut = "Esc";

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SettingsStore _settingsStore = new();
    private readonly ScreenshotService _screenshotService = new();
    private readonly TrailModel _trail = new();
    private readonly AnnotationDocument _annotations;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _settingsSaveTimer;
    private readonly DispatcherTimer _pinnedLensRefreshTimer;

    private OverlayManager? _overlayManager;
    private MagnifierHostWindow? _magnifierHost;
    private readonly List<PinnedLensHostWindow> _pinnedLensHosts = [];
    private readonly List<RegionMask> _regionMasks = [];
    private TimerController? _timerController;
    private TrayIconController? _trayIcon;
    private HotKeyManager? _hotKeyManager;
    private SettingsWindow? _settingsWindow;
    private OverlayToolbarWindow? _toolbarWindow;
    private ScreenBoardFrame? _screenBoardFrame;
    private Shortcut _laserHoldShortcut;
    private ScreenPoint _lastCursor;
    private ScreenPoint _pinnedLensSelectionStart;
    private ScreenPoint _regionMaskSelectionStart;
    private ScreenPoint _lastSelectionMovePoint;
    private ScreenPoint _lastRegionMaskMovePoint;
    // Shared rubber-band rect for the pinned-lens and region-mask select modes
    // (only one is ever active, so reusing one field is safe).
    private ScreenRect? _pinnedLensSelectionDraft;
    private bool _hasLastCursor;
    private bool _drawing;
    private bool _movingSelection;
    private bool _selectingPinnedLens;
    private bool _selectingRegionMask;
    private bool _restoreToolbarAfterPinnedLensSelection;
    private bool _restoreToolbarAfterRegionMaskSelection;
    private RegionMask? _movingRegionMask;
    private RegionMask? _resizingRegionMask;
    private ScreenPoint _regionMaskResizeAnchor;
    private Forms.ContextMenuStrip? _regionMaskContextMenu;
    private int _regionMaskContextMenuMaskId;
    private bool _regionMaskContextMenuActionTaken;
    private int _nextRegionMaskId = 1;
    private bool _captureInProgress;
    private bool _disposed;
    private bool _laserVisuallyActive;
    private bool _spotlightEnabled;
    private bool _hasSpotlightCursor;
    private bool _magnifierRenderingSubscribed;
    private ScreenPoint _spotlightCursor;
    private IntPtr _previousForegroundWindow = IntPtr.Zero;
    private IntPtr _lastExternalForegroundWindow = IntPtr.Zero;
    private ScreenPoint _lastMagnifierCursor;
    private bool _hasLastMagnifierCursor;
    private InteractionMode _mode = InteractionMode.Passthrough;

    public event EventHandler? StateChanged;

    public AppSettings Settings { get; private set; }
    public string SettingsFilePath => _settingsStore.SettingsFilePath;
    public InteractionMode Mode => _mode;
    public LaserActivationMode ActivationMode => Settings.GetLaserActivationMode();
    public AnnotationTool CurrentTool => Settings.GetAnnotationTool();
    public AnnotationDocument Annotations => _annotations;
    public bool LaserVisuallyActive => _laserVisuallyActive;
    public bool SpotlightEnabled => _spotlightEnabled;
    public bool MagnifierEnabled => Settings.MagnifierEnabled;
    public bool ToolbarVisible => _toolbarWindow?.IsVisible == true;
    public bool ScreenBoardEnabled => _mode == InteractionMode.ScreenBoard;
    public bool BlackScreenEnabled => _mode == InteractionMode.BlackScreen;
    public bool WhiteScreenEnabled => _mode == InteractionMode.WhiteScreen;
    public bool PinnedLensActive => _pinnedLensHosts.Count > 0;
    public int PinnedLensCount => _pinnedLensHosts.Count;
    public bool PinnedLensSelectionActive => _mode == InteractionMode.PinnedLensSelect;
    public bool RegionMaskActive => _regionMasks.Count > 0;
    public int RegionMaskCount => _regionMasks.Count;
    public bool RegionMaskSelectionActive => _mode == InteractionMode.RegionMaskSelect;
    public bool FadingAnnotationsEnabled => Settings.FadingAnnotationsEnabled;
    public string MagnifierShortcut => Settings.Shortcuts.ToggleMagnifier;
    public string PinnedLensShortcut => Settings.Shortcuts.TogglePinnedLens;
    public string RegionMaskShortcut => Settings.Shortcuts.ToggleRegionMask;
    public string ClearRegionMasksShortcut => Settings.Shortcuts.ClearRegionMasks;
    public string FadingAnnotationsShortcut => Settings.Shortcuts.ToggleFadingAnnotations;
    public bool TimerActive => _timerController is { ActiveCount: > 0 };
    public int TimerCount => _timerController?.ActiveCount ?? 0;
    public string TimerShortcut => Settings.Shortcuts.ToggleTimer;
    public string ToolbarShortcut => Settings.Shortcuts.ToggleToolbar;
    public string ScreenshotShortcut => Settings.Shortcuts.TakeScreenshot;
    public string ScreenBoardShortcut => Settings.Shortcuts.ToggleScreenBoard;

    public FocusToolController()
    {
        _annotations = new AnnotationDocument(NowMs);
        Settings = _settingsStore.Load();
        CacheParsedSettings();
        Settings.SpotlightEnabled = false;
        Settings.MagnifierEnabled = false;
        _spotlightEnabled = false;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = IdleInterval
        };
        _timer.Tick += OnTimerTick;

        // Coalesce frequent settings mutations (thickness/tool/color hotkeys) into a
        // single debounced disk write instead of serializing + writing the file on
        // every change. Flushed synchronously on Dispose.
        _settingsSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _settingsSaveTimer.Tick += OnSettingsSaveTick;

        _pinnedLensRefreshTimer = new DispatcherTimer(DispatcherPriority.Render);
        _pinnedLensRefreshTimer.Tick += OnPinnedLensRefreshTick;
        UpdatePinnedLensRefreshInterval();

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

        SaveSettingsDebounced();
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

        SaveSettingsDebounced();
    }

    private void SaveSettingsDebounced()
    {
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void OnSettingsSaveTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _settingsSaveTimer.Stop();

        // Write off the UI thread so a slow disk / AV scan can't hitch the overlay.
        // A snapshot is serialized so the background write never races UI-thread
        // mutations of the live Settings; Dispose still flushes synchronously.
        var snapshot = Settings.Clone();
        _ = Task.Run(() => _settingsStore.Save(snapshot));
    }

    public void Start()
    {
        _overlayManager = new OverlayManager(_trail, _annotations, () => Settings, () => _mode, NowMs, GetSpotlightPoint, () => _screenBoardFrame, () => _pinnedLensSelectionDraft, () => _regionMasks, this, ReassertPinnedLensTopmost, ReassertFloatingChromeTopmost);
        _trayIcon = new TrayIconController(this);
        _timerController = new TimerController(NowMs, () => Settings.Timer, ApplyTimerDefaults, AddTimerLabelToHistory, OnTimerActiveCountChanged);
        RegisterHotKeys();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _overlayManager.Show();
        _timer.Start();
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (_settingsStore.WasCreatedOnLoad)
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

    public void ClearRegionMasks()
    {
        var hadMasks = _regionMasks.Count > 0;
        var wasSelecting = _mode == InteractionMode.RegionMaskSelect;
        if (!hadMasks && !wasSelecting)
        {
            return;
        }

        _regionMasks.Clear();
        _selectingRegionMask = false;
        _movingRegionMask = null;
        _resizingRegionMask = null;
        if (wasSelecting)
        {
            _pinnedLensSelectionDraft = null;
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
        for (var i = _regionMasks.Count - 1; i >= 0; i--)
        {
            if (_regionMasks[i].Contains(point))
            {
                mask = _regionMasks[i];
                return true;
            }
        }

        mask = null!;
        return false;
    }

    private bool TryHitRegionMaskResizeHandle(ScreenPoint point, out RegionMask mask, out RegionMaskResizeHandle handle)
    {
        var hitRadiusSquared = RegionMaskResizeHitRadiusPixels * RegionMaskResizeHitRadiusPixels;
        for (var i = _regionMasks.Count - 1; i >= 0; i--)
        {
            var rect = _regionMasks[i].Rect;
            if (DistanceSquared(point, new ScreenPoint(rect.Left, rect.Top)) <= hitRadiusSquared)
            {
                mask = _regionMasks[i];
                handle = RegionMaskResizeHandle.TopLeft;
                return true;
            }

            if (DistanceSquared(point, new ScreenPoint(rect.Right, rect.Top)) <= hitRadiusSquared)
            {
                mask = _regionMasks[i];
                handle = RegionMaskResizeHandle.TopRight;
                return true;
            }

            if (DistanceSquared(point, new ScreenPoint(rect.Left, rect.Bottom)) <= hitRadiusSquared)
            {
                mask = _regionMasks[i];
                handle = RegionMaskResizeHandle.BottomLeft;
                return true;
            }

            if (DistanceSquared(point, new ScreenPoint(rect.Right, rect.Bottom)) <= hitRadiusSquared)
            {
                mask = _regionMasks[i];
                handle = RegionMaskResizeHandle.BottomRight;
                return true;
            }
        }

        mask = null!;
        handle = RegionMaskResizeHandle.None;
        return false;
    }

    private static double DistanceSquared(ScreenPoint first, ScreenPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private static ScreenPoint GetRegionMaskResizeAnchor(ScreenRect rect, RegionMaskResizeHandle handle)
    {
        return handle switch
        {
            RegionMaskResizeHandle.TopLeft => new ScreenPoint(rect.Right, rect.Bottom),
            RegionMaskResizeHandle.TopRight => new ScreenPoint(rect.Left, rect.Bottom),
            RegionMaskResizeHandle.BottomLeft => new ScreenPoint(rect.Right, rect.Top),
            RegionMaskResizeHandle.BottomRight => new ScreenPoint(rect.Left, rect.Top),
            _ => new ScreenPoint(rect.Left, rect.Top)
        };
    }

    private static ScreenRect CreateRegionMaskResizeRect(ScreenPoint anchor, ScreenPoint point)
    {
        var left = point.X < anchor.X
            ? Math.Min(point.X, anchor.X - RegionMaskMinSizePixels)
            : anchor.X;
        var right = point.X < anchor.X
            ? anchor.X
            : Math.Max(point.X, anchor.X + RegionMaskMinSizePixels);
        var top = point.Y < anchor.Y
            ? Math.Min(point.Y, anchor.Y - RegionMaskMinSizePixels)
            : anchor.Y;
        var bottom = point.Y < anchor.Y
            ? anchor.Y
            : Math.Max(point.Y, anchor.Y + RegionMaskMinSizePixels);

        return new ScreenRect(left, top, right, bottom);
    }

    private void DeleteRegionMask(int maskId, bool exitMaskMode)
    {
        var index = _regionMasks.FindIndex(mask => mask.Id == maskId);
        if (index < 0)
        {
            return;
        }

        _regionMasks.RemoveAt(index);
        _movingRegionMask = null;
        _resizingRegionMask = null;
        _selectingRegionMask = false;
        _pinnedLensSelectionDraft = null;
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

    private void ShowRegionMaskContextMenu(ScreenPoint point, int maskId)
    {
        var menu = GetRegionMaskContextMenu();
        if (menu.Visible)
        {
            _regionMaskContextMenuActionTaken = true;
            menu.Close(Forms.ToolStripDropDownCloseReason.CloseCalled);
        }

        _regionMaskContextMenuMaskId = maskId;
        _regionMaskContextMenuActionTaken = false;
        menu.Show(new DrawingPoint((int)Math.Round(point.X), (int)Math.Round(point.Y)));
        ReassertRegionMaskContextMenuTopmost(menu);
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            () => ReassertRegionMaskContextMenuTopmost(menu),
            DispatcherPriority.ContextIdle);
    }

    private Forms.ContextMenuStrip GetRegionMaskContextMenu()
    {
        if (_regionMaskContextMenu is not null)
        {
            return _regionMaskContextMenu;
        }

        var menu = new Forms.ContextMenuStrip();
        var deleteItem = menu.Items.Add("Delete mask");
        deleteItem.Click += (_, _) => DeleteRegionMaskFromContextMenu();
        menu.Opened += (_, _) => ReassertRegionMaskContextMenuTopmost(menu);
        menu.Closed += (_, _) => _regionMaskContextMenuActionTaken = false;

        _regionMaskContextMenu = menu;
        return menu;
    }

    private void DeleteRegionMaskFromContextMenu()
    {
        if (_regionMaskContextMenuActionTaken)
        {
            return;
        }

        _regionMaskContextMenuActionTaken = true;
        DeleteRegionMask(_regionMaskContextMenuMaskId, exitMaskMode: false);
    }

    private static void ReassertRegionMaskContextMenuTopmost(Forms.ContextMenuStrip menu)
    {
        if (menu.IsDisposed || menu.Disposing)
        {
            return;
        }

        if (menu.Handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            menu.Handle,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
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
        if (_toolbarWindow is { IsVisible: true })
        {
            HideToolbar();
            return;
        }

        ShowToolbar();
    }

    public void ShowToolbar()
    {
        if (_toolbarWindow is null)
        {
            _toolbarWindow = new OverlayToolbarWindow(this);
            _toolbarWindow.Closed += (_, _) =>
            {
                _toolbarWindow = null;
                if (!_disposed)
                {
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
            };
        }

        _toolbarWindow.ShowNearCursor();
        _overlayManager?.ReassertTopmost();
        _toolbarWindow.ReassertTopmost();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void HideToolbar()
    {
        if (_toolbarWindow is not { IsVisible: true })
        {
            return;
        }

        _toolbarWindow.Hide();
        StateChanged?.Invoke(this, EventArgs.Empty);
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
        _pinnedLensSelectionDraft = null;
        _selectingPinnedLens = false;
        _restoreToolbarAfterPinnedLensSelection = ToolbarVisible;
        if (_restoreToolbarAfterPinnedLensSelection)
        {
            _toolbarWindow?.Hide();
        }

        SetInteractionMode(InteractionMode.PinnedLensSelect);
    }

    private void BeginRegionMaskSelection()
    {
        _pinnedLensSelectionDraft = null;
        _selectingRegionMask = false;
        _restoreToolbarAfterRegionMaskSelection = ToolbarVisible;
        if (_restoreToolbarAfterRegionMaskSelection)
        {
            _toolbarWindow?.Hide();
        }

        SetInteractionMode(InteractionMode.RegionMaskSelect);
    }

    private async Task TakeScreenshotAsync()
    {
        if (_disposed || _captureInProgress)
        {
            return;
        }

        _captureInProgress = true;
        var toolbarWasVisible = ToolbarVisible;
        var magnifierWasActive = Settings.MagnifierEnabled;

        // Keep the screen overlay (region masks are a privacy layer that must be in the
        // shot), pinned lenses and timers visible. Only the toolbar and the cursor-
        // following magnifier lens are excluded from the capture.
        if (toolbarWasVisible)
        {
            _toolbarWindow?.Hide();
        }

        // UpdateMagnifierHost stays a no-op while _captureInProgress, so the render
        // loop will not bring the lens back before the capture completes.
        CloseMagnifierHost();

        if (toolbarWasVisible || magnifierWasActive)
        {
            await WaitForScreenRefreshAsync();
        }

        try
        {
            await _screenshotService.CaptureCurrentMonitorAsync(copyToClipboard: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not capture screenshot.", ex);
            _trayIcon?.ShowMessage("Screenshot failed", ex.Message);
        }
        finally
        {
            if (toolbarWasVisible && !_disposed)
            {
                ShowToolbar();
            }

            _captureInProgress = false;

            if (magnifierWasActive && !_disposed)
            {
                UpdateMagnifierHost();
            }
        }
    }

    private async Task EnterScreenBoardAsync()
    {
        if (_disposed || _captureInProgress)
        {
            return;
        }

        _captureInProgress = true;
        var toolbarWasVisible = ToolbarVisible;
        var previousMode = _mode;
        var magnifierWasEnabled = Settings.MagnifierEnabled;
        if (toolbarWasVisible)
        {
            _toolbarWindow?.Hide();
        }

        _overlayManager?.Hide();
        CloseMagnifierHost();
        HidePinnedLensesForBoard();
        await WaitForScreenRefreshAsync();

        try
        {
            _screenBoardFrame = await _screenshotService.CaptureCurrentMonitorFrameAsync();
            SetInteractionMode(InteractionMode.ScreenBoard);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not capture screen board.", ex);
            _trayIcon?.ShowMessage("Screen board failed", ex.Message);
            _overlayManager?.Show();
            SetInteractionMode(previousMode);
        }
        finally
        {
            if (toolbarWasVisible && !_disposed)
            {
                ShowToolbar();
            }

            if (magnifierWasEnabled && _mode != InteractionMode.ScreenBoard && !_disposed)
            {
                UpdateMagnifierHost();
            }

            // Restore pinned lenses unless we actually entered a board (then they stay
            // hidden until the board is dismissed, which restores them via SetInteractionMode).
            if (!IsVisualBoardMode(_mode) && !_disposed)
            {
                RestorePinnedLensesAfterBoard();
            }

            _captureInProgress = false;
        }
    }

    private static async Task WaitForScreenRefreshAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(70);
    }

    private void SaveScreenBoardSnapshot()
    {
        _ = SaveScreenBoardSnapshotAsync();
    }

    private async Task SaveScreenBoardSnapshotAsync()
    {
        if (_screenBoardFrame is not { } frame || _overlayManager is null)
        {
            return;
        }

        try
        {
            var image = _overlayManager.CaptureScreenBoardFrame(frame);
            if (image is null)
            {
                return;
            }

            await _screenshotService.SaveImageAsync(image, copyToClipboard: true, fileNamePrefix: "FocusTool_Board");
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not save screen board snapshot.", ex);
            _trayIcon?.ShowMessage("Screen board save failed", ex.Message);
        }
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

        var leavingAnnotationInput = IsAnnotationMode(_mode) && !IsAnnotationMode(mode);
        var enteringAnnotationInput = !IsAnnotationMode(_mode) && IsAnnotationMode(mode);
        var leavingPinnedLensSelection = _mode == InteractionMode.PinnedLensSelect && mode != InteractionMode.PinnedLensSelect;
        var leavingRegionMaskSelection = _mode == InteractionMode.RegionMaskSelect && mode != InteractionMode.RegionMaskSelect;
        var leavingScreenBoard = _mode == InteractionMode.ScreenBoard && mode != InteractionMode.ScreenBoard;
        var leavingVisualBoard = IsVisualBoardMode(_mode);
        var enteringVisualBoard = IsVisualBoardMode(mode);
        var exitVisualHotKeyWasNeeded = ShouldRegisterExitVisualHotKey(
            _mode,
            Settings.MagnifierEnabled,
            _spotlightEnabled);

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
            if (_annotations.HasDraftText)
            {
                _annotations.CommitTextDraft();
            }
            else
            {
                _annotations.CancelDraft();
            }

            _drawing = false;
            _movingSelection = false;
            _annotations.EndSelectionMove();
        }

        if (leavingPinnedLensSelection)
        {
            _selectingPinnedLens = false;
            _pinnedLensSelectionDraft = null;
            RestoreToolbarAfterPinnedLensSelection();
        }

        if (leavingRegionMaskSelection)
        {
            _selectingRegionMask = false;
            _movingRegionMask = null;
            _resizingRegionMask = null;
            _pinnedLensSelectionDraft = null;
            RestoreToolbarAfterRegionMaskSelection();
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
                _hasLastMagnifierCursor = false;
                UpdateSpotlightCursor(force: true);
                UpdateMagnifierHost();
            }
        }

        // Boards are a clean canvas: hide pinned lenses while a board is shown and
        // restore them on the way out. Timers intentionally stay visible.
        if (enteringVisualBoard)
        {
            HidePinnedLensesForBoard();
        }
        else if (leavingVisualBoard)
        {
            RestorePinnedLensesAfterBoard();
        }

        _overlayManager?.Invalidate();

        if (leavingAnnotationInput && _previousForegroundWindow != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(_previousForegroundWindow);
            _previousForegroundWindow = IntPtr.Zero;
        }

        SetLaserVisualActive(ActivationMode == LaserActivationMode.Always || IsAnnotationMode(_mode));
        var exitVisualHotKeyIsNeeded = ShouldRegisterExitVisualHotKey(
            _mode,
            Settings.MagnifierEnabled,
            _spotlightEnabled);
        if (exitVisualHotKeyWasNeeded != exitVisualHotKeyIsNeeded)
        {
            RegisterHotKeys();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplySettings(AppSettings settings)
    {
        var previousActivationMode = ActivationMode;
        var magnifierWasEnabled = Settings.MagnifierEnabled;
        var globalHotKeysChanged = !HaveSameGlobalHotKeys(Settings.Shortcuts, settings.Shortcuts);
        var exitVisualHotKeyWasNeeded = ShouldRegisterExitVisualHotKey(
            _mode,
            Settings.MagnifierEnabled,
            _spotlightEnabled);
        var magnifierVisualChanged = Math.Abs(Settings.MagnifierRadius - settings.MagnifierRadius) > 0.001
            || Math.Abs(Settings.MagnifierZoom - settings.MagnifierZoom) > 0.001;
        Settings.CopyFrom(settings);
        CacheParsedSettings();
        UpdatePinnedLensRefreshInterval();
        _spotlightEnabled = Settings.SpotlightEnabled;

        if (Settings.MagnifierEnabled)
        {
            SubscribeMagnifierRendering();
            UpdateSpotlightCursor(force: true);
            if (!magnifierWasEnabled || magnifierVisualChanged)
            {
                _hasLastMagnifierCursor = false;
                UpdateMagnifierHost();
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

        SaveSettingsDebounced();
        var exitVisualHotKeyIsNeeded = ShouldRegisterExitVisualHotKey(
            _mode,
            Settings.MagnifierEnabled,
            _spotlightEnabled);
        if (globalHotKeysChanged || exitVisualHotKeyWasNeeded != exitVisualHotKeyIsNeeded)
        {
            RegisterHotKeys();
        }

        if (previousActivationMode != ActivationMode)
        {
            _trail.Clear();
            _hasLastCursor = false;
        }
        else
        {
            _trail.TrimWhileMoving(NowMs(), RetainedTrailLengthMs);
        }

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
        var updated = Settings.Clone();
        updated.SetAnnotationTool(tool);
        ApplySettings(updated);

        if (tool != AnnotationTool.Move)
        {
            _annotations.ClearSelection();
        }
    }

    public void AdjustAnnotationThickness(double delta)
    {
        var updated = Settings.Clone();
        updated.AnnotationThickness += delta;
        ApplySettings(updated);
    }

    public void AdjustAnnotationFontSize(double delta)
    {
        var updated = Settings.Clone();
        updated.AnnotationFontSize += delta;
        ApplySettings(updated);
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
        var updated = Settings.Clone();
        updated.RegionMaskOpacity += delta;
        ApplySettings(updated);
    }

    public void SetRegionMaskColor(string color)
    {
        var updated = Settings.Clone();
        updated.RegionMaskColor = color;
        ApplySettings(updated);
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

            _selectingPinnedLens = true;
            _pinnedLensSelectionStart = point;
            _pinnedLensSelectionDraft = ScreenRect.FromPoints(point, point);
            _overlayManager?.Invalidate();
            return;
        }

        if (_mode == InteractionMode.RegionMaskSelect)
        {
            if (button == MouseButton.Right)
            {
                if (TryHitRegionMask(point, out var mask))
                {
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
                _resizingRegionMask = resizeMask;
                _regionMaskResizeAnchor = GetRegionMaskResizeAnchor(resizeMask.Rect, resizeHandle);
                _movingRegionMask = null;
                _selectingRegionMask = false;
                _pinnedLensSelectionDraft = null;
                return;
            }

            if (TryHitRegionMask(point, out var existingMask))
            {
                _movingRegionMask = existingMask;
                _resizingRegionMask = null;
                _lastRegionMaskMovePoint = point;
                _selectingRegionMask = false;
                _pinnedLensSelectionDraft = null;
                return;
            }

            _selectingRegionMask = true;
            _regionMaskSelectionStart = point;
            _pinnedLensSelectionDraft = ScreenRect.FromPoints(point, point);
            _overlayManager?.Invalidate();
            return;
        }

        if (!IsAnnotationMode(_mode) || button != MouseButton.Left)
        {
            return;
        }

        if (CurrentTool == AnnotationTool.Text)
        {
            if (_annotations.HasDraftText)
            {
                _annotations.CommitTextDraft();
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
            if (_selectingPinnedLens)
            {
                _pinnedLensSelectionDraft = ScreenRect.FromPoints(_pinnedLensSelectionStart, point);
                _overlayManager?.Invalidate();
            }

            return;
        }

        if (_mode == InteractionMode.RegionMaskSelect)
        {
            if (_resizingRegionMask is not null)
            {
                _resizingRegionMask.SetRect(CreateRegionMaskResizeRect(_regionMaskResizeAnchor, point));
                _overlayManager?.Invalidate();
                return;
            }

            if (_movingRegionMask is not null)
            {
                _movingRegionMask.MoveBy(point.X - _lastRegionMaskMovePoint.X, point.Y - _lastRegionMaskMovePoint.Y);
                _lastRegionMaskMovePoint = point;
                _overlayManager?.Invalidate();
                return;
            }

            if (_selectingRegionMask)
            {
                _pinnedLensSelectionDraft = ScreenRect.FromPoints(_regionMaskSelectionStart, point);
                _overlayManager?.Invalidate();
            }

            return;
        }

        if (!IsAnnotationMode(_mode))
        {
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
            if (!_selectingPinnedLens || button != MouseButton.Left)
            {
                return;
            }

            _selectingPinnedLens = false;
            var sourceRect = ScreenRect.FromPoints(_pinnedLensSelectionStart, point);
            _pinnedLensSelectionDraft = null;
            if (sourceRect.Width >= 16 && sourceRect.Height >= 16)
            {
                SetInteractionMode(InteractionMode.Passthrough);
                OpenPinnedLens(sourceRect);
            }
            else
            {
                SetInteractionMode(InteractionMode.Passthrough);
            }

            return;
        }

        if (_mode == InteractionMode.RegionMaskSelect)
        {
            if (_resizingRegionMask is not null && button == MouseButton.Left)
            {
                _resizingRegionMask = null;
                _overlayManager?.Invalidate();
                return;
            }

            if (_movingRegionMask is not null && button == MouseButton.Left)
            {
                _movingRegionMask = null;
                _overlayManager?.Invalidate();
                return;
            }

            if (!_selectingRegionMask || button != MouseButton.Left)
            {
                return;
            }

            _selectingRegionMask = false;
            var maskRect = ScreenRect.FromPoints(_regionMaskSelectionStart, point);
            _pinnedLensSelectionDraft = null;
            if (maskRect.Width >= RegionMaskMinSizePixels && maskRect.Height >= RegionMaskMinSizePixels)
            {
                _regionMasks.Add(new RegionMask(_nextRegionMaskId++, maskRect, Settings));
            }

            _overlayManager?.Invalidate();
            SetInteractionMode(InteractionMode.Passthrough);
            return;
        }

        if (!IsAnnotationMode(_mode) || button != MouseButton.Left)
        {
            return;
        }

        if (_movingSelection)
        {
            _annotations.EndSelectionMove();
            _movingSelection = false;
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
            return;
        }

        _annotations.UpdateStroke(point, (modifiers & ModifierKeys.Shift) != 0);
        _annotations.CommitStroke();
        _drawing = false;
    }

    public void HandleOverlayCaptureLost()
    {
        if (_mode == InteractionMode.PinnedLensSelect)
        {
            SetInteractionMode(InteractionMode.Passthrough);
            return;
        }

        if (_mode == InteractionMode.RegionMaskSelect)
        {
            if (_resizingRegionMask is not null)
            {
                _resizingRegionMask = null;
                _overlayManager?.Invalidate();
            }

            if (_movingRegionMask is not null)
            {
                _movingRegionMask = null;
                _overlayManager?.Invalidate();
            }

            if (_selectingRegionMask)
            {
                _selectingRegionMask = false;
                _pinnedLensSelectionDraft = null;
                _overlayManager?.Invalidate();
            }

            return;
        }

        if (_movingSelection)
        {
            _annotations.EndSelectionMove();
            _movingSelection = false;
        }

        if (_drawing)
        {
            _annotations.CancelDraft();
            _drawing = false;
        }
    }

    public bool HandleOverlayKeyDown(Key key, ModifierKeys modifiers)
    {
        if (_mode == InteractionMode.PinnedLensSelect)
        {
            if (Matches(key, modifiers, ExitVisualShortcut) || Matches(key, modifiers, Settings.Shortcuts.ExitAnnotate))
            {
                SetInteractionMode(InteractionMode.Passthrough);
                return true;
            }

            return false;
        }

        if (_mode == InteractionMode.RegionMaskSelect)
        {
            if (Matches(key, modifiers, ExitVisualShortcut) || Matches(key, modifiers, Settings.Shortcuts.ExitAnnotate))
            {
                SetInteractionMode(InteractionMode.Passthrough);
                return true;
            }

            return false;
        }

        if (!IsAnnotationMode(_mode))
        {
            return false;
        }

        var shortcuts = Settings.Shortcuts;

        if (_annotations.HasDraftText)
        {
            if (Matches(key, modifiers, "Enter"))
            {
                _annotations.CommitTextDraft();
                return true;
            }

            if (Matches(key, modifiers, "Back"))
            {
                _annotations.BackspaceText();
                return true;
            }

            if (Matches(key, modifiers, shortcuts.ExitAnnotate))
            {
                SetInteractionMode(InteractionMode.Passthrough);
                return true;
            }

            return false;
        }

        if (Matches(key, modifiers, shortcuts.ExitAnnotate))
        {
            SetInteractionMode(InteractionMode.Passthrough);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.Undo))
        {
            UndoAnnotation();
            return true;
        }

        if (Matches(key, modifiers, shortcuts.Redo))
        {
            RedoAnnotation();
            return true;
        }

        if (Matches(key, modifiers, shortcuts.DeleteSelection))
        {
            DeleteSelectedAnnotations();
            return true;
        }

        if (Matches(key, modifiers, shortcuts.Clear) || Matches(key, modifiers, shortcuts.ClearAlternate))
        {
            ClearAnnotations();
            return true;
        }

        if (Matches(key, modifiers, shortcuts.ThicknessDown))
        {
            AdjustAnnotationThickness(-1);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.ThicknessUp))
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

    public void HandleOverlayTextInput(string text)
    {
        if (IsAnnotationMode(_mode) && _annotations.HasDraftText)
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
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Tick -= OnSettingsSaveTick;
        _pinnedLensRefreshTimer.Stop();
        _pinnedLensRefreshTimer.Tick -= OnPinnedLensRefreshTick;
        UnsubscribeMagnifierRendering();
        _annotations.Changed -= OnAnnotationsChanged;
        _annotations.DraftProgressed -= OnAnnotationDraftProgressed;
        _regionMaskContextMenu?.Dispose();
        _regionMaskContextMenu = null;
        _hotKeyManager?.Dispose();
        CloseMagnifierHost();
        ClosePinnedLenses();
        _timerController?.Dispose();
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        _toolbarWindow?.Close();
        _overlayManager?.Dispose();
        _settingsStore.Save(Settings);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        TrackExternalForegroundWindow();

        var fadingAnnotationsAnimating = UpdateFadingAnnotations();
        var magnifierActive = Settings.MagnifierEnabled;
        var spotlightActive = IsSpotlightVisibleInMode(_mode);
        var holdActive = ActivationMode == LaserActivationMode.Always
            || _laserHoldShortcut.IsPressed();

        SetLaserVisualActive(holdActive || IsAnnotationMode(_mode));
        if (magnifierActive)
        {
            if (!_magnifierRenderingSubscribed)
            {
                UpdateSpotlightCursor(force: false);
                UpdateMagnifierHost();
                _overlayManager?.Invalidate();
            }
        }
        else if (spotlightActive)
        {
            UpdateSpotlightCursor(force: false);
        }

        if (holdActive)
        {
            TrackLaserWhileHeld();
            return;
        }

        FadeLaserAfterRelease();
        if (magnifierActive && !_magnifierRenderingSubscribed)
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

    private void TrackLaserWhileHeld()
    {
        if (!TryGetCursor(out var cursor))
        {
            _timer.Interval = IdleInterval;
            return;
        }

        var nowMs = NowMs();
        if (!_hasLastCursor)
        {
            if (ActivationMode == LaserActivationMode.Hold)
            {
                _trail.Clear();
            }

            _lastCursor = cursor;
            _hasLastCursor = true;
            _trail.AddPoint(cursor, nowMs);
        }
        else if (cursor.DistanceTo(_lastCursor) >= MovementThresholdPixels)
        {
            _lastCursor = cursor;
            _trail.AddPoint(cursor, nowMs);
        }
        else
        {
            var hadVisibleTrail = _trail.Points.Count > 1;
            _trail.TouchLastPoint(cursor, nowMs);
            _trail.TrimWhileMoving(nowMs, RetainedTrailLengthMs);
            _timer.Interval = hadVisibleTrail ? FadeInterval : IdleInterval;
            if (hadVisibleTrail)
            {
                _overlayManager?.Invalidate();
            }

            return;
        }

        _trail.TrimWhileMoving(nowMs, RetainedTrailLengthMs);
        _timer.Interval = ActiveInterval;
        _overlayManager?.Invalidate();
    }

    private void FadeLaserAfterRelease()
    {
        _hasLastCursor = false;

        if (_trail.Points.Count == 0 || _trail.LastMovementMs < 0)
        {
            _timer.Interval = IdleInterval;
            return;
        }

        var nowMs = NowMs();
        var stationaryMs = nowMs - _trail.LastMovementMs;
        if (stationaryMs > Settings.FadeDurationMs + 64)
        {
            _trail.Clear();
            _overlayManager?.Invalidate();
            _timer.Interval = IdleInterval;
            return;
        }

        _trail.TrimWhileStationary(RetainedTrailLengthMs);
        _timer.Interval = FadeInterval;
        _overlayManager?.Invalidate();
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
        if (_captureInProgress)
        {
            // The cursor-following magnifier lens must not appear in a screenshot or
            // screen-board frame; the render loop recreates it once capture ends.
            CloseMagnifierHost();
            return;
        }

        if (!Settings.MagnifierEnabled || IsVisualBoardMode(_mode) || !_hasSpotlightCursor || _overlayManager is null)
        {
            if (IsVisualBoardMode(_mode))
            {
                CloseMagnifierHost();
            }

            return;
        }

        var hostCreated = false;
        if (_magnifierHost is null)
        {
            var host = new MagnifierHostWindow();
            if (!host.IsAvailable)
            {
                host.Dispose();
                return;
            }

            _magnifierHost = host;
            hostCreated = true;
        }

        var dpiScale = _overlayManager.GetDpiScaleForPoint(_spotlightCursor);
        _magnifierHost.UpdateLens(_spotlightCursor, Settings, dpiScale, GetMagnifierExcludedWindows());
        if (!_magnifierHost.IsAvailable)
        {
            CloseMagnifierHost();
            return;
        }

        if (hostCreated)
        {
            _overlayManager.ReassertTopmost();
        }
    }

    // The cursor-following magnifier intentionally keeps the screen overlay in
    // its source so region masks remain a privacy layer inside the magnified
    // image. Only floating chrome is excluded to avoid self-capture.
    private IReadOnlyList<IntPtr> GetMagnifierExcludedWindows()
    {
        var handles = new List<IntPtr>();
        if (_toolbarWindow is { IsVisible: true } && _toolbarWindow.Handle != IntPtr.Zero)
        {
            handles.Add(_toolbarWindow.Handle);
        }

        foreach (var pinnedLensHost in _pinnedLensHosts)
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
        if (_toolbarWindow is { IsVisible: true } && _toolbarWindow.Handle != IntPtr.Zero)
        {
            handles.Add(_toolbarWindow.Handle);
        }

        foreach (var pinnedLensHost in _pinnedLensHosts)
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
        if (_magnifierRenderingSubscribed)
        {
            return;
        }

        _hasLastMagnifierCursor = false;
        CompositionTarget.Rendering += OnMagnifierRendering;
        _magnifierRenderingSubscribed = true;
    }

    private void UnsubscribeMagnifierRendering()
    {
        if (!_magnifierRenderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= OnMagnifierRendering;
        _magnifierRenderingSubscribed = false;
        _hasLastMagnifierCursor = false;
    }

    private void OnMagnifierRendering(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (!Settings.MagnifierEnabled || IsVisualBoardMode(_mode) || !TryGetCursor(out var cursor))
        {
            if (IsVisualBoardMode(_mode))
            {
                CloseMagnifierHost();
            }

            return;
        }

        // Follow the raw cursor at display rate; only move when it actually moved.
        // Follow cursor movement at display rate. Source refresh is performed every
        // frame below because dynamic content may not repaint while the lens is still.
        var cursorMoved = !_hasLastMagnifierCursor
            || cursor.DistanceTo(_lastMagnifierCursor) >= 0.5;
        if (cursorMoved)
        {
            var previous = _hasLastMagnifierCursor ? _lastMagnifierCursor : cursor;
            _lastMagnifierCursor = cursor;
            _hasLastMagnifierCursor = true;
            _spotlightCursor = cursor;
            _hasSpotlightCursor = true;
            _overlayManager?.InvalidateForCursor(cursor, previous);
        }

        // Refresh the magnified source even while the cursor is stationary so
        // video and other dynamic content continue to animate inside the lens.
        UpdateMagnifierHost();
    }

    private void CloseMagnifierHost()
    {
        _magnifierHost?.Dispose();
        _magnifierHost = null;
    }

    private void OpenPinnedLens(ScreenRect sourceRect)
    {
        var host = new PinnedLensHostWindow(sourceRect, Settings, ClosePinnedLenses, CapturePinnedLensFreezeFrameAsync);
        if (!host.IsAvailable)
        {
            host.Dispose();
            _trayIcon?.ShowMessage("Pinned lens failed", "Could not create the pinned lens window.");
            return;
        }

        _pinnedLensHosts.Add(host);
        host.Closed += OnPinnedLensClosed;
        host.FreezeStateChanged += OnPinnedLensFreezeStateChanged;
        host.Show();
        UpdatePinnedLensHost();
        UpdatePinnedLensRefreshTimer();
        _overlayManager?.ReassertTopmost();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> CapturePinnedLensFreezeFrameAsync(PinnedLensHostWindow target, Func<bool> capture)
    {
        if (_disposed || !_pinnedLensHosts.Contains(target))
        {
            return false;
        }

        var hiddenHosts = new List<PinnedLensHostWindow>();
        foreach (var host in _pinnedLensHosts.ToArray())
        {
            if (host.HideForDesktopCapture())
            {
                hiddenHosts.Add(host);
            }
        }

        await WaitForScreenRefreshAsync();

        try
        {
            return !_disposed && _pinnedLensHosts.Contains(target) && capture();
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not freeze the pinned lens frame.", ex);
            return false;
        }
        finally
        {
            foreach (var host in hiddenHosts)
            {
                if (_pinnedLensHosts.Contains(host))
                {
                    host.RestoreAfterDesktopCapture();
                }
            }

            _overlayManager?.ReassertTopmost();
        }
    }

    // Boards present a clean canvas, so pinned lenses are hidden for the board's
    // duration (they would otherwise float over it and get baked into a screen-board
    // capture). Reuses the same hide/restore path as the freeze-frame desktop capture.
    private void HidePinnedLensesForBoard()
    {
        foreach (var host in _pinnedLensHosts)
        {
            host.HideForDesktopCapture();
        }
    }

    private void RestorePinnedLensesAfterBoard()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var host in _pinnedLensHosts)
        {
            host.RestoreAfterDesktopCapture();
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
        foreach (var host in _pinnedLensHosts.ToArray())
        {
            host.ReconcileToWorkingArea();
        }

        _timerController?.ReconcileToWorkingArea();
    }

    public void ClosePinnedLenses()
    {
        _pinnedLensRefreshTimer.Stop();
        if (_pinnedLensHosts.Count == 0)
        {
            return;
        }

        foreach (var host in _pinnedLensHosts.ToArray())
        {
            RemovePinnedLensHost(host);
        }

        if (!_disposed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPinnedLensClosed(object? sender, EventArgs e)
    {
        if (sender is PinnedLensHostWindow host)
        {
            RemovePinnedLensHost(host);
            if (!_disposed)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnPinnedLensFreezeStateChanged(object? sender, EventArgs e)
    {
        UpdatePinnedLensRefreshTimer();
        if (!_disposed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPinnedLensRefreshTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        UpdatePinnedLensHost();
    }

    private void UpdatePinnedLensHost()
    {
        if (_pinnedLensHosts.Count == 0)
        {
            _pinnedLensRefreshTimer.Stop();
            return;
        }

        if (!_pinnedLensHosts.Any(host => !host.IsFrozen))
        {
            _pinnedLensRefreshTimer.Stop();
            return;
        }

        var excludedWindows = GetPinnedLensExcludedWindows();
        foreach (var host in _pinnedLensHosts.ToArray())
        {
            if (host.IsFrozen)
            {
                continue;
            }

            host.UpdateLens(excludedWindows);
        }
    }

    private void RemovePinnedLensHost(PinnedLensHostWindow host)
    {
        if (!_pinnedLensHosts.Remove(host))
        {
            return;
        }

        host.Closed -= OnPinnedLensClosed;
        host.FreezeStateChanged -= OnPinnedLensFreezeStateChanged;
        host.Dispose();
        UpdatePinnedLensRefreshTimer();
    }

    private void UpdatePinnedLensRefreshInterval()
    {
        var fps = Math.Clamp(Settings.PinnedLensRefreshFps, 10, 60);
        _pinnedLensRefreshTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
    }

    private void UpdatePinnedLensRefreshTimer()
    {
        if (_disposed || !_pinnedLensHosts.Any(host => !host.IsFrozen))
        {
            _pinnedLensRefreshTimer.Stop();
            return;
        }

        _pinnedLensRefreshTimer.Start();
    }

    private void RestoreToolbarAfterPinnedLensSelection()
    {
        if (!_restoreToolbarAfterPinnedLensSelection)
        {
            return;
        }

        _restoreToolbarAfterPinnedLensSelection = false;
        if (!_disposed)
        {
            ShowToolbar();
        }
    }

    private void RestoreToolbarAfterRegionMaskSelection()
    {
        if (!_restoreToolbarAfterRegionMaskSelection)
        {
            return;
        }

        _restoreToolbarAfterRegionMaskSelection = false;
        if (!_disposed)
        {
            ShowToolbar();
        }
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
        var registrations = new List<HotKeyRegistration>();
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleLaserActivation, ToggleLaserActivationMode);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleAnnotate, ToggleAnnotateMode);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleSpotlight, ToggleSpotlight);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleMagnifier, ToggleMagnifierMode);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.TogglePinnedLens, TogglePinnedLens);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleRegionMask, ToggleRegionMask);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ClearRegionMasks, ClearRegionMasks);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleFadingAnnotations, ToggleFadingAnnotations);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleTimer, NewTimer);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleToolbar, ToggleToolbar);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.TakeScreenshot, TakeScreenshot);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleScreenBoard, ToggleScreenBoard);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleBlackScreen, ToggleBlackScreen);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleWhiteScreen, ToggleWhiteScreen);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ExitApp, Exit);

        if (ShouldRegisterExitVisualHotKey(_mode, Settings.MagnifierEnabled, _spotlightEnabled))
        {
            registrations.Add(new HotKeyRegistration(ExitVisualShortcut, ExitVisualEffects));
        }

        _hotKeyManager ??= new HotKeyManager();
        _hotKeyManager.SetRegistrations(registrations);

        if (_hotKeyManager.RegistrationErrors.Count > 0)
        {
            _trayIcon?.ShowMessage("Global hotkey was not registered", string.Join(Environment.NewLine, _hotKeyManager.RegistrationErrors));
        }
    }

    private static void AddHotKeyIfEnabled(List<HotKeyRegistration> registrations, string shortcutText, Action action)
    {
        if (!ShortcutSettings.IsShortcutDisabled(shortcutText))
        {
            registrations.Add(new HotKeyRegistration(shortcutText, action));
        }
    }

    private static bool HaveSameGlobalHotKeys(
        ShortcutSettings left,
        ShortcutSettings right)
    {
        return string.Equals(left.ToggleLaserActivation, right.ToggleLaserActivation, StringComparison.Ordinal)
            && string.Equals(left.ToggleAnnotate, right.ToggleAnnotate, StringComparison.Ordinal)
            && string.Equals(left.ToggleSpotlight, right.ToggleSpotlight, StringComparison.Ordinal)
            && string.Equals(left.ToggleMagnifier, right.ToggleMagnifier, StringComparison.Ordinal)
            && string.Equals(left.TogglePinnedLens, right.TogglePinnedLens, StringComparison.Ordinal)
            && string.Equals(left.ToggleRegionMask, right.ToggleRegionMask, StringComparison.Ordinal)
            && string.Equals(left.ClearRegionMasks, right.ClearRegionMasks, StringComparison.Ordinal)
            && string.Equals(left.ToggleFadingAnnotations, right.ToggleFadingAnnotations, StringComparison.Ordinal)
            && string.Equals(left.ToggleTimer, right.ToggleTimer, StringComparison.Ordinal)
            && string.Equals(left.ToggleToolbar, right.ToggleToolbar, StringComparison.Ordinal)
            && string.Equals(left.TakeScreenshot, right.TakeScreenshot, StringComparison.Ordinal)
            && string.Equals(left.ToggleScreenBoard, right.ToggleScreenBoard, StringComparison.Ordinal)
            && string.Equals(left.ToggleBlackScreen, right.ToggleBlackScreen, StringComparison.Ordinal)
            && string.Equals(left.ToggleWhiteScreen, right.ToggleWhiteScreen, StringComparison.Ordinal)
            && string.Equals(left.ExitApp, right.ExitApp, StringComparison.Ordinal);
    }

    private void ReassertFloatingChromeTopmost()
    {
        if (_toolbarWindow is { IsVisible: true })
        {
            _toolbarWindow.ReassertTopmost();
        }

        _timerController?.ReassertTopmost();

        foreach (var host in _pinnedLensHosts)
        {
            host.ReassertContextMenuTopmost();
        }

        if (_regionMaskContextMenu is { Visible: true } menu)
        {
            ReassertRegionMaskContextMenuTopmost(menu);
        }
    }

    private void ReassertPinnedLensTopmost()
    {
        foreach (var host in _pinnedLensHosts)
        {
            host.ReassertTopmost();
        }
    }

    private static bool ShouldRegisterExitVisualHotKey(
        InteractionMode mode,
        bool magnifierEnabled,
        bool spotlightEnabled)
    {
        return !IsAnnotationMode(mode) && (magnifierEnabled || spotlightEnabled);
    }

    private void SetLaserVisualActive(bool active)
    {
        if (_laserVisuallyActive == active)
        {
            return;
        }

        _laserVisuallyActive = active;
        StateChanged?.Invoke(this, EventArgs.Empty);
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
        if (ActivationMode == LaserActivationMode.Always)
        {
            _laserHoldShortcut = default;
            return;
        }

        if (AppSettings.IsLaserHoldShortcutDisabled(Settings.LaserHoldShortcut)
            || !Shortcut.TryParse(Settings.LaserHoldShortcut, out _laserHoldShortcut))
        {
            Settings.LaserHoldShortcut = "XButton2";
            Shortcut.TryParse(Settings.LaserHoldShortcut, out _laserHoldShortcut);
        }
    }

    private bool TrySelectTool(Key key, ModifierKeys modifiers)
    {
        var shortcuts = Settings.Shortcuts;
        if (Matches(key, modifiers, shortcuts.ToolArrow))
        {
            SetAnnotationTool(AnnotationTool.Arrow);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.ToolRectangle))
        {
            SetAnnotationTool(AnnotationTool.Rectangle);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.ToolEllipse))
        {
            SetAnnotationTool(AnnotationTool.Ellipse);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.ToolLine))
        {
            SetAnnotationTool(AnnotationTool.Line);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.ToolPencil))
        {
            SetAnnotationTool(AnnotationTool.Pencil);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.ToolHighlighter))
        {
            SetAnnotationTool(AnnotationTool.Highlighter);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.ToolText))
        {
            SetAnnotationTool(AnnotationTool.Text);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.ToolMove))
        {
            SetAnnotationTool(AnnotationTool.Move);
            return true;
        }

        return false;
    }

    private bool TrySelectColor(Key key, ModifierKeys modifiers)
    {
        var shortcuts = Settings.Shortcuts;
        if (Matches(key, modifiers, shortcuts.Color1))
        {
            SetAnnotationPresetColor(0);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.Color2))
        {
            SetAnnotationPresetColor(1);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.Color3))
        {
            SetAnnotationPresetColor(2);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.Color4))
        {
            SetAnnotationPresetColor(3);
            return true;
        }

        if (Matches(key, modifiers, shortcuts.Color5))
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

    private static bool IsAnnotationMode(InteractionMode mode)
    {
        return mode is InteractionMode.Annotate or InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
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

    // Retain exactly the trail length: a point that ages out of the window is
    // trimmed at the same instant, so it can never be revealed again as a phantom.
    private int RetainedTrailLengthMs => Settings.TrailLengthMs;

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

    private enum RegionMaskResizeHandle
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}
