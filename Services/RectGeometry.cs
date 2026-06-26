using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal static class RectGeometry
{
    public const double DefaultMinSizePixels = 8;
    public const double DefaultResizeHitRadiusPixels = 12;

    public static bool IsLargeEnough(ScreenRect rect, double minSize = DefaultMinSizePixels) =>
        rect.Width >= minSize && rect.Height >= minSize;

    public static bool TryHitResizeHandle(
        ScreenRect rect,
        ScreenPoint point,
        out RectResizeHandle handle,
        double hitRadius = DefaultResizeHitRadiusPixels)
    {
        var hitRadiusSquared = hitRadius * hitRadius;
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

    public static ScreenPoint GetResizeAnchor(ScreenRect rect, RectResizeHandle handle)
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

    public static ScreenRect CreateResizeRect(
        ScreenPoint anchor,
        ScreenPoint point,
        double minSize = DefaultMinSizePixels)
    {
        var left = point.X < anchor.X
            ? Math.Min(point.X, anchor.X - minSize)
            : anchor.X;
        var right = point.X < anchor.X
            ? anchor.X
            : Math.Max(point.X, anchor.X + minSize);
        var top = point.Y < anchor.Y
            ? Math.Min(point.Y, anchor.Y - minSize)
            : anchor.Y;
        var bottom = point.Y < anchor.Y
            ? anchor.Y
            : Math.Max(point.Y, anchor.Y + minSize);

        return new ScreenRect(left, top, right, bottom);
    }

    private static double DistanceSquared(ScreenPoint first, ScreenPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }
}
