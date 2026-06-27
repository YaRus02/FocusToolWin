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
        Style = Enum.TryParse<RegionMaskStyle>(settings.RegionMaskStyle, true, out var style)
            ? style
            : RegionMaskStyle.StripesWithLabel;
    }

    public int Id { get; }
    public ScreenRect Rect { get; private set; }
    public string Color { get; private set; }
    public double Opacity { get; private set; }
    public RegionMaskStyle Style { get; private set; }

    public bool Contains(ScreenPoint point) => Rect.Contains(point);

    public void MoveBy(double dx, double dy)
    {
        Rect = Rect.Offset(dx, dy);
    }

    public void SetRect(ScreenRect rect)
    {
        Rect = rect;
    }

    public void SetStyle(RegionMaskStyle style)
    {
        Style = style;
    }

    public void SetColor(string color)
    {
        Color = color;
    }

    public void SetOpacity(double opacity)
    {
        Opacity = Math.Clamp(opacity, 0.1, 1.0);
    }
}

internal enum RegionMaskStyle
{
    Solid,
    Stripes,
    Label,
    StripesWithLabel
}
