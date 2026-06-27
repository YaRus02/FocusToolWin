using FocusTool.Win.Overlay;

namespace FocusTool.Win.Models;

internal static class AnnotationHitTesting
{
    public static bool TryFindShapeAt(IReadOnlyList<AnnotationShape> shapes, ScreenPoint point, out int index)
    {
        var hitRect = new ScreenRect(point.X, point.Y, point.X, point.Y).Inflate(6);
        for (var i = shapes.Count - 1; i >= 0; i--)
        {
            if (shapes[i].Tool == AnnotationTool.Text
                ? shapes[i].GetBounds().Contains(point)
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
