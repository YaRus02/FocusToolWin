using FocusTool.Win.Overlay;

namespace FocusTool.Win.Models;

internal sealed class RegionMask
{
    public RegionMask(int id, ScreenRect rect, AppSettings settings)
    {
        Id = id;
        Rect = rect;
        Color = settings.RegionMaskColor;
        Opacity = Math.Clamp(settings.RegionMaskOpacity, 0.1, 1.0);
    }

    public int Id { get; }
    public ScreenRect Rect { get; private set; }
    public string Color { get; }
    public double Opacity { get; }

    public bool Contains(ScreenPoint point) => Rect.Contains(point);

    public void MoveBy(double dx, double dy)
    {
        Rect = Rect.Offset(dx, dy);
    }

    public void SetRect(ScreenRect rect)
    {
        Rect = rect;
    }
}
