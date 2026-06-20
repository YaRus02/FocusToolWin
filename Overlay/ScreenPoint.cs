namespace FocusTool.Win.Overlay;

internal readonly record struct ScreenPoint(double X, double Y)
{
    public double DistanceTo(ScreenPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public ScreenPoint Offset(double dx, double dy)
    {
        return new ScreenPoint(X + dx, Y + dy);
    }
}
