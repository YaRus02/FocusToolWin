namespace FocusTool.Win.Overlay;

internal readonly record struct ScreenRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Math.Max(0, Right - Left);
    public double Height => Math.Max(0, Bottom - Top);

    public static ScreenRect FromPoints(ScreenPoint first, ScreenPoint second)
    {
        return new ScreenRect(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Max(first.X, second.X),
            Math.Max(first.Y, second.Y));
    }

    public ScreenRect Inflate(double amount)
    {
        return new ScreenRect(Left - amount, Top - amount, Right + amount, Bottom + amount);
    }

    public ScreenRect Offset(double dx, double dy)
    {
        return new ScreenRect(Left + dx, Top + dy, Right + dx, Bottom + dy);
    }

    public ScreenRect Union(ScreenRect other)
    {
        return new ScreenRect(
            Math.Min(Left, other.Left),
            Math.Min(Top, other.Top),
            Math.Max(Right, other.Right),
            Math.Max(Bottom, other.Bottom));
    }

    public bool Contains(ScreenPoint point)
    {
        return point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
    }

    public bool Intersects(ScreenRect other)
    {
        return Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
    }

    public bool TryIntersect(ScreenRect other, out ScreenRect intersection)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        if (left >= right || top >= bottom)
        {
            intersection = default;
            return false;
        }

        intersection = new ScreenRect(left, top, right, bottom);
        return true;
    }
}
