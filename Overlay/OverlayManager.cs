using FocusTool.Win.Models;
using Microsoft.Win32;
using FocusTool.Win.Native;
using System.Windows.Media.Imaging;
using Screen = System.Windows.Forms.Screen;

namespace FocusTool.Win.Overlay;

internal sealed class OverlayManager : IDisposable
{
    private static readonly TimeSpan TopmostReassertInterval = TimeSpan.FromSeconds(2);
    private readonly TrailModel _trailModel;
    private readonly AnnotationDocument _annotations;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<InteractionMode> _modeProvider;
    private readonly Func<double> _clockProvider;
    private readonly Func<ScreenPoint?> _spotlightProvider;
    private readonly Func<CursorHighlightFrame> _cursorHighlightProvider;
    private readonly Func<ScreenBoardFrame?> _screenBoardProvider;
    private readonly Func<RectOverlayVisual?> _rectOverlayProvider;
    private readonly Func<IReadOnlyList<RegionMask>> _regionMaskProvider;
    private readonly Func<int> _regionMaskSelectionProvider;
    private readonly Func<IReadOnlyList<ScreenRect>> _spotlightRegionProvider;
    private readonly Func<int> _spotlightRegionSelectionProvider;
    private readonly Func<LiveAdjustmentHudFrame?> _liveAdjustmentHudProvider;
    private readonly IOverlayInputHandler _inputHandler;
    private readonly Action? _beforeTopmostReassert;
    private readonly Action? _afterTopmostReassert;
    private readonly List<OverlayWindow> _windows = [];
    private readonly System.Windows.Threading.DispatcherTimer _topmostTimer;
    private long _captureRevision;
    private bool _visible;
    private bool _disposed;

    public OverlayManager(
        TrailModel trailModel,
        AnnotationDocument annotations,
        Func<AppSettings> settingsProvider,
        Func<InteractionMode> modeProvider,
        Func<double> clockProvider,
        Func<ScreenPoint?> spotlightProvider,
        Func<CursorHighlightFrame> cursorHighlightProvider,
        Func<ScreenBoardFrame?> screenBoardProvider,
        Func<RectOverlayVisual?> rectOverlayProvider,
        Func<IReadOnlyList<RegionMask>> regionMaskProvider,
        Func<int> regionMaskSelectionProvider,
        Func<IReadOnlyList<ScreenRect>> spotlightRegionProvider,
        Func<int> spotlightRegionSelectionProvider,
        Func<LiveAdjustmentHudFrame?> liveAdjustmentHudProvider,
        IOverlayInputHandler inputHandler,
        Action? beforeTopmostReassert = null,
        Action? afterTopmostReassert = null)
    {
        _trailModel = trailModel;
        _annotations = annotations;
        _settingsProvider = settingsProvider;
        _modeProvider = modeProvider;
        _clockProvider = clockProvider;
        _spotlightProvider = spotlightProvider;
        _cursorHighlightProvider = cursorHighlightProvider;
        _screenBoardProvider = screenBoardProvider;
        _rectOverlayProvider = rectOverlayProvider;
        _regionMaskProvider = regionMaskProvider;
        _regionMaskSelectionProvider = regionMaskSelectionProvider;
        _spotlightRegionProvider = spotlightRegionProvider;
        _spotlightRegionSelectionProvider = spotlightRegionSelectionProvider;
        _liveAdjustmentHudProvider = liveAdjustmentHudProvider;
        _inputHandler = inputHandler;
        _beforeTopmostReassert = beforeTopmostReassert;
        _afterTopmostReassert = afterTopmostReassert;
        _topmostTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TopmostReassertInterval
        };
        _topmostTimer.Tick += OnTopmostTimerTick;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void Show()
    {
        if (_disposed)
        {
            return;
        }

        _visible = true;
        EnsureWindows();

        foreach (var window in _windows)
        {
            if (!window.IsVisible)
            {
                window.Show();
            }

            window.SetInteractionMode(_modeProvider());
            window.PositionOverScreen();
        }

        ReassertTopmost();
        _topmostTimer.Start();
    }

    public void Hide()
    {
        _visible = false;

        foreach (var window in _windows)
        {
            window.Hide();
        }

        _topmostTimer.Stop();
    }

    public void Invalidate()
    {
        _captureRevision++;
        if (!_visible)
        {
            return;
        }

        foreach (var window in _windows)
        {
            window.Refresh();
        }
    }

    // Repaint only the monitor(s) whose content actually changes as the cursor moves
    // (spotlight hole / magnifier ring). Other monitors keep an identical cached
    // frame, so on a multi-monitor setup idle surfaces are not repainted every move.
    // Both the current and previous cursor monitors are refreshed to clear the old
    // position when the cursor crosses a monitor boundary.
    public void InvalidateForCursor(ScreenPoint current, ScreenPoint previous)
    {
        _captureRevision++;
        if (!_visible)
        {
            return;
        }

        foreach (var window in _windows)
        {
            if (window.Contains(current) || window.Contains(previous))
            {
                window.Refresh();
            }
        }
    }

