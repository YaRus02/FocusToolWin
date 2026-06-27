using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal sealed class RectEditSession
{
    private ScreenPoint _lastMovePoint;
    private ScreenPoint _resizeAnchor;

    public bool IsMoving { get; private set; }
    public RectResizeHandle ResizeHandle { get; private set; }
    public bool IsResizing => ResizeHandle != RectResizeHandle.None;

    public void BeginMove(ScreenPoint point)
    {
        _lastMovePoint = point;
        IsMoving = true;
        ResizeHandle = RectResizeHandle.None;
    }

    public ScreenRect Move(ScreenRect rect, ScreenPoint point)
    {
        var moved = rect.Offset(point.X - _lastMovePoint.X, point.Y - _lastMovePoint.Y);
        _lastMovePoint = point;
        return moved;
    }

    public void BeginResize(ScreenRect rect, RectResizeHandle handle)
    {
        _resizeAnchor = RectGeometry.GetResizeAnchor(rect, handle);
        ResizeHandle = handle;
        IsMoving = false;
    }

    public ScreenRect Resize(ScreenPoint point)
    {
        return RectGeometry.CreateResizeRect(_resizeAnchor, point);
    }

    public void EndPointerAction()
    {
        IsMoving = false;
        ResizeHandle = RectResizeHandle.None;
    }

    public void Cancel()
    {
        EndPointerAction();
    }
}
