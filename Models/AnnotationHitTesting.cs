using FocusTool.Win.Overlay;

namespace FocusTool.Win.Models;

internal static class AnnotationHitTesting
{
    public const double EraserRadius = 10;

    public static bool TryFindShapeAt(
        IReadOnlyList<AnnotationShape> shapes,
        ScreenPoint point,
        out int index,
        double tolerance = 6,
        double? nowMs = null)
    {
        var hitRect = new ScreenRect(point.X, point.Y, point.X, point.Y).Inflate(tolerance);
        for (var i = shapes.Count - 1; i >= 0; i--)
        {
            if (nowMs is { } currentTime && shapes[i].IsExpired(currentTime))
            {
                continue;
            }

            if (shapes[i].Tool == AnnotationTool.Text
                ? shapes[i].GetBounds().Inflate(tolerance).Contains(point)
                : shapes[i].IntersectsSelection(hitRect))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    public static bool TryFindTextAt(IReadOnlyList<AnnotationShape> shapes, ScreenPoint point, out int index)
    {
        for (var i = shapes.Count - 1; i >= 0; i--)
        {
            if (shapes[i].Tool == AnnotationTool.Text && shapes[i].GetBounds().Contains(point))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    public static IReadOnlyList<AnnotationShape> FindShapesAlongPath(
        IReadOnlyList<AnnotationShape> shapes,
        ScreenPoint start,
        ScreenPoint end,
        double nowMs)
    {
        var distance = start.DistanceTo(end);
        var sampleCount = Math.Max(1, (int)Math.Ceiling(distance / Math.Max(2, EraserRadius * 0.55)));
        var hits = new HashSet<AnnotationShape>();
        for (var sample = 0; sample <= sampleCount; sample++)
        {
            var amount = sample / (double)sampleCount;
            var point = new ScreenPoint(
                start.X + (end.X - start.X) * amount,
                start.Y + (end.Y - start.Y) * amount);
            if (TryFindShapeAt(shapes, point, out var index, EraserRadius, nowMs))
            {
                hits.Add(shapes[index]);
            }
        }

        return hits.ToList();
    }

    public static bool TryHitEditHandle(AnnotationShape shape, ScreenPoint point, out AnnotationEditHandle handle)
    {
        const double hitRadius = 10;
        var hitRadiusSquared = hitRadius * hitRadius;
        if (shape.Tool is AnnotationTool.Line or AnnotationTool.Arrow)
        {
            if (DistanceSquared(point, shape.Start) <= hitRadiusSquared)
            {
                handle = AnnotationEditHandle.Start;
                return true;
            }

            if (DistanceSquared(point, shape.End) <= hitRadiusSquared)
            {
                handle = AnnotationEditHandle.End;
                return true;
            }
        }
        else if (shape.Tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.StepRect or AnnotationTool.Image)
        {
            var rect = ScreenRect.FromPoints(shape.Start, shape.End);
            if (DistanceSquared(point, new ScreenPoint(rect.Left, rect.Top)) <= hitRadiusSquared)
            {
                handle = AnnotationEditHandle.TopLeft;
                return true;
            }

            if (DistanceSquared(point, new ScreenPoint(rect.Right, rect.Top)) <= hitRadiusSquared)
            {
                handle = AnnotationEditHandle.TopRight;
                return true;
            }

            if (DistanceSquared(point, new ScreenPoint(rect.Left, rect.Bottom)) <= hitRadiusSquared)
            {
                handle = AnnotationEditHandle.BottomLeft;
                return true;
            }

            if (DistanceSquared(point, new ScreenPoint(rect.Right, rect.Bottom)) <= hitRadiusSquared)
            {
                handle = AnnotationEditHandle.BottomRight;
                return true;
            }
        }

        handle = AnnotationEditHandle.None;
        return false;
    }

    private static double DistanceSquared(ScreenPoint first, ScreenPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }
}
