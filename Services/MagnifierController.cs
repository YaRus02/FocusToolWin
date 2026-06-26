using System.Windows.Media;
using FocusTool.Win.Models;
using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal sealed class MagnifierController : IDisposable
{
    private const double RenderCursorMovementThresholdPixels = 0.5;

    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<bool> _isDisposed;
    private readonly Func<bool> _isCaptureInProgress;
    private readonly Func<bool> _isVisualBoardMode;
    private readonly Func<ScreenPoint, double?> _dpiScaleProvider;
    private readonly Func<IReadOnlyList<IntPtr>> _excludedWindowsProvider;
    private readonly TryGetScreenPoint _tryGetCursor;
    private readonly Action<ScreenPoint, ScreenPoint?, bool> _cursorChanged;
    private readonly Action _reassertTopmost;
    private MagnifierHostWindow? _host;
    private ScreenPoint _lastRenderCursor;
    private bool _hasLastRenderCursor;
    private bool _renderingSubscribed;

    public MagnifierController(
        Func<AppSettings> settingsProvider,
        Func<bool> isDisposed,
        Func<bool> isCaptureInProgress,
        Func<bool> isVisualBoardMode,
        Func<ScreenPoint, double?> dpiScaleProvider,
        Func<IReadOnlyList<IntPtr>> excludedWindowsProvider,
        TryGetScreenPoint tryGetCursor,
        Action<ScreenPoint, ScreenPoint?, bool> cursorChanged,
        Action reassertTopmost)
    {
        _settingsProvider = settingsProvider;
        _isDisposed = isDisposed;
        _isCaptureInProgress = isCaptureInProgress;
        _isVisualBoardMode = isVisualBoardMode;
        _dpiScaleProvider = dpiScaleProvider;
        _excludedWindowsProvider = excludedWindowsProvider;
        _tryGetCursor = tryGetCursor;
        _cursorChanged = cursorChanged;
        _reassertTopmost = reassertTopmost;
    }

    public bool IsRenderingSubscribed => _renderingSubscribed;

    public void SubscribeRendering()
    {
        if (_renderingSubscribed)
        {
            return;
        }

        ResetRenderCursor();
        CompositionTarget.Rendering += OnRendering;
        _renderingSubscribed = true;
    }

    public void UnsubscribeRendering()
    {
        if (!_renderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _renderingSubscribed = false;
        ResetRenderCursor();
    }

    public void RefreshFromCurrentCursor(bool forceCursorInvalidation, double movementThresholdPixels)
    {
        if (!_tryGetCursor(out var cursor))
        {
            return;
        }

        var previous = _hasLastRenderCursor ? _lastRenderCursor : (ScreenPoint?)null;
        if (forceCursorInvalidation
            || !_hasLastRenderCursor
            || cursor.DistanceTo(_lastRenderCursor) >= movementThresholdPixels)
        {
            _lastRenderCursor = cursor;
            _hasLastRenderCursor = true;
            _cursorChanged(cursor, previous, forceCursorInvalidation);
        }

        UpdateHost(cursor);
    }

    public void UpdateHost(ScreenPoint cursor)
    {
        if (_isCaptureInProgress())
        {
            // The cursor-following magnifier lens must not appear in a screenshot or
            // screen-board frame; the render loop recreates it once capture ends.
            CloseHost();
            return;
        }

        var settings = _settingsProvider();
        if (!settings.MagnifierEnabled || _isVisualBoardMode() || _dpiScaleProvider(cursor) is not { } dpiScale)
        {
            if (_isVisualBoardMode())
            {
                CloseHost();
            }

            return;
        }

        var hostCreated = false;
        if (_host is null)
        {
            var host = new MagnifierHostWindow();
            if (!host.IsAvailable)
            {
                host.Dispose();
                return;
            }

            _host = host;
            hostCreated = true;
        }

        _host.UpdateLens(cursor, settings, dpiScale, _excludedWindowsProvider());
        if (!_host.IsAvailable)
        {
            CloseHost();
            return;
        }

        if (hostCreated)
        {
            _reassertTopmost();
        }
    }

    public void CloseHost()
    {
        _host?.Dispose();
        _host = null;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_isDisposed())
        {
            return;
        }

        var settings = _settingsProvider();
        if (!settings.MagnifierEnabled || _isVisualBoardMode() || !_tryGetCursor(out var cursor))
        {
            if (_isVisualBoardMode())
            {
                CloseHost();
            }

            return;
        }

        var cursorMoved = !_hasLastRenderCursor
            || cursor.DistanceTo(_lastRenderCursor) >= RenderCursorMovementThresholdPixels;
        if (cursorMoved)
        {
            var previous = _hasLastRenderCursor ? _lastRenderCursor : (ScreenPoint?)null;
            _lastRenderCursor = cursor;
            _hasLastRenderCursor = true;
            _cursorChanged(cursor, previous, false);
        }

        // Refresh the magnified source even while the cursor is stationary so
        // video and other dynamic content continue to animate inside the lens.
        UpdateHost(cursor);
    }

    private void ResetRenderCursor()
    {
        _hasLastRenderCursor = false;
    }

    public void Dispose()
    {
        UnsubscribeRendering();
        CloseHost();
    }
}
