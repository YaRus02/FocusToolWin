using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal sealed class RectSelectionSession
{
    private ScreenPoint _start;

    public bool IsActive { get; private set; }
    public ScreenRect? Draft { get; private set; }

    public void Begin(ScreenPoint point)
    {
        _start = point;
        IsActive = true;
        Draft = ScreenRect.FromPoints(point, point);
    }

    public bool Update(ScreenPoint point)
    {
        if (!IsActive)
        {
            return false;
        }

        Draft = ScreenRect.FromPoints(_start, point);
        return true;
    }

    public ScreenRect? Complete(ScreenPoint point)
    {
        if (!IsActive)
        {
            return null;
        }

        var rect = ScreenRect.FromPoints(_start, point);
        Cancel();
        return rect;
    }

    public void Cancel()
    {
        IsActive = false;
        Draft = null;
    }
}
