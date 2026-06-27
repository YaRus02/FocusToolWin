using System.Windows.Media.Imaging;
using FocusTool.Win.Overlay;

namespace FocusTool.Win.Models;

internal sealed class AnnotationShape
{
    private const double TextLineHeightFactor = 1.12;
    private const double TextAverageGlyphWidthFactor = 0.78;
    private const double StepBadgeHeightFactor = 1.45;
    private const double StepBadgeWidthFactor = 2.2;

    public AnnotationTool Tool { get; set; }
    public ScreenPoint Start { get; set; }
    public ScreenPoint End { get; set; }
    public List<ScreenPoint> Points { get; set; } = [];
    public string Color { get; set; } = "#FFFF2020";
    public double Thickness { get; set; } = 4;
    public string Text { get; set; } = string.Empty;
    public BitmapSource? Image { get; set; }
    public double FontSize { get; set; } = 28;
    public bool IsTemporary { get; set; }
    public double CreatedAtMs { get; set; }
    public int TemporaryVisibleMs { get; set; }
    public int TemporaryFadeMs { get; set; }
    public double TextLineHeight => Math.Max(1, FontSize * TextLineHeightFactor);
    internal int GeometryVersion { get; private set; }

    public AnnotationShape Clone()
    {
        return new AnnotationShape
        {
            Tool = Tool,
            Start = Start,
            End = End,
            Points = [.. Points],
            Color = Color,
            Thickness = Thickness,
            Text = Text,
            Image = Image,
            FontSize = FontSize,
            IsTemporary = IsTemporary,
            CreatedAtMs = CreatedAtMs,
            TemporaryVisibleMs = TemporaryVisibleMs,
            TemporaryFadeMs = TemporaryFadeMs,
            GeometryVersion = GeometryVersion
        };
    }

    public void ApplyFadingSettings(AppSettings settings)
    {
        IsTemporary = settings.FadingAnnotationsEnabled;
        TemporaryVisibleMs = Math.Max(0, settings.FadingAnnotationVisibleMs);
        TemporaryFadeMs = Math.Max(0, settings.FadingAnnotationFadeMs);
    }

    public void MarkCreated(double nowMs)
    {
        CreatedAtMs = nowMs;
    }

    public bool IsExpired(double nowMs)
    {
        if (!IsTemporary)
        {
            return false;
        }

        var expireAtMs = CreatedAtMs + TemporaryVisibleMs + Math.Max(0, TemporaryFadeMs);
        return nowMs >= expireAtMs;
    }

    public bool IsFadeInProgress(double nowMs)
    {
        return IsTemporary
            && TemporaryFadeMs > 0
            && nowMs >= CreatedAtMs + TemporaryVisibleMs
            && !IsExpired(nowMs);
    }

    public double GetOpacityScale(double nowMs)
    {
        if (!IsTemporary)
        {
            return 1;
        }

        var fadeStartMs = CreatedAtMs + TemporaryVisibleMs;
        if (nowMs < fadeStartMs)
        {
            return 1;
        }

        if (TemporaryFadeMs <= 0)
        {
            return 0;
        }

        var progress = Math.Clamp((nowMs - fadeStartMs) / TemporaryFadeMs, 0, 1);
        return 1 - SmoothStep(progress);
    }

    public ScreenRect GetBounds()
    {
        var bounds = Tool switch
        {
            AnnotationTool.Pencil or AnnotationTool.Highlighter when Points.Count > 0 => BoundsFromPoints(Points),
            AnnotationTool.Text => TextBounds(),
            AnnotationTool.Image => ImageBounds(),
            AnnotationTool.StepOval => StepOvalBounds(),
            AnnotationTool.StepRect => StepRectBounds(),
            _ => ScreenRect.FromPoints(Start, End)
        };

        var padding = Tool switch
        {
            AnnotationTool.Highlighter => Math.Max(6, Thickness * 2.1),
            AnnotationTool.Text => 6,
            AnnotationTool.Image => 3,
            AnnotationTool.StepOval or AnnotationTool.StepRect => 4,
            _ => Math.Max(3, Thickness / 2 + 2)
        };

        return bounds.Inflate(padding);
    }

