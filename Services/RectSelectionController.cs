using FocusTool.Win.Models;
using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal sealed class RectSelectionController
{
    private readonly RectSelectionSession _selection = new();
    private readonly RectEditSession _screenshotEdit = new();
    private readonly Func<InteractionMode> _modeProvider;
    private readonly Func<bool> _isDisposed;
    private readonly Func<bool> _isToolbarVisible;
    private readonly Action _hideToolbarTransient;
    private readonly Action _showToolbar;
    private ScreenRect? _pendingScreenshotRegion;
    private bool _restoreToolbarAfterRectSelection;
    private bool _screenshotRegionToolbarRestorePending;

    public RectSelectionController(
        Func<InteractionMode> modeProvider,
        Func<bool> isDisposed,
        Func<bool> isToolbarVisible,
        Action hideToolbarTransient,
        Action showToolbar)
    {
        _modeProvider = modeProvider;
        _isDisposed = isDisposed;
        _isToolbarVisible = isToolbarVisible;
        _hideToolbarTransient = hideToolbarTransient;
        _showToolbar = showToolbar;
    }

    public bool IsDraftActive => _selection.IsActive;
    public bool IsScreenshotRegionResizing => _screenshotEdit.IsResizing;
    public bool IsScreenshotRegionMoving => _screenshotEdit.IsMoving;
    public ScreenRect? PendingScreenshotRegion => _pendingScreenshotRegion;

    public void BeginMode(InteractionMode mode)
    {
        if (!IsRectSelectionMode(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Mode is not a rectangle selection mode.");
        }

        _selection.Cancel();
        _screenshotEdit.Cancel();
        _restoreToolbarAfterRectSelection = _isToolbarVisible();
        if (_restoreToolbarAfterRectSelection)
        {
            _hideToolbarTransient();
        }
    }

    public void BeginScreenshotMode()
    {
        _pendingScreenshotRegion = null;
        _screenshotRegionToolbarRestorePending = false;
        _screenshotEdit.Cancel();
        BeginMode(InteractionMode.ScreenshotRegionSelect);
    }

    public void BeginDraft(ScreenPoint point)
    {
        _selection.Begin(point);
    }

    public bool UpdateDraft(ScreenPoint point)
    {
        return _selection.Update(point);
    }

    public ScreenRect? CompleteDraft(ScreenPoint point)
    {
        return _selection.Complete(point);
    }

    public void CancelDraft()
    {
        _selection.Cancel();
    }

    public bool TryBeginScreenshotEdit(ScreenPoint point)
    {
        if (_pendingScreenshotRegion is not { } pending)
        {
            return false;
        }

        if (RectGeometry.TryHitResizeHandle(pending, point, out var handle))
        {
            _screenshotEdit.BeginResize(pending, handle);
            _selection.Cancel();
            return true;
        }

        if (pending.Contains(point))
        {
            _screenshotEdit.BeginMove(point);
            _selection.Cancel();
            return true;
        }

        _pendingScreenshotRegion = null;
        _screenshotEdit.Cancel();
        return false;
    }

    public bool UpdateScreenshotEdit(ScreenPoint point)
    {
        if (_pendingScreenshotRegion is { } pending && _screenshotEdit.IsResizing)
        {
            _pendingScreenshotRegion = _screenshotEdit.Resize(point);
            return true;
        }

        if (_pendingScreenshotRegion is { } movingPending && _screenshotEdit.IsMoving)
        {
            _pendingScreenshotRegion = _screenshotEdit.Move(movingPending, point);
            return true;
        }

        return false;
    }

    public void EndScreenshotPointerAction()
    {
        _screenshotEdit.EndPointerAction();
    }

    public void CancelScreenshotPointerState()
    {
        _screenshotEdit.Cancel();
        _selection.Cancel();
    }

    public void SetPendingScreenshotRegion(ScreenRect rect)
    {
        _pendingScreenshotRegion = rect;
        _screenshotRegionToolbarRestorePending = _restoreToolbarAfterRectSelection;
        _restoreToolbarAfterRectSelection = false;
    }

    public bool TryTakePendingScreenshotRegion(out ScreenRect rect, out bool restoreToolbar)
    {
        if (_pendingScreenshotRegion is not { } pending)
        {
            rect = default;
            restoreToolbar = false;
            return false;
        }

        rect = pending;
        restoreToolbar = _screenshotRegionToolbarRestorePending;
        ResetScreenshotRegionEditState(restoreToolbar: false);
        _restoreToolbarAfterRectSelection = false;
        return true;
    }

    public bool TryNudgePendingScreenshotRegion(double dx, double dy)
    {
        if (_pendingScreenshotRegion is not { } rect)
        {
            return false;
        }

        _pendingScreenshotRegion = rect.Offset(dx, dy);
        return true;
    }

    public void RestoreToolbarAfterRectSelection()
    {
        if (!_restoreToolbarAfterRectSelection)
        {
            return;
        }

        _restoreToolbarAfterRectSelection = false;
        if (!_isDisposed())
        {
            _showToolbar();
        }
    }

    public void ResetScreenshotRegionEditState(bool restoreToolbar)
    {
        _pendingScreenshotRegion = null;
        _screenshotEdit.Cancel();
        if (restoreToolbar)
        {
            RestoreScreenshotRegionToolbarIfNeeded();
        }
        else
        {
            _screenshotRegionToolbarRestorePending = false;
        }
    }

    public void ResetRectStateForMode(InteractionMode mode)
    {
        _selection.Cancel();
        if (mode == InteractionMode.ScreenshotRegionSelect)
        {
            ResetScreenshotRegionEditState(restoreToolbar: true);
        }
    }

    public RectOverlayVisual? GetOverlayVisual()
    {
        var mode = _modeProvider();
        if (_selection.Draft is { } draft)
        {
            return new RectOverlayVisual(
                draft,
                IsDraft: true,
                ShowHandles: false,
                ShowReadout: mode == InteractionMode.ScreenshotRegionSelect);
        }

        if (mode == InteractionMode.ScreenshotRegionSelect && _pendingScreenshotRegion is { } pending)
        {
            return new RectOverlayVisual(
                pending,
                IsDraft: false,
                ShowHandles: true,
                ShowReadout: true);
        }

        return null;
    }

    private void RestoreScreenshotRegionToolbarIfNeeded()
    {
        if (!_screenshotRegionToolbarRestorePending)
        {
            return;
        }

        _screenshotRegionToolbarRestorePending = false;
        if (!_isDisposed())
        {
            _showToolbar();
        }
    }

    private static bool IsRectSelectionMode(InteractionMode mode)
    {
        return mode is InteractionMode.PinnedLensSelect
            or InteractionMode.RegionMaskSelect
            or InteractionMode.ScreenshotRegionSelect
            or InteractionMode.RegionSpotlightSelect;
    }
}
