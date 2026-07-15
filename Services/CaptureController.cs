using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal sealed class CaptureController
{
    private readonly ScreenshotService _screenshotService = new();
    private readonly Func<bool> _isDisposed;
    private readonly Func<bool> _isToolbarVisible;
    private readonly Action _hideToolbar;
    private readonly Action _showToolbar;
    private readonly Func<bool> _isMagnifierEnabled;
    private readonly Action _closeMagnifierHost;
    private readonly Action _updateMagnifierHost;
    private readonly Func<IDisposable?> _excludeOverlayFromCapture;
    private readonly Action _hidePinnedLensesForBoard;
    private readonly Action _restorePinnedLensesAfterBoard;
    private readonly Func<bool> _isVisualBoardMode;
    private readonly Func<ScreenRect, ScreenBoardPrivacySnapshot> _captureScreenBoardPrivacySnapshot;
    private readonly Func<ScreenBoardFrame, BitmapSource?> _captureScreenBoardFrame;
    private readonly Action<string, string> _showMessage;

    public CaptureController(
        Func<bool> isDisposed,
        Func<bool> isToolbarVisible,
        Action hideToolbar,
        Action showToolbar,
        Func<bool> isMagnifierEnabled,
        Action closeMagnifierHost,
        Action updateMagnifierHost,
        Func<IDisposable?> excludeOverlayFromCapture,
        Action hidePinnedLensesForBoard,
        Action restorePinnedLensesAfterBoard,
        Func<bool> isVisualBoardMode,
        Func<ScreenRect, ScreenBoardPrivacySnapshot> captureScreenBoardPrivacySnapshot,
        Func<ScreenBoardFrame, BitmapSource?> captureScreenBoardFrame,
        Action<string, string> showMessage)
    {
        _isDisposed = isDisposed;
        _isToolbarVisible = isToolbarVisible;
        _hideToolbar = hideToolbar;
        _showToolbar = showToolbar;
        _isMagnifierEnabled = isMagnifierEnabled;
        _closeMagnifierHost = closeMagnifierHost;
        _updateMagnifierHost = updateMagnifierHost;
        _excludeOverlayFromCapture = excludeOverlayFromCapture;
        _hidePinnedLensesForBoard = hidePinnedLensesForBoard;
        _restorePinnedLensesAfterBoard = restorePinnedLensesAfterBoard;
        _isVisualBoardMode = isVisualBoardMode;
        _captureScreenBoardPrivacySnapshot = captureScreenBoardPrivacySnapshot;
        _captureScreenBoardFrame = captureScreenBoardFrame;
        _showMessage = showMessage;
    }

    public bool IsCaptureInProgress { get; private set; }

    public async Task TakeScreenshotAsync()
    {
        if (_isDisposed() || IsCaptureInProgress)
        {
            return;
        }

        IsCaptureInProgress = true;
        var toolbarWasVisible = _isToolbarVisible();
        var magnifierWasActive = _isMagnifierEnabled();

        // Keep the screen overlay (region masks are a privacy layer that must be in the
        // shot), pinned lenses and timers visible. Only the toolbar and the cursor-
        // following magnifier lens are excluded from the capture.
        if (toolbarWasVisible)
        {
            _hideToolbar();
        }

        // UpdateMagnifierHost stays a no-op while capture is in progress, so the render
        // loop will not bring the lens back before the capture completes.
        _closeMagnifierHost();

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
            _showMessage("Screenshot failed", ex.Message);
        }
        finally
        {
            if (toolbarWasVisible && !_isDisposed())
            {
                _showToolbar();
            }

            IsCaptureInProgress = false;

            if (magnifierWasActive && !_isDisposed())
            {
                _updateMagnifierHost();
            }
        }
    }

    public async Task TakeRegionScreenshotAsync(ScreenRect sourceRect, bool restoreToolbar)
    {
        if (_isDisposed())
        {
            return;
        }

        if (IsCaptureInProgress)
        {
            if (restoreToolbar && !_isDisposed())
            {
                _showToolbar();
            }

            return;
        }

        IsCaptureInProgress = true;
        var magnifierWasActive = _isMagnifierEnabled();

        // The toolbar is already hidden by the rectangle selection mode and is restored
        // only after capture. Keep overlays visible so annotations and masks are
        // captured exactly like the full-monitor screenshot path.
        _closeMagnifierHost();
        await WaitForScreenRefreshAsync();

        try
        {
            await _screenshotService.CaptureRegionAsync(sourceRect, copyToClipboard: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not capture region screenshot.", ex);
            _showMessage("Region screenshot failed", ex.Message);
        }
        finally
        {
            if (restoreToolbar && !_isDisposed())
            {
                _showToolbar();
            }

            IsCaptureInProgress = false;

            if (magnifierWasActive && !_isDisposed())
            {
                _updateMagnifierHost();
            }
        }
    }

    public async Task EnterScreenBoardAsync(
        Action<ScreenBoardFrame> enterScreenBoard,
        Action restorePreviousMode)
    {
        if (_isDisposed() || IsCaptureInProgress)
        {
            return;
        }

        IsCaptureInProgress = true;
        var toolbarWasVisible = _isToolbarVisible();
        var magnifierWasEnabled = _isMagnifierEnabled();
        var boardBounds = _screenshotService.GetCurrentMonitorBounds();
        ScreenBoardPrivacySnapshot privacySnapshot;
        try
        {
            privacySnapshot = _captureScreenBoardPrivacySnapshot(boardBounds);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not render the Screen Board privacy layer.", ex);
            _showMessage("Screen Board cancelled", "Region masks could not be secured in the board snapshot.");
            IsCaptureInProgress = false;
            return;
        }

        if (!privacySnapshot.IsComplete)
        {
            AppLog.Error("Screen Board privacy layer was unavailable while region masks were active.");
            _showMessage("Screen Board cancelled", "Region masks could not be secured in the board snapshot.");
            IsCaptureInProgress = false;
            return;
        }

        using var overlayCaptureExclusion = _excludeOverlayFromCapture();
        if (overlayCaptureExclusion is null)
        {
            AppLog.Error("Screen Board could not exclude the live overlay from capture.");
            _showMessage("Screen Board cancelled", "The live privacy overlay could not be excluded from capture.");
            IsCaptureInProgress = false;
            return;
        }

        if (toolbarWasVisible)
        {
            _hideToolbar();
        }

        _closeMagnifierHost();
        _hidePinnedLensesForBoard();
        await WaitForScreenRefreshAsync();

        try
        {
            var frame = await _screenshotService.CaptureFrameAsync(boardBounds);
            if (privacySnapshot.Layer is { } privacyLayer)
            {
                var protectedImage = ScreenBoardCompositor.CompositePrivacyLayer(frame.Image, privacyLayer);
                frame = new ScreenBoardFrame(frame.Bounds, protectedImage, privacySnapshot.MaskIds);
            }

            enterScreenBoard(frame);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not capture screen board.", ex);
            _showMessage("Screen board failed", ex.Message);
            restorePreviousMode();
        }
        finally
        {
            if (toolbarWasVisible && !_isDisposed())
            {
                _showToolbar();
            }

            if (magnifierWasEnabled && !_isVisualBoardMode() && !_isDisposed())
            {
                _updateMagnifierHost();
            }

            // Restore pinned lenses unless we actually entered a board (then they stay
            // hidden until the board is dismissed by the interaction mode transition).
            if (!_isVisualBoardMode() && !_isDisposed())
            {
                _restorePinnedLensesAfterBoard();
            }

            IsCaptureInProgress = false;
        }
    }

    public async Task SaveScreenBoardSnapshotAsync(ScreenBoardFrame? frame)
    {
        if (frame is null)
        {
            return;
        }

        try
        {
            var image = _captureScreenBoardFrame(frame);
            if (image is null)
            {
                return;
            }

            await _screenshotService.SaveImageAsync(image, copyToClipboard: true, fileNamePrefix: "FocusTool_Board");
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not save screen board snapshot.", ex);
            _showMessage("Screen board save failed", ex.Message);
        }
    }

    public static async Task WaitForScreenRefreshAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(70);
    }
}
