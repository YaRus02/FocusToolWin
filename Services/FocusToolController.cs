using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Media;
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
    private const double RegionMaskMinSizePixels = 8;
    private const double RegionMaskResizeHitRadiusPixels = 12;
    private const double CursorPulseDurationMs = 360;
    private const int MaximumCursorClickPulses = 4;
    private const string ExitVisualShortcut = "Esc";

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SettingsStore _settingsStore = new();
    private readonly ScreenshotService _screenshotService = new();
    private readonly TrailModel _trail = new();
    private readonly AnnotationDocument _annotations;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _settingsSaveTimer;
    private readonly DispatcherTimer _pinnedLensRefreshTimer;
    private readonly HashSet<string> _pushToAnnotatePolledShortcutDown = new(StringComparer.Ordinal);

    private OverlayManager? _overlayManager;
    private MagnifierHostWindow? _magnifierHost;
    private readonly List<PinnedLensHostWindow> _pinnedLensHosts = [];
    private readonly List<RegionMask> _regionMasks = [];
    private readonly List<ScreenRect> _spotlightRegions = [];
    private TimerController? _timerController;
    private TrayIconController? _trayIcon;
    private HotKeyManager? _hotKeyManager;
    private MouseHook? _mouseHook;
    private SettingsWindow? _settingsWindow;
    private OverlayToolbarWindow? _toolbarWindow;
    private ScreenBoardFrame? _screenBoardFrame;
    private Shortcut _laserHoldShortcut;
    private Shortcut _cursorHighlightHoldShortcut;
    private Shortcut _pushToAnnotateShortcut;
    private ScreenPoint _lastCursor;
    private ScreenPoint _cursorHighlightPoint;
    private ScreenPoint _lastSelectionMovePoint;
    private ScreenPoint _lastRegionMaskMovePoint;
    private ScreenPoint _lastScreenshotRegionMovePoint;
    private ScreenPoint _lastSpotlightRegionMovePoint;
    private ScreenPoint _lastTextClickPoint;
    private ScreenPoint _lastObjectClickPoint;
    private ScreenPoint _pendingTextEditMovePoint;
    private readonly List<CursorClickPulse> _cursorClickPulses = [];
    private readonly RectSelectionSession _rectSelection = new();
    private bool _hasLastCursor;
    private bool _hasCursorHighlightPoint;
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
    private RegionMask? _movingRegionMask;
    private RegionMask? _resizingRegionMask;
    private ScreenPoint _regionMaskResizeAnchor;
    private bool _movingScreenshotRegion;
    private RectResizeHandle _screenshotRegionResizeHandle;
    private ScreenPoint _screenshotRegionResizeAnchor;
    private int _selectedSpotlightRegionIndex = -1;
    private int _selectedRegionMaskId = -1;
    private bool _movingSpotlightRegion;
    private RectResizeHandle _spotlightRegionResizeHandle;
    private ScreenPoint _spotlightRegionResizeAnchor;
    private Forms.ContextMenuStrip? _regionMaskContextMenu;
    private readonly List<Forms.Timer> _toolStripTopmostTimers = [];
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
    private AnnotationTool _lastStepTool = AnnotationTool.StepOval;

    public event EventHandler? StateChanged;

    public AppSettings Settings { get; private set; }
    public string SettingsFilePath => _settingsStore.SettingsFilePath;
    public InteractionMode Mode => _mode;
    public LaserActivationMode ActivationMode => Settings.GetLaserActivationMode();
    public AnnotationTool CurrentTool => Settings.GetAnnotationTool();
    public AnnotationDocument Annotations => _annotations;
    public bool LaserVisuallyActive => _laserVisuallyActive;
    public bool CursorHighlightEnabled => Settings.CursorHighlightEnabled;
    public bool ClickPulseEnabled => Settings.ClickPulseEnabled;
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
    public bool RegionSpotlightActive => _spotlightRegions.Count > 0;
    public int RegionSpotlightCount => _spotlightRegions.Count;
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
        Settings = _settingsStore.Load();
        CacheParsedSettings();
        if (IsStepTool(CurrentTool))
        {
            _lastStepTool = CurrentTool;
        }

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
        _overlayManager = new OverlayManager(_trail, _annotations, () => Settings, () => _mode, NowMs, GetSpotlightPoint, GetCursorHighlightFrame, () => _screenBoardFrame, GetRectOverlayVisual, () => _regionMasks, () => _selectedRegionMaskId, () => _spotlightRegions, () => _selectedSpotlightRegionIndex, this, ReassertPinnedLensTopmost, ReassertFloatingChromeTopmost);
        _trayIcon = new TrayIconController(this);
        _timerController = new TimerController(NowMs, () => Settings.Timer, ApplyTimerDefaults, AddTimerLabelToHistory, OnTimerActiveCountChanged);
        _mouseHook = new MouseHook();
        _mouseHook.Clicked += OnMouseHookClicked;
        RegisterHotKeys();
        UpdateMouseHook();
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
        var hadRegions = _spotlightRegions.Count > 0;
        var wasSelecting = _mode == InteractionMode.RegionSpotlightSelect;
        if (!hadRegions && !wasSelecting)
        {
            return;
        }

        _spotlightRegions.Clear();
        ResetSpotlightRegionEditState();
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
        var hadMasks = _regionMasks.Count > 0;
        var wasSelecting = _mode == InteractionMode.RegionMaskSelect;
        if (!hadMasks && !wasSelecting)
        {
            return;
        }

        _regionMasks.Clear();
        _rectSelection.Cancel();
        _movingRegionMask = null;
        _resizingRegionMask = null;
        _selectedRegionMaskId = -1;
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

    private bool TryGetSelectedRegionMask(out RegionMask mask)
    {
        mask = _regionMasks.FirstOrDefault(item => item.Id == _selectedRegionMaskId)!;
        if (mask is not null)
        {
            return true;
        }

        _selectedRegionMaskId = -1;
        return false;
    }

    private bool TryHitRegionMaskResizeHandle(ScreenPoint point, out RegionMask mask, out RectResizeHandle handle)
    {
        if (TryGetSelectedRegionMask(out var selectedMask))
        {
            if (TryHitRectResizeHandle(selectedMask.Rect, point, out handle))
            {
                mask = selectedMask;
                return true;
            }

            mask = null!;
            handle = RectResizeHandle.None;
            return false;
        }

        for (var i = _regionMasks.Count - 1; i >= 0; i--)
        {
            if (TryHitRectResizeHandle(_regionMasks[i].Rect, point, out handle))
            {
                mask = _regionMasks[i];
                return true;
            }
        }

        mask = null!;
        handle = RectResizeHandle.None;
        return false;
    }

    private bool TryHitSpotlightRegionResizeHandle(ScreenPoint point, out int index, out RectResizeHandle handle)
    {
        for (var i = _spotlightRegions.Count - 1; i >= 0; i--)
        {
            if (TryHitRectResizeHandle(_spotlightRegions[i], point, out handle))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        handle = RectResizeHandle.None;
        return false;
    }

    private bool TryHitSpotlightRegion(ScreenPoint point, out int index)
    {
        for (var i = _spotlightRegions.Count - 1; i >= 0; i--)
        {
            if (_spotlightRegions[i].Contains(point))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private bool IsValidSpotlightRegionIndex(int index)
    {
        return index >= 0 && index < _spotlightRegions.Count;
    }

    private static bool TryHitRectResizeHandle(ScreenRect rect, ScreenPoint point, out RectResizeHandle handle)
    {
        var hitRadiusSquared = RegionMaskResizeHitRadiusPixels * RegionMaskResizeHitRadiusPixels;
        if (DistanceSquared(point, new ScreenPoint(rect.Left, rect.Top)) <= hitRadiusSquared)
        {
            handle = RectResizeHandle.TopLeft;
            return true;
        }

        if (DistanceSquared(point, new ScreenPoint(rect.Right, rect.Top)) <= hitRadiusSquared)
        {
            handle = RectResizeHandle.TopRight;
            return true;
        }

        if (DistanceSquared(point, new ScreenPoint(rect.Left, rect.Bottom)) <= hitRadiusSquared)
        {
            handle = RectResizeHandle.BottomLeft;
            return true;
        }

        if (DistanceSquared(point, new ScreenPoint(rect.Right, rect.Bottom)) <= hitRadiusSquared)
        {
            handle = RectResizeHandle.BottomRight;
            return true;
        }

        handle = RectResizeHandle.None;
        return false;
    }

    private static double DistanceSquared(ScreenPoint first, ScreenPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private static ScreenPoint GetRectResizeAnchor(ScreenRect rect, RectResizeHandle handle)
    {
        return handle switch
        {
            RectResizeHandle.TopLeft => new ScreenPoint(rect.Right, rect.Bottom),
            RectResizeHandle.TopRight => new ScreenPoint(rect.Left, rect.Bottom),
            RectResizeHandle.BottomLeft => new ScreenPoint(rect.Right, rect.Top),
            RectResizeHandle.BottomRight => new ScreenPoint(rect.Left, rect.Top),
            _ => new ScreenPoint(rect.Left, rect.Top)
        };
    }

    private static ScreenRect CreateResizeRect(ScreenPoint anchor, ScreenPoint point)
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
        if (_selectedRegionMaskId == maskId)
        {
            _selectedRegionMaskId = -1;
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
        if (_selectedRegionMaskId < 0)
        {
            return;
        }

        DeleteRegionMask(_selectedRegionMaskId, exitMaskMode: false);
    }

    private void SetRegionMaskStyle(int maskId, RegionMaskStyle style)
    {
        if (!string.Equals(Settings.RegionMaskStyle, style.ToString(), StringComparison.Ordinal))
        {
            var updated = Settings.Clone();
            updated.RegionMaskStyle = style.ToString();
            ApplySettings(updated);
        }

        var mask = _regionMasks.FirstOrDefault(item => item.Id == maskId);
        if (mask is null || mask.Style == style)
        {
            return;
        }

        mask.SetStyle(style);
        _overlayManager?.Invalidate();
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
        ReassertToolStripDropDownTopmostRepeated(menu);
    }

    private Forms.ContextMenuStrip GetRegionMaskContextMenu()
    {
        if (_regionMaskContextMenu is not null)
        {
            return _regionMaskContextMenu;
        }

        var menu = new Forms.ContextMenuStrip();
        var styleMenu = new Forms.ToolStripMenuItem("Style");
        AddRegionMaskStyleItem(styleMenu, "Solid", RegionMaskStyle.Solid);
        AddRegionMaskStyleItem(styleMenu, "Stripes", RegionMaskStyle.Stripes);
        AddRegionMaskStyleItem(styleMenu, "HIDE label", RegionMaskStyle.Label);
        AddRegionMaskStyleItem(styleMenu, "Stripes + HIDE", RegionMaskStyle.StripesWithLabel);
        styleMenu.DropDownOpened += (_, _) =>
        {
            ReassertToolStripDropDownTopmostRepeated(styleMenu.DropDown);
        };
        menu.Items.Add(styleMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var deleteItem = menu.Items.Add("Delete mask");
        deleteItem.Click += (_, _) => DeleteRegionMaskFromContextMenu();
        menu.Opening += (_, _) => UpdateRegionMaskContextMenuState(menu);
        menu.Opened += (_, _) => ReassertToolStripDropDownTopmostRepeated(menu);
        menu.Closed += (_, _) => _regionMaskContextMenuActionTaken = false;

        _regionMaskContextMenu = menu;
        return menu;
    }

    private void AddRegionMaskStyleItem(Forms.ToolStripMenuItem parent, string text, RegionMaskStyle style)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            CheckOnClick = false,
            Tag = style
        };
        item.Click += (_, _) => SetRegionMaskStyleFromContextMenu(style);
        parent.DropDownItems.Add(item);
    }

    private void UpdateRegionMaskContextMenuState(Forms.ContextMenuStrip menu)
    {
        var currentStyle = _regionMasks.FirstOrDefault(mask => mask.Id == _regionMaskContextMenuMaskId)?.Style
            ?? RegionMaskStyle.StripesWithLabel;
        foreach (Forms.ToolStripItem item in menu.Items)
        {
            if (item is Forms.ToolStripMenuItem { Text: "Style" } styleMenu)
            {
                foreach (Forms.ToolStripItem child in styleMenu.DropDownItems)
                {
                    if (child is Forms.ToolStripMenuItem styleItem && styleItem.Tag is RegionMaskStyle style)
                    {
                        styleItem.Checked = style == currentStyle;
                    }
                }
            }
        }
    }

    private void SetRegionMaskStyleFromContextMenu(RegionMaskStyle style)
    {
        if (_regionMaskContextMenuActionTaken)
        {
            return;
        }

        _regionMaskContextMenuActionTaken = true;
        SetRegionMaskStyle(_regionMaskContextMenuMaskId, style);
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

    private void ReassertRegionMaskContextMenuTopmost(Forms.ContextMenuStrip menu)
    {
        ReassertToolStripDropDownTopmost(menu);
    }

    private void ReassertToolStripDropDownTopmostRepeated(Forms.ToolStripDropDown menu)
    {
        ReassertToolStripDropDownTopmost(menu);
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            () => ReassertToolStripDropDownTopmost(menu),
            DispatcherPriority.Send);
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            () => ReassertToolStripDropDownTopmost(menu),
            DispatcherPriority.ContextIdle);

        _toolStripTopmostTimers.RemoveAll(timer =>
        {
            if (timer.Enabled)
            {
                return false;
            }

            timer.Dispose();
            return true;
        });

        var timer = new Forms.Timer
        {
            Interval = 16
        };
        timer.Tick += (_, _) =>
        {
            if (menu.IsDisposed || !menu.Visible)
            {
                timer.Stop();
                _toolStripTopmostTimers.Remove(timer);
                timer.Dispose();
                return;
            }

            ReassertToolStripDropDownTopmost(menu);
        };
        _toolStripTopmostTimers.Add(timer);
        timer.Start();
    }

    private static void ReassertToolStripDropDownTopmost(Forms.ToolStripDropDown menu)
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
        _movingScreenshotRegion = false;
        _screenshotRegionResizeHandle = RectResizeHandle.None;
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

        _selectedSpotlightRegionIndex = _spotlightRegions.Count - 1;
        _movingSpotlightRegion = false;
        _spotlightRegionResizeHandle = RectResizeHandle.None;
        BeginRectSelectionMode(InteractionMode.RegionSpotlightSelect);
        if (_spotlightRegions.Count > 0)
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
        _movingRegionMask = null;
        _resizingRegionMask = null;
        _movingScreenshotRegion = false;
        _screenshotRegionResizeHandle = RectResizeHandle.None;
        _movingSpotlightRegion = false;
        _spotlightRegionResizeHandle = RectResizeHandle.None;
        _restoreToolbarAfterRectSelection = ToolbarVisible;
        if (_restoreToolbarAfterRectSelection)
        {
            _toolbarWindow?.Hide();
        }

        SetInteractionMode(mode);
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

    private async Task TakeRegionScreenshotAsync(ScreenRect sourceRect, bool restoreToolbar)
    {
        if (_disposed)
        {
            return;
        }

        if (_captureInProgress)
        {
            if (restoreToolbar && !_disposed)
            {
                ShowToolbar();
            }

            return;
        }

        _captureInProgress = true;
        var magnifierWasActive = Settings.MagnifierEnabled;

        // The toolbar is already hidden by BeginRectSelectionMode and is restored
        // only after capture. Keep overlays visible so annotations and masks are
        // captured exactly like the full-monitor screenshot path.
        CloseMagnifierHost();
        await WaitForScreenRefreshAsync();

        try
        {
            await _screenshotService.CaptureRegionAsync(sourceRect, copyToClipboard: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not capture region screenshot.", ex);
            _trayIcon?.ShowMessage("Region screenshot failed", ex.Message);
        }
        finally
        {
            if (restoreToolbar && !_disposed)
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
            _movingRegionMask = null;
            _resizingRegionMask = null;
            _selectedRegionMaskId = -1;
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
        var globalHotKeysChanged = !HaveSameGlobalHotKeys(Settings.Shortcuts, settings.Shortcuts);
        var exitVisualHotKeyWasNeeded = ShouldRegisterExitVisualHotKey(
            _mode,
            Settings.MagnifierEnabled,
            HasExitVisualSpotlightEffect());
        var magnifierVisualChanged = Math.Abs(Settings.MagnifierRadius - settings.MagnifierRadius) > 0.001
            || Math.Abs(Settings.MagnifierZoom - settings.MagnifierZoom) > 0.001;
        Settings.CopyFrom(settings);
        CacheParsedSettings();
        UpdatePinnedLensRefreshInterval();
        _spotlightEnabled = Settings.SpotlightEnabled;
        UpdateMouseHook();

        if (Settings.CursorHighlightEnabled)
        {
            _timer.Interval = ActiveInterval;
            UpdateCursorHighlight(force: true);
        }
        else if (cursorHighlightWasEnabled || _hasCursorHighlightPoint)
        {
            ClearCursorHighlightPoint();
        }

        if (!Settings.ClickPulseEnabled && (clickPulseWasEnabled || _cursorClickPulses.Count > 0))
        {
            ClearCursorClickPulses();
        }

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
            HasExitVisualSpotlightEffect());
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
                if (TryHitRectResizeHandle(pending, point, out var handle))
                {
                    _screenshotRegionResizeHandle = handle;
                    _screenshotRegionResizeAnchor = GetRectResizeAnchor(pending, handle);
                    _movingScreenshotRegion = false;
                    _rectSelection.Cancel();
                    return;
                }

                if (pending.Contains(point))
                {
                    _movingScreenshotRegion = true;
                    _screenshotRegionResizeHandle = RectResizeHandle.None;
                    _lastScreenshotRegionMovePoint = point;
                    _rectSelection.Cancel();
                    return;
                }

                _pendingScreenshotRegion = null;
                _movingScreenshotRegion = false;
                _screenshotRegionResizeHandle = RectResizeHandle.None;
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
                _selectedSpotlightRegionIndex = resizeIndex;
                _spotlightRegionResizeHandle = resizeHandle;
                _spotlightRegionResizeAnchor = GetRectResizeAnchor(_spotlightRegions[resizeIndex], resizeHandle);
                _movingSpotlightRegion = false;
                _rectSelection.Cancel();
                _overlayManager?.Invalidate();
                return;
            }

            if (TryHitSpotlightRegion(point, out var moveIndex))
            {
                _selectedSpotlightRegionIndex = moveIndex;
                _movingSpotlightRegion = true;
                _spotlightRegionResizeHandle = RectResizeHandle.None;
                _lastSpotlightRegionMovePoint = point;
                _rectSelection.Cancel();
                _overlayManager?.Invalidate();
                return;
            }

            _selectedSpotlightRegionIndex = -1;
            _movingSpotlightRegion = false;
            _spotlightRegionResizeHandle = RectResizeHandle.None;
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
                    _selectedRegionMaskId = mask.Id;
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
                _resizingRegionMask = resizeMask;
                _selectedRegionMaskId = resizeMask.Id;
                _regionMaskResizeAnchor = GetRectResizeAnchor(resizeMask.Rect, resizeHandle);
                _movingRegionMask = null;
                _rectSelection.Cancel();
                _overlayManager?.Invalidate();
                return;
            }

            if (TryHitRegionMask(point, out var existingMask))
            {
                _movingRegionMask = existingMask;
                _selectedRegionMaskId = existingMask.Id;
                _resizingRegionMask = null;
                _lastRegionMaskMovePoint = point;
                _rectSelection.Cancel();
                _overlayManager?.Invalidate();
                return;
            }

            _selectedRegionMaskId = -1;
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
            if (_pendingScreenshotRegion is { } pending && _screenshotRegionResizeHandle != RectResizeHandle.None)
            {
                _pendingScreenshotRegion = CreateResizeRect(_screenshotRegionResizeAnchor, point);
                _overlayManager?.Invalidate();
                return;
            }

            if (_pendingScreenshotRegion is { } movingPending && _movingScreenshotRegion)
            {
                _pendingScreenshotRegion = movingPending.Offset(point.X - _lastScreenshotRegionMovePoint.X, point.Y - _lastScreenshotRegionMovePoint.Y);
                _lastScreenshotRegionMovePoint = point;
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
            if (IsValidSpotlightRegionIndex(_selectedSpotlightRegionIndex)
                && _spotlightRegionResizeHandle != RectResizeHandle.None)
            {
                _spotlightRegions[_selectedSpotlightRegionIndex] = CreateResizeRect(_spotlightRegionResizeAnchor, point);
                _overlayManager?.Invalidate();
                return;
            }

            if (IsValidSpotlightRegionIndex(_selectedSpotlightRegionIndex) && _movingSpotlightRegion)
            {
                _spotlightRegions[_selectedSpotlightRegionIndex] = _spotlightRegions[_selectedSpotlightRegionIndex].Offset(
                    point.X - _lastSpotlightRegionMovePoint.X,
                    point.Y - _lastSpotlightRegionMovePoint.Y);
                _lastSpotlightRegionMovePoint = point;
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
            if (_resizingRegionMask is not null)
            {
                _resizingRegionMask.SetRect(CreateResizeRect(_regionMaskResizeAnchor, point));
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
                OpenPinnedLens(completedSourceRect);
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

            if (_screenshotRegionResizeHandle != RectResizeHandle.None)
            {
                _screenshotRegionResizeHandle = RectResizeHandle.None;
                _overlayManager?.Invalidate();
                return;
            }

            if (_movingScreenshotRegion)
            {
                _movingScreenshotRegion = false;
                _overlayManager?.Invalidate();
                return;
            }

            var sourceRect = _rectSelection.Complete(point);
            if (sourceRect is null)
            {
                return;
            }

            var completedSourceRect = sourceRect.Value;
            if (completedSourceRect.Width >= RegionMaskMinSizePixels && completedSourceRect.Height >= RegionMaskMinSizePixels)
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

            if (_spotlightRegionResizeHandle != RectResizeHandle.None)
            {
                _spotlightRegionResizeHandle = RectResizeHandle.None;
                _overlayManager?.Invalidate();
                return;
            }

            if (_movingSpotlightRegion)
            {
                _movingSpotlightRegion = false;
                _overlayManager?.Invalidate();
                return;
            }

            var sourceRect = _rectSelection.Complete(point);
            if (sourceRect is null)
            {
                return;
            }

            var completedSourceRect = sourceRect.Value;
            if (completedSourceRect.Width >= RegionMaskMinSizePixels && completedSourceRect.Height >= RegionMaskMinSizePixels)
            {
                var hadSpotlightRegions = _spotlightRegions.Count > 0;
                _spotlightRegions.Add(completedSourceRect);
                _selectedSpotlightRegionIndex = _spotlightRegions.Count - 1;
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
            if (completedMaskRect.Width >= RegionMaskMinSizePixels && completedMaskRect.Height >= RegionMaskMinSizePixels)
            {
                var mask = new RegionMask(_nextRegionMaskId++, completedMaskRect, Settings);
                _regionMasks.Add(mask);
                _selectedRegionMaskId = mask.Id;
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

        if (!TryGetSelectedRegionMask(out var mask))
        {
            if (!TryHitRegionMask(point, out mask))
            {
                return false;
            }

            _selectedRegionMaskId = mask.Id;
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
            _movingScreenshotRegion = false;
            _screenshotRegionResizeHandle = RectResizeHandle.None;
            if (_rectSelection.IsActive)
            {
                _rectSelection.Cancel();
            }

            _overlayManager?.Invalidate();
            return;
        }

        if (_mode == InteractionMode.RegionSpotlightSelect)
        {
            _movingSpotlightRegion = false;
            _spotlightRegionResizeHandle = RectResizeHandle.None;
            if (_rectSelection.IsActive)
            {
                _rectSelection.Cancel();
            }

            _overlayManager?.Invalidate();
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
        if (!IsValidSpotlightRegionIndex(_selectedSpotlightRegionIndex))
        {
            return;
        }

        _spotlightRegions.RemoveAt(_selectedSpotlightRegionIndex);
        if (_spotlightRegions.Count == 0)
        {
            ResetSpotlightRegionEditState();
            RegisterHotKeys();
        }
        else
        {
            _selectedSpotlightRegionIndex = Math.Min(_selectedSpotlightRegionIndex, _spotlightRegions.Count - 1);
            _movingSpotlightRegion = false;
            _spotlightRegionResizeHandle = RectResizeHandle.None;
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
        if (!IsValidSpotlightRegionIndex(_selectedSpotlightRegionIndex)
            || !TryGetNudgeDelta(key, modifiers, out var dx, out var dy))
        {
            return false;
        }

        _spotlightRegions[_selectedSpotlightRegionIndex] = _spotlightRegions[_selectedSpotlightRegionIndex].Offset(dx, dy);
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
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Tick -= OnSettingsSaveTick;
        if (_mouseHook is not null)
        {
            _mouseHook.Clicked -= OnMouseHookClicked;
            _mouseHook.Dispose();
        }

        _pinnedLensRefreshTimer.Stop();
        _pinnedLensRefreshTimer.Tick -= OnPinnedLensRefreshTick;
        UnsubscribeMagnifierRendering();
        _annotations.Changed -= OnAnnotationsChanged;
        _annotations.DraftProgressed -= OnAnnotationDraftProgressed;
        _regionMaskContextMenu?.Dispose();
        _regionMaskContextMenu = null;
        foreach (var timer in _toolStripTopmostTimers)
        {
            timer.Stop();
            timer.Dispose();
        }

        _toolStripTopmostTimers.Clear();
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
        UpdatePushToAnnotate();

        var fadingAnnotationsAnimating = UpdateFadingAnnotations();
        var cursorHighlightAnimating = UpdateCursorHighlight(force: false);
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
        if (_pushToAnnotateActive)
        {
            _timer.Interval = ActiveInterval;
        }
        else if (magnifierActive && !_magnifierRenderingSubscribed)
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

    private bool UpdateCursorHighlight(bool force)
    {
        var nowMs = NowMs();
        var removedExpiredPulses = RemoveExpiredCursorClickPulses(nowMs);
        var active = IsCursorHighlightVisuallyActive();

        if (active && TryGetCursor(out var cursor))
        {
            var previous = _hasCursorHighlightPoint ? _cursorHighlightPoint : cursor;
            var moved = force
                || !_hasCursorHighlightPoint
                || cursor.DistanceTo(_cursorHighlightPoint) >= 0.5;
            _cursorHighlightPoint = cursor;
            _hasCursorHighlightPoint = true;

            if (moved)
            {
                if (force)
                {
                    _overlayManager?.Invalidate();
                }
                else
                {
                    _overlayManager?.InvalidateForCursor(cursor, previous);
                }

                _timer.Interval = ActiveInterval;
            }
            else
            {
                _timer.Interval = FadeInterval;
            }
        }
        else if (_hasCursorHighlightPoint)
        {
            var previous = _cursorHighlightPoint;
            _hasCursorHighlightPoint = false;
            _overlayManager?.InvalidateForCursor(previous, previous);
        }

        if (removedExpiredPulses || _cursorClickPulses.Count > 0)
        {
            _overlayManager?.Invalidate();
        }

        return active || _cursorClickPulses.Count > 0;
    }

    private bool RemoveExpiredCursorClickPulses(double nowMs)
    {
        var removed = false;
        for (var i = _cursorClickPulses.Count - 1; i >= 0; i--)
        {
            if (nowMs - _cursorClickPulses[i].StartedAtMs >= CursorPulseDurationMs)
            {
                _cursorClickPulses.RemoveAt(i);
                removed = true;
            }
        }

        return removed;
    }

    private CursorHighlightFrame GetCursorHighlightFrame()
    {
        if (!_hasCursorHighlightPoint && _cursorClickPulses.Count == 0)
        {
            return CursorHighlightFrame.Empty;
        }

        return new CursorHighlightFrame(
            _hasCursorHighlightPoint ? _cursorHighlightPoint : null,
            _cursorClickPulses.ToArray());
    }

    private void ClearCursorHighlightPoint()
    {
        var hadVisual = _hasCursorHighlightPoint;
        _hasCursorHighlightPoint = false;
        if (hadVisual)
        {
            _overlayManager?.Invalidate();
        }
    }

    private void ClearCursorClickPulses()
    {
        if (_cursorClickPulses.Count == 0)
        {
            return;
        }

        _cursorClickPulses.Clear();
        _overlayManager?.Invalidate();
    }

    private bool IsCursorHighlightVisuallyActive()
    {
        if (!Settings.CursorHighlightEnabled)
        {
            return false;
        }

        return Settings.GetCursorHighlightActivationMode() == LaserActivationMode.Always
            || _cursorHighlightHoldShortcut.IsPressed();
    }

    private void UpdateMouseHook()
    {
        if (_mouseHook is null)
        {
            return;
        }

        if (Settings.ClickPulseEnabled)
        {
            if (!_mouseHook.Install())
            {
                AppLog.Error("Could not install low-level mouse hook for cursor click pulse.");
            }

            return;
        }

        _mouseHook.Uninstall();
        ClearCursorClickPulses();
    }

    private void OnMouseHookClicked(object? sender, MouseHookClickEventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => OnMouseHookClicked(sender, e));
            return;
        }

        if (_disposed
            || !Settings.ClickPulseEnabled)
        {
            return;
        }

        _cursorClickPulses.Add(new CursorClickPulse(e.Point, e.Button, NowMs()));
        while (_cursorClickPulses.Count > MaximumCursorClickPulses)
        {
            _cursorClickPulses.RemoveAt(0);
        }

        _timer.Interval = FadeInterval;
        _overlayManager?.Invalidate();
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
        _movingScreenshotRegion = false;
        _screenshotRegionResizeHandle = RectResizeHandle.None;
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
        _selectedSpotlightRegionIndex = -1;
        _movingSpotlightRegion = false;
        _spotlightRegionResizeHandle = RectResizeHandle.None;
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
        var registrations = new List<HotKeyRegistration>();
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleLaserActivation, ToggleLaserActivationMode);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleAnnotate, ToggleAnnotateMode);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.PushToAnnotate, StartPushToAnnotate);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleCursorHighlight, ToggleCursorHighlight);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleSpotlight, ToggleSpotlight);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleMagnifier, ToggleMagnifierMode);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.TogglePinnedLens, TogglePinnedLens);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleRegionMask, ToggleRegionMask);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ClearRegionMasks, ClearRegionMasks);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleRegionSpotlight, ToggleRegionSpotlight);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ClearRegionSpotlights, ClearRegionSpotlights);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleFadingAnnotations, ToggleFadingAnnotations);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleTimer, NewTimer);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleToolbar, ToggleToolbar);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.TakeScreenshot, TakeScreenshot);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.TakeRegionScreenshot, TakeRegionScreenshot);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleScreenBoard, ToggleScreenBoard);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleBlackScreen, ToggleBlackScreen);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ToggleWhiteScreen, ToggleWhiteScreen);
        AddHotKeyIfEnabled(registrations, Settings.Shortcuts.ExitApp, Exit);

        if (ShouldRegisterExitVisualHotKey(_mode, Settings.MagnifierEnabled, HasExitVisualSpotlightEffect()))
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
            && string.Equals(left.PushToAnnotate, right.PushToAnnotate, StringComparison.Ordinal)
            && string.Equals(left.ToggleCursorHighlight, right.ToggleCursorHighlight, StringComparison.Ordinal)
            && string.Equals(left.ToggleSpotlight, right.ToggleSpotlight, StringComparison.Ordinal)
            && string.Equals(left.ToggleMagnifier, right.ToggleMagnifier, StringComparison.Ordinal)
            && string.Equals(left.TogglePinnedLens, right.TogglePinnedLens, StringComparison.Ordinal)
            && string.Equals(left.ToggleRegionMask, right.ToggleRegionMask, StringComparison.Ordinal)
            && string.Equals(left.ClearRegionMasks, right.ClearRegionMasks, StringComparison.Ordinal)
            && string.Equals(left.ToggleRegionSpotlight, right.ToggleRegionSpotlight, StringComparison.Ordinal)
            && string.Equals(left.ClearRegionSpotlights, right.ClearRegionSpotlights, StringComparison.Ordinal)
            && string.Equals(left.ToggleFadingAnnotations, right.ToggleFadingAnnotations, StringComparison.Ordinal)
            && string.Equals(left.ToggleTimer, right.ToggleTimer, StringComparison.Ordinal)
            && string.Equals(left.ToggleToolbar, right.ToggleToolbar, StringComparison.Ordinal)
            && string.Equals(left.TakeScreenshot, right.TakeScreenshot, StringComparison.Ordinal)
            && string.Equals(left.TakeRegionScreenshot, right.TakeRegionScreenshot, StringComparison.Ordinal)
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
        }
        else if (AppSettings.IsLaserHoldShortcutDisabled(Settings.LaserHoldShortcut)
            || !Shortcut.TryParse(Settings.LaserHoldShortcut, out _laserHoldShortcut))
        {
            Settings.LaserHoldShortcut = "XButton2";
            Shortcut.TryParse(Settings.LaserHoldShortcut, out _laserHoldShortcut);
        }

        if (Settings.GetCursorHighlightActivationMode() == LaserActivationMode.Always)
        {
            _cursorHighlightHoldShortcut = default;
        }
        else if (AppSettings.IsLaserHoldShortcutDisabled(Settings.CursorHighlightHoldShortcut)
            || !Shortcut.TryParse(Settings.CursorHighlightHoldShortcut, out _cursorHighlightHoldShortcut))
        {
            Settings.CursorHighlightHoldShortcut = "XButton1";
            Shortcut.TryParse(Settings.CursorHighlightHoldShortcut, out _cursorHighlightHoldShortcut);
        }

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

    private enum RectResizeHandle
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}