    public bool IntersectsSelection(ScreenRect selection)
    {
        return Tool switch
        {
            AnnotationTool.Line => SegmentIntersectsRect(Start, End, selection.Inflate(StrokePadding())),
            AnnotationTool.Arrow => SegmentIntersectsRect(Start, End, selection.Inflate(Math.Max(6, Thickness * 3))),
            AnnotationTool.Rectangle => RectangleOutlineIntersects(selection),
            AnnotationTool.Ellipse => EllipseOutlineIntersects(selection),
            AnnotationTool.Pencil or AnnotationTool.Highlighter => PolylineIntersects(selection),
            AnnotationTool.StepRect => StepRectIntersects(selection),
            AnnotationTool.Text or AnnotationTool.Image or AnnotationTool.StepOval => GetBounds().Intersects(selection),
            _ => GetBounds().Intersects(selection)
        };
    }

    public void Offset(double dx, double dy)
    {
        Start = Start.Offset(dx, dy);
        End = End.Offset(dx, dy);

        for (var i = 0; i < Points.Count; i++)
        {
            Points[i] = Points[i].Offset(dx, dy);
        }

        GeometryVersion++;
    }

    public void SetEndpoints(ScreenPoint start, ScreenPoint end)
    {
        Start = start;
        End = end;
        GeometryVersion++;
    }

    private ScreenRect TextBounds()
    {
        var lines = Text.Replace("\r\n", "\n").Split('\n');
        var longestLine = Math.Max(1, lines.Max(line => line.Length));
        var width = Math.Max(FontSize * 0.7, longestLine * FontSize * TextAverageGlyphWidthFactor);
        var height = Math.Max(FontSize, lines.Length * TextLineHeight);

        return new ScreenRect(Start.X, Start.Y, Start.X + width, Start.Y + height);
    }

    private ScreenRect ImageBounds()
    {
        var rect = ScreenRect.FromPoints(Start, End);
        if (rect.Width >= 1 && rect.Height >= 1)
        {
            return rect;
        }

        var width = Math.Max(1, Image?.PixelWidth ?? 1);
        var height = Math.Max(1, Image?.PixelHeight ?? 1);
        return new ScreenRect(Start.X, Start.Y, Start.X + width, Start.Y + height);
    }

    private ScreenRect StepOvalBounds()
    {
        var height = StepBadgeHeight(FontSize);
        var width = StepBadgeWidth(FontSize);
        return new ScreenRect(
            Start.X - width / 2,
            Start.Y - height / 2,
            Start.X + width / 2,
            Start.Y + height / 2);
    }

    private ScreenRect StepRectBounds()
    {
        var rect = ScreenRect.FromPoints(Start, End);
        return rect.Union(StepRectBadgeBounds(rect));
    }

    private static double StepBadgeHeight(double fontSize)
    {
        return Math.Clamp(fontSize * StepBadgeHeightFactor, 22, 96);
    }

    private static double StepBadgeWidth(double fontSize)
    {
        return Math.Max(StepBadgeHeight(fontSize), fontSize * StepBadgeWidthFactor);
    }

    private bool PolylineIntersects(ScreenRect selection)
    {
        if (Points.Count == 0)
        {
            return false;
        }

        var hitRect = selection.Inflate(StrokePadding());
        if (Points.Count == 1)
        {
            return hitRect.Contains(Points[0]);
        }

        for (var i = 1; i < Points.Count; i++)
        {
            if (SegmentIntersectsRect(Points[i - 1], Points[i], hitRect))
            {
                return true;
            }
        }

        return false;
    }

    private bool RectangleOutlineIntersects(ScreenRect selection)
    {
        var rect = ScreenRect.FromPoints(Start, End);
        var hitRect = selection.Inflate(StrokePadding());
        var topLeft = new ScreenPoint(rect.Left, rect.Top);
        var topRight = new ScreenPoint(rect.Right, rect.Top);
        var bottomRight = new ScreenPoint(rect.Right, rect.Bottom);
        var bottomLeft = new ScreenPoint(rect.Left, rect.Bottom);

        return SegmentIntersectsRect(topLeft, topRight, hitRect)
            || SegmentIntersectsRect(topRight, bottomRight, hitRect)
            || SegmentIntersectsRect(bottomRight, bottomLeft, hitRect)
            || SegmentIntersectsRect(bottomLeft, topLeft, hitRect);
    }

    private bool StepRectIntersects(ScreenRect selection)
    {
        var rect = ScreenRect.FromPoints(Start, End);
        return RectangleOutlineIntersects(selection)
            || StepRectBadgeBounds(rect).Intersects(selection);
    }

