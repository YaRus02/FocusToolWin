using FocusTool.Win.Overlay;

namespace FocusTool.Win.Models;

internal static class AnnotationGeometry
{
    public static bool CanResize(AnnotationTool tool)
    {
        return tool is AnnotationTool.Rectangle
            or AnnotationTool.Ellipse
            or AnnotationTool.StepRect
            or AnnotationTool.Image
            or AnnotationTool.Line
            or AnnotationTool.Arrow;
    }

    public static void ResizeShape(AnnotationShape shape, AnnotationEditHandle handle, ScreenPoint point, bool shift)
    {
        if (shape.Tool is AnnotationTool.Line or AnnotationTool.Arrow)
        {
            if (handle == AnnotationEditHandle.Start)
            {
                var nextStart = shift ? ApplyConstraint(AnnotationTool.Line, shape.End, point, shift: true) : point;
                shape.SetEndpoints(nextStart, shape.End);
            }
            else if (handle == AnnotationEditHandle.End)
            {
                var nextEnd = shift ? ApplyConstraint(AnnotationTool.Line, shape.Start, point, shift: true) : point;
                shape.SetEndpoints(shape.Start, nextEnd);
            }

            return;
        }

        if (shape.Tool is not (AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.StepRect or AnnotationTool.Image))
        {
            return;
        }

        var rect = ScreenRect.FromPoints(shape.Start, shape.End);
        var anchor = handle switch
        {
            AnnotationEditHandle.TopLeft => new ScreenPoint(rect.Right, rect.Bottom),
            AnnotationEditHandle.TopRight => new ScreenPoint(rect.Left, rect.Bottom),
            AnnotationEditHandle.BottomLeft => new ScreenPoint(rect.Right, rect.Top),
            AnnotationEditHandle.BottomRight => new ScreenPoint(rect.Left, rect.Top),
            _ => shape.Start
        };
        var next = shape.Tool == AnnotationTool.Image
            ? ApplyImageResizeConstraint(anchor, point, shape)
            : ApplyConstraint(shape.Tool, anchor, point, shift);
        shape.SetEndpoints(anchor, next);
    }

    public static ScreenPoint ApplyConstraint(AnnotationTool tool, ScreenPoint start, ScreenPoint current, bool shift)
    {
        if (!shift)
        {
            return current;
        }

        var dx = current.X - start.X;
        var dy = current.Y - start.Y;

        if (tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.StepRect)
        {
            var size = Math.Max(Math.Abs(dx), Math.Abs(dy));
            // Use a non-zero sign so an axis-aligned drag (dx==0 or dy==0) still
            // produces a square instead of collapsing to a zero-width/height shape.
            var signX = dx < 0 ? -1 : 1;
            var signY = dy < 0 ? -1 : 1;
            return new ScreenPoint(start.X + signX * size, start.Y + signY * size);
        }

        if (tool == AnnotationTool.Line)
        {
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.01)
            {
                return current;
            }

            var snappedAngle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
            return new ScreenPoint(start.X + Math.Cos(snappedAngle) * length, start.Y + Math.Sin(snappedAngle) * length);
        }

        return current;
    }

    public static double ClampFontSize(double value)
    {
        return Math.Clamp(value, 8, 96);
    }

    public static double ClampThickness(double value)
    {
        return Math.Clamp(value, 1, 32);
    }

    public static bool IsMeaningful(AnnotationShape shape)
    {
        return shape.Tool switch
        {
            AnnotationTool.Pencil or AnnotationTool.Highlighter => shape.Points.Count > 1,
            AnnotationTool.StepOval => true,
            _ => shape.Start.DistanceTo(shape.End) >= 2.0
        };
    }

    private static ScreenPoint ApplyImageResizeConstraint(ScreenPoint anchor, ScreenPoint point, AnnotationShape shape)
    {
        var width = Math.Max(1, shape.Image?.PixelWidth ?? 1);
        var height = Math.Max(1, shape.Image?.PixelHeight ?? 1);
        var aspect = width / height;
        var dx = point.X - anchor.X;
        var dy = point.Y - anchor.Y;
        if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1)
        {
            return point;
        }

        if (Math.Abs(dx) / aspect >= Math.Abs(dy))
        {
            dy = Math.Sign(dy == 0 ? 1 : dy) * Math.Abs(dx) / aspect;
        }
        else
        {
            dx = Math.Sign(dx == 0 ? 1 : dx) * Math.Abs(dy) * aspect;
        }

        return new ScreenPoint(anchor.X + dx, anchor.Y + dy);
    }
}