    public void SetInteractionMode(InteractionMode mode)
    {
        foreach (var window in _windows)
        {
            window.SetInteractionMode(mode);
        }
    }

    public IReadOnlyList<IntPtr> GetWindowHandles()
    {
        return _windows
            .Select(window => window.Handle)
            .Where(handle => handle != IntPtr.Zero)
            .ToArray();
    }

    public long CaptureRevision => _captureRevision;

    public IDisposable? TryExcludeVisibleWindowsFromCapture()
    {
        var handles = _windows
            .Where(window => window.IsVisible)
            .Select(window => window.Handle)
            .ToArray();
        return handles.Any(handle => handle == IntPtr.Zero)
            ? null
            : WindowCaptureExclusionScope.TryCreate(handles);
    }

    public double GetDpiScaleForPoint(ScreenPoint point)
    {
        OverlayWindow? nearestWindow = null;
        var nearestDistance = double.PositiveInfinity;

        foreach (var window in _windows)
        {
            if (window.Contains(point))
            {
                return window.DpiScaleX;
            }

            var distance = window.DistanceSquaredTo(point);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestWindow = window;
            }
        }

        return nearestWindow?.DpiScaleX ?? 1.0;
    }

    public void ReassertTopmost()
    {
        _beforeTopmostReassert?.Invoke();
        ReassertOverlayAndChromeTopmost();
    }

    public void ReassertOverlayAndChromeTopmost()
    {
        foreach (var window in _windows)
        {
            window.ReassertTopmost();
        }

        _afterTopmostReassert?.Invoke();
    }

    public BitmapSource? CaptureScreenBoardFrame(ScreenBoardFrame frame)
    {
        foreach (var window in _windows)
        {
            if (window.Intersects(frame.Bounds))
            {
                return window.CaptureSurface();
            }
        }

        return null;
    }

    public ScreenBoardPrivacySnapshot CaptureScreenBoardPrivacySnapshot(ScreenRect bounds)
    {
        var maskIds = _regionMaskProvider()
            .Where(mask => mask.Rect.Intersects(bounds))
            .Select(mask => mask.Id)
            .ToHashSet();
        if (maskIds.Count == 0)
        {
            return new ScreenBoardPrivacySnapshot(layer: null, maskIds: maskIds);
        }

        var window = _windows.FirstOrDefault(candidate => candidate.Intersects(bounds));
        var layer = window?.CaptureRegionMaskSurfaceRegion(bounds);
        return new ScreenBoardPrivacySnapshot(layer, maskIds);
    }

    // Snapshot only the part of the overlay surface that overlaps the captured
    // source rect. The Capture Stage can draw it straight onto its back buffer.
    public OverlayLayer? CaptureOverlayLayer(ScreenRect rect)
    {
        var width = (int)Math.Round(rect.Width);
        var height = (int)Math.Round(rect.Height);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var stride = width * 4;
        var pixels = new byte[stride * height];
        var capturedAny = false;
        foreach (var window in _windows)
        {
            if (!rect.TryIntersect(window.ScreenBounds, out var intersection))
            {
                continue;
            }

            var bitmap = window.CaptureSurfaceRegion(intersection, OverlayRenderOptions.CaptureStage);
            if (bitmap is null)
            {
                continue;
            }

            var left = (int)Math.Round(intersection.Left - rect.Left);
            var top = (int)Math.Round(intersection.Top - rect.Top);
            OverlayLayerComposer.CopyInto(bitmap, pixels, width, height, left, top);
            capturedAny = true;
        }

        return capturedAny ? new OverlayLayer(pixels, width, height, stride) : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _topmostTimer.Stop();
        _topmostTimer.Tick -= OnTopmostTimerTick;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        CloseWindows();
    }

    private void OnTopmostTimerTick(object? sender, EventArgs e)
    {
        if (_disposed || !_visible)
        {
            return;
        }

        ReassertTopmost();
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

        var wasVisible = _visible;
        _topmostTimer.Stop();
        CloseWindows();
        _captureRevision++;

        if (wasVisible)
        {
            Show();
        }
    }

    private void EnsureWindows()
    {
        if (_windows.Count > 0)
        {
            return;
        }

        foreach (var screen in Screen.AllScreens)
        {
            _windows.Add(new OverlayWindow(
                screen,
                _trailModel,
                _annotations,
                _settingsProvider,
                _modeProvider,
                _clockProvider,
                _spotlightProvider,
                _cursorHighlightProvider,
                _screenBoardProvider,
                _rectOverlayProvider,
                _regionMaskProvider,
                _regionMaskSelectionProvider,
                _spotlightRegionProvider,
                _spotlightRegionSelectionProvider,
                _liveAdjustmentHudProvider,
                _inputHandler));
        }
    }

    private void CloseWindows()
    {
        foreach (var window in _windows)
        {
            window.Close();
        }

        _windows.Clear();
    }
}