    private ScreenRect StepRectBadgeBounds(ScreenRect rect)
    {
        var height = StepBadgeHeight(FontSize);
        var width = StepBadgeWidth(FontSize);
        return new ScreenRect(
            rect.Left - width / 2,
            rect.Top - height / 2,
            rect.Left + width / 2,
            rect.Top + height / 2);
    }

    private bool EllipseOutlineIntersects(ScreenRect selection)
    {
        var rect = ScreenRect.FromPoints(Start, End);
        if (rect.Width < 0.01 || rect.Height < 0.01)
        {
            return SegmentIntersectsRect(Start, End, selection.Inflate(StrokePadding()));
        }

        var hitRect = selection.Inflate(StrokePadding());
        var centerX = rect.Left + rect.Width / 2;
        var centerY = rect.Top + rect.Height / 2;
        var radiusX = rect.Width / 2;
        var radiusY = rect.Height / 2;
        const int segments = 72;

        var previous = PointOnEllipse(centerX, centerY, radiusX, radiusY, 0);
        for (var i = 1; i <= segments; i++)
        {
            var current = PointOnEllipse(centerX, centerY, radiusX, radiusY, Math.Tau * i / segments);
            if (SegmentIntersectsRect(previous, current, hitRect))
            {
                return true;
            }

            previous = current;
        }

        return false;
    }

    private double StrokePadding()
    {
        return Tool == AnnotationTool.Highlighter
            ? Math.Max(6, Thickness * 2.1)
            : Math.Max(3, Thickness / 2 + 2);
    }

    private static ScreenPoint PointOnEllipse(double centerX, double centerY, double radiusX, double radiusY, double angle)
    {
        return new ScreenPoint(centerX + Math.Cos(angle) * radiusX, centerY + Math.Sin(angle) * radiusY);
    }

    private static bool SegmentIntersectsRect(ScreenPoint start, ScreenPoint end, ScreenRect rect)
    {
        if (rect.Contains(start) || rect.Contains(end))
        {
            return true;
        }

        var topLeft = new ScreenPoint(rect.Left, rect.Top);
        var topRight = new ScreenPoint(rect.Right, rect.Top);
        var bottomRight = new ScreenPoint(rect.Right, rect.Bottom);
        var bottomLeft = new ScreenPoint(rect.Left, rect.Bottom);

        return SegmentsIntersect(start, end, topLeft, topRight)
            || SegmentsIntersect(start, end, topRight, bottomRight)
            || SegmentsIntersect(start, end, bottomRight, bottomLeft)
            || SegmentsIntersect(start, end, bottomLeft, topLeft);
    }

    private static bool SegmentsIntersect(ScreenPoint a, ScreenPoint b, ScreenPoint c, ScreenPoint d)
    {
        var o1 = Orientation(a, b, c);
        var o2 = Orientation(a, b, d);
        var o3 = Orientation(c, d, a);
        var o4 = Orientation(c, d, b);

        if (o1 * o2 < 0 && o3 * o4 < 0)
        {
            return true;
        }

        return Math.Abs(o1) < 0.0001 && IsOnSegment(a, c, b)
            || Math.Abs(o2) < 0.0001 && IsOnSegment(a, d, b)
            || Math.Abs(o3) < 0.0001 && IsOnSegment(c, a, d)
            || Math.Abs(o4) < 0.0001 && IsOnSegment(c, b, d);
    }

    private static double Orientation(ScreenPoint a, ScreenPoint b, ScreenPoint c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private static bool IsOnSegment(ScreenPoint a, ScreenPoint point, ScreenPoint b)
    {
        return point.X >= Math.Min(a.X, b.X) - 0.0001
            && point.X <= Math.Max(a.X, b.X) + 0.0001
            && point.Y >= Math.Min(a.Y, b.Y) - 0.0001
            && point.Y <= Math.Max(a.Y, b.Y) + 0.0001;
    }

    private static double SmoothStep(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static ScreenRect BoundsFromPoints(IReadOnlyList<ScreenPoint> points)
    {
        var left = points[0].X;
        var top = points[0].Y;
        var right = points[0].X;
        var bottom = points[0].Y;

        for (var i = 1; i < points.Count; i++)
        {
            left = Math.Min(left, points[i].X);
            top = Math.Min(top, points[i].Y);
            right = Math.Max(right, points[i].X);
            bottom = Math.Max(bottom, points[i].Y);
        }

        return new ScreenRect(left, top, right, bottom);
    }
}
