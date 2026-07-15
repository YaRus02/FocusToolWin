using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FocusTool.Win.Models;
using MediaColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace FocusTool.Win.Overlay;

internal sealed class AnnotationRenderer
{
    private readonly AnnotationDocument _annotations;
    private readonly Func<double> _clockProvider;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<ScreenPoint, WpfPoint> _toLocal;
    private readonly Func<ScreenPoint, ScreenPoint, Rect> _toRect;
    private readonly RectSelectionRenderer _rectSelectionRenderer;
    private readonly Action<DrawingContext, Rect> _drawRectHandles;
    private readonly Func<MediaColor, double, SolidColorBrush> _getBrush;
    private readonly Func<MediaColor, double, double, WpfPen> _createPen;
    private readonly Func<string, MediaColor, double, double, double, FormattedText> _getFormattedText;
    private readonly double _handleSize;
    private readonly Dictionary<AnnotationShape, CachedStrokeGeometry> _strokeGeometryCache = [];

    public AnnotationRenderer(
        AnnotationDocument annotations,
        Func<double> clockProvider,
        Func<AppSettings> settingsProvider,
        Func<ScreenPoint, WpfPoint> toLocal,
        Func<ScreenPoint, ScreenPoint, Rect> toRect,
        RectSelectionRenderer rectSelectionRenderer,
        Action<DrawingContext, Rect> drawRectHandles,
        Func<MediaColor, double, SolidColorBrush> getBrush,
        Func<MediaColor, double, double, WpfPen> createPen,
        Func<string, MediaColor, double, double, double, FormattedText> getFormattedText,
        double handleSize)
    {
        _annotations = annotations;
        _clockProvider = clockProvider;
        _settingsProvider = settingsProvider;
        _toLocal = toLocal;
        _toRect = toRect;
        _rectSelectionRenderer = rectSelectionRenderer;
        _drawRectHandles = drawRectHandles;
        _getBrush = getBrush;
        _createPen = createPen;
        _getFormattedText = getFormattedText;
        _handleSize = handleSize;
    }

    public void TrimStrokeGeometryCache()
    {
        if (_strokeGeometryCache.Count == 0)
        {
            return;
        }

        var liveShapes = new HashSet<AnnotationShape>(_annotations.Shapes);
        foreach (var shape in _strokeGeometryCache.Keys.ToArray())
        {
            if (!liveShapes.Contains(shape))
            {
                _strokeGeometryCache.Remove(shape);
            }
        }
    }

    public void ClearStrokeGeometryCache()
    {
        _strokeGeometryCache.Clear();
    }

    public void Draw(DrawingContext drawingContext)
    {
        var nowMs = _clockProvider();
        var stepNumbersByColor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var shape in _annotations.Shapes)
        {
            var opacityScale = shape.GetOpacityScale(nowMs);
            if (opacityScale > 0.001)
            {
                var number = IsStepTool(shape.Tool) ? NextStepNumber(stepNumbersByColor, shape.Color) : (int?)null;
                DrawShape(drawingContext, shape, isDraft: false, opacityScale, number);
            }
        }

        if (_annotations.Draft is { Tool: not AnnotationTool.Move } draft)
        {
            var number = IsStepTool(draft.Tool) ? PeekStepNumber(stepNumbersByColor, draft.Color) : (int?)null;
            DrawShape(drawingContext, draft, isDraft: true, opacityScale: 1, number);
        }

        if (_annotations.SelectionBounds is { } selectionBounds)
        {
            _rectSelectionRenderer.DrawSelectionRectangle(drawingContext, selectionBounds, isDraft: false);
        }

        if (_annotations.ObjectEditShape is { } objectEditShape)
        {
            DrawObjectEditHandles(drawingContext, objectEditShape);
        }

        if (_annotations.SelectionDraftBounds is { } draftBounds)
        {
            _rectSelectionRenderer.DrawSelectionRectangle(drawingContext, draftBounds, isDraft: true);
        }

        DrawEraserPreview(drawingContext);
    }

    private void DrawShape(DrawingContext drawingContext, AnnotationShape shape, bool isDraft, double opacityScale, int? stepNumber = null)
    {
        var opacity = (isDraft ? 0.72 : 0.95) * opacityScale;
        if (opacity <= 0.001)
        {
            return;
        }

        var color = AppSettings.TryParseColor(shape.Color, out var parsedColor)
            ? parsedColor
            : Colors.Red;
        var haloOpacity = (isDraft ? 0.16 : 0.24) * opacityScale;
        var haloPen = _createPen(Colors.Black, haloOpacity, shape.Thickness + 2.2);
        var pen = _createPen(color, opacity, shape.Thickness);

        switch (shape.Tool)
        {
            case AnnotationTool.Arrow:
                DrawArrow(drawingContext, _toLocal(shape.Start), _toLocal(shape.End), color, shape.Thickness, opacity);
                break;
            case AnnotationTool.Rectangle:
                drawingContext.DrawRectangle(null, haloPen, _toRect(shape.Start, shape.End));
                drawingContext.DrawRectangle(null, pen, _toRect(shape.Start, shape.End));
                break;
            case AnnotationTool.Ellipse:
                var ellipseRect = _toRect(shape.Start, shape.End);
                var ellipseCenter = new WpfPoint(
                    ellipseRect.Left + ellipseRect.Width / 2,
                    ellipseRect.Top + ellipseRect.Height / 2);
                drawingContext.DrawEllipse(null, haloPen, ellipseCenter, ellipseRect.Width / 2, ellipseRect.Height / 2);
                drawingContext.DrawEllipse(null, pen, ellipseCenter, ellipseRect.Width / 2, ellipseRect.Height / 2);
                break;
            case AnnotationTool.Line:
                drawingContext.DrawLine(haloPen, _toLocal(shape.Start), _toLocal(shape.End));
                drawingContext.DrawLine(pen, _toLocal(shape.Start), _toLocal(shape.End));
                break;
            case AnnotationTool.Pencil:
                DrawPencil(drawingContext, shape, pen);
                break;
            case AnnotationTool.Highlighter:
                DrawHighlighter(drawingContext, shape, color, isDraft, opacityScale);
                break;
            case AnnotationTool.Text:
                DrawText(drawingContext, shape, Colors.Black, haloOpacity + 0.12 * opacityScale, new Vector(1.2, 1.2));
                DrawText(drawingContext, shape, color, opacity, default);
                break;
            case AnnotationTool.Image:
                DrawImageAnnotation(drawingContext, shape, opacity);
                break;
            case AnnotationTool.StepOval:
                DrawStepOval(drawingContext, shape, color, opacity, stepNumber ?? 1);
                break;
            case AnnotationTool.StepRect:
                DrawStepRect(drawingContext, shape, color, haloPen, pen, opacity, stepNumber ?? 1);
                break;
            case AnnotationTool.Eraser:
            case AnnotationTool.Move:
                break;
        }
    }

    private void DrawEraserPreview(DrawingContext drawingContext)
    {
        if (_annotations.EraserPoint is not { } point)
        {
            return;
        }

        var center = _toLocal(point);
        var radiusX = Math.Abs(_toLocal(point.Offset(AnnotationHitTesting.EraserRadius, 0)).X - center.X);
        var radiusY = Math.Abs(_toLocal(point.Offset(0, AnnotationHitTesting.EraserRadius)).Y - center.Y);
        var hasHover = _annotations.EraserHoverShape is not null;
        var color = hasHover ? Colors.OrangeRed : Colors.White;
        var outline = _createPen(color, hasHover ? 0.84 : 0.42, hasHover ? 1.2 : 1.0);
        drawingContext.DrawEllipse(null, outline, center, Math.Max(3, radiusX), Math.Max(3, radiusY));

        if (_annotations.EraserHoverShape is not { } hoverShape)
        {
            return;
        }

        var bounds = hoverShape.GetBounds();
        var hoverPen = new WpfPen(_getBrush(Colors.OrangeRed, 0.7), 1.2)
        {
            DashStyle = DashStyles.Dash
        };
        hoverPen.Freeze();
        drawingContext.DrawRectangle(
            _getBrush(Colors.OrangeRed, 0.05),
            hoverPen,
            _toRect(new ScreenPoint(bounds.Left, bounds.Top), new ScreenPoint(bounds.Right, bounds.Bottom)));
    }

    private static bool IsStepTool(AnnotationTool tool)
    {
        return tool is AnnotationTool.StepOval or AnnotationTool.StepRect;
    }

    private static int NextStepNumber(Dictionary<string, int> stepNumbersByColor, string color)
    {
        var key = StepColorKey(color);
        var number = stepNumbersByColor.TryGetValue(key, out var current)
            ? current + 1
            : 1;
        stepNumbersByColor[key] = number;
        return number;
    }

    private static int PeekStepNumber(Dictionary<string, int> stepNumbersByColor, string color)
    {
        return stepNumbersByColor.TryGetValue(StepColorKey(color), out var current)
            ? current + 1
            : 1;
    }

    private static string StepColorKey(string color)
    {
        return AppSettings.TryParseColor(color, out var parsed)
            ? parsed.ToString()
            : color.Trim();
    }

    private void DrawObjectEditHandles(DrawingContext drawingContext, AnnotationShape shape)
    {
        if (shape.Tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.StepRect or AnnotationTool.Image)
        {
            _drawRectHandles(drawingContext, _toRect(shape.Start, shape.End));
            return;
        }

        if (shape.Tool is AnnotationTool.Line or AnnotationTool.Arrow)
        {
            DrawPointHandle(drawingContext, _toLocal(shape.Start));
            DrawPointHandle(drawingContext, _toLocal(shape.End));
        }
    }

    private void DrawPointHandle(DrawingContext drawingContext, WpfPoint center)
    {
        var fill = _getBrush(Colors.White, 0.94);
        var pen = _createPen(Colors.Black, 0.72, 1.2);
        drawingContext.DrawEllipse(fill, pen, center, _handleSize / 2, _handleSize / 2);
    }

    private void DrawImageAnnotation(DrawingContext drawingContext, AnnotationShape shape, double opacity)
    {
        if (shape.Image is null)
        {
            return;
        }

        var rect = _toRect(shape.Start, shape.End);
        if (rect.Width < 1 || rect.Height < 1)
        {
            return;
        }

        drawingContext.PushOpacity(opacity);
        drawingContext.DrawImage(shape.Image, rect);
        drawingContext.Pop();
    }

    private void DrawHighlighter(DrawingContext drawingContext, AnnotationShape shape, MediaColor color, bool isDraft, double opacityScale)
    {
        drawingContext.DrawGeometry(
            _getBrush(color, (isDraft ? 0.28 : 0.36) * opacityScale),
            null,
            GetStrokeGeometry(shape));
    }

    private void DrawPencil(DrawingContext drawingContext, AnnotationShape shape, WpfPen pen)
    {
        if (shape.Points.Count == 0)
        {
            return;
        }

        if (shape.Points.Count == 1)
        {
            var center = _toLocal(shape.Points[0]);
            drawingContext.DrawEllipse(pen.Brush, null, center, pen.Thickness / 2, pen.Thickness / 2);
            return;
        }

        drawingContext.DrawGeometry(null, pen, GetStrokeGeometry(shape));
    }

    private Geometry GetStrokeGeometry(AnnotationShape shape)
    {
        var smoothing = _settingsProvider().GetStrokeSmoothingLevel();
        var isDraft = ReferenceEquals(_annotations.Draft, shape);
        if (isDraft)
        {
            return shape.Tool == AnnotationTool.Highlighter
                ? BuildHighlighterGeometry(shape, smoothing, finalize: false)
                : BuildStrokeGeometry(shape, smoothing, finalize: false);
        }

        if (_strokeGeometryCache.TryGetValue(shape, out var cached)
            && cached.Version == shape.GeometryVersion
            && cached.Smoothing == smoothing)
        {
            return cached.Geometry;
        }

        var geometry = shape.Tool == AnnotationTool.Highlighter
            ? BuildHighlighterGeometry(shape, smoothing, finalize: true)
            : BuildStrokeGeometry(shape, smoothing, finalize: true);
        _strokeGeometryCache[shape] = new CachedStrokeGeometry(
            shape.GeometryVersion,
            smoothing,
            geometry);
        return geometry;
    }

    private Geometry BuildStrokeGeometry(AnnotationShape shape, StrokeSmoothingLevel smoothing, bool finalize)
    {
        var points = AnnotationStrokeGeometry.Smooth(shape.Points, smoothing, finalize);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(_toLocal(points[0]), isFilled: false, isClosed: false);
            if (smoothing == StrokeSmoothingLevel.Off || points.Count < 3)
            {
                for (var i = 1; i < points.Count; i++)
                {
                    context.LineTo(_toLocal(points[i]), isStroked: true, isSmoothJoin: true);
                }
            }
            else
            {
                for (var i = 1; i < points.Count - 1; i++)
                {
                    var midpoint = new ScreenPoint(
                        (points[i].X + points[i + 1].X) / 2,
                        (points[i].Y + points[i + 1].Y) / 2);
                    context.QuadraticBezierTo(
                        _toLocal(points[i]),
                        _toLocal(midpoint),
                        isStroked: true,
                        isSmoothJoin: true);
                }

                context.LineTo(_toLocal(points[^1]), isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private Geometry BuildHighlighterGeometry(AnnotationShape shape, StrokeSmoothingLevel smoothing, bool finalize)
    {
        IReadOnlyList<ScreenPoint> centerLine = shape.HighlighterStraightened
            ? [shape.Start, shape.End]
            : AnnotationStrokeGeometry.Smooth(shape.Points, smoothing, finalize);
        var nibHeight = Math.Max(12, shape.Thickness * 4.2);
        var nibWidth = Math.Max(2, shape.Thickness * 0.72);
        var nibGeometry = AnnotationStrokeGeometry.BuildFixedNibGeometry(
            centerLine,
            nibWidth,
            nibHeight);
        if (nibGeometry.IsEmpty)
        {
            return Geometry.Empty;
        }

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            var figureStart = 0;
            foreach (var figureEnd in nibGeometry.FigureEnds)
            {
                if (figureEnd - figureStart < 3)
                {
                    figureStart = figureEnd;
                    continue;
                }

                context.BeginFigure(_toLocal(nibGeometry.Points[figureStart]), isFilled: true, isClosed: true);
                for (var i = figureStart + 1; i < figureEnd; i++)
                {
                    context.LineTo(_toLocal(nibGeometry.Points[i]), isStroked: true, isSmoothJoin: false);
                }

                figureStart = figureEnd;
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private void DrawArrow(DrawingContext drawingContext, WpfPoint start, WpfPoint end, MediaColor color, double thickness, double opacity)
    {
        var vector = start - end;
        if (vector.Length < 4)
        {
            DrawLine(drawingContext, start, end, color, opacity, thickness);
            return;
        }

        vector.Normalize();
        var normal = new Vector(-vector.Y, vector.X);
        var headLength = Math.Max(12, thickness * 4.5);
        var headWidth = Math.Max(7, thickness * 2.8);
        var shaftEnd = end + vector * Math.Max(1, headLength * 0.72);
        var point1 = end + vector * headLength + normal * headWidth;
        var point2 = end + vector * headLength - normal * headWidth;

        drawingContext.DrawLine(_createPen(color, opacity, thickness), start, shaftEnd);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(end, isFilled: true, isClosed: true);
            context.LineTo(point1, isStroked: true, isSmoothJoin: true);
            context.LineTo(point2, isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(_getBrush(color, opacity), null, geometry);
    }

    private void DrawText(DrawingContext drawingContext, AnnotationShape shape, MediaColor color, double opacity, Vector offset)
    {
        var text = shape.Text;
        if (string.IsNullOrEmpty(text))
        {
            text = "|";
        }
        else if (_annotations.Draft == shape || _annotations.IsTextBeingEdited(shape))
        {
            text += "|";
        }

        var formattedText = _getFormattedText(text, color, opacity, shape.FontSize, shape.TextLineHeight);
        drawingContext.DrawText(formattedText, _toLocal(shape.Start) + offset);
    }

    private void DrawStepOval(DrawingContext drawingContext, AnnotationShape shape, MediaColor color, double opacity, int number)
    {
        var center = _toLocal(shape.Start);
        var formattedText = CreateStepFormattedText(number, color, opacity, shape.FontSize);
        var badge = CreateStepBadgeRect(center, shape.FontSize, formattedText.WidthIncludingTrailingWhitespace);
        DrawStepBadge(drawingContext, badge, color, opacity, formattedText);
    }

    private void DrawStepRect(
        DrawingContext drawingContext,
        AnnotationShape shape,
        MediaColor color,
        WpfPen haloPen,
        WpfPen pen,
        double opacity,
        int number)
    {
        var rect = _toRect(shape.Start, shape.End);
        if (rect.Width >= 1 && rect.Height >= 1)
        {
            drawingContext.DrawRectangle(null, haloPen, rect);
            drawingContext.DrawRectangle(null, pen, rect);
        }

        var formattedText = CreateStepFormattedText(number, color, opacity, shape.FontSize);
        var badge = CreateStepBadgeRect(new WpfPoint(rect.Left, rect.Top), shape.FontSize, formattedText.WidthIncludingTrailingWhitespace);
        DrawStepBadge(drawingContext, badge, color, opacity, formattedText);
    }

    private FormattedText CreateStepFormattedText(int number, MediaColor badgeColor, double opacity, double baseFontSize)
    {
        var fontSize = Math.Clamp(baseFontSize * 0.82, 10, 72);
        return _getFormattedText(
            Math.Max(1, number).ToString(CultureInfo.InvariantCulture),
            GetReadableTextColor(badgeColor),
            opacity,
            fontSize,
            fontSize * 1.05);
    }

    private static Rect CreateStepBadgeRect(WpfPoint center, double fontSize, double textWidth)
    {
        var height = Math.Clamp(fontSize * 1.45, 22, 96);
        var width = Math.Max(height, textWidth + fontSize * 0.85);
        return new Rect(center.X - width / 2, center.Y - height / 2, width, height);
    }

    private void DrawStepBadge(
        DrawingContext drawingContext,
        Rect badge,
        MediaColor color,
        double opacity,
        FormattedText formattedText)
    {
        var radius = badge.Height / 2;
        var haloPen = _createPen(Colors.Black, 0.36 * opacity, Math.Max(2, badge.Height * 0.09));
        var edgePen = _createPen(GetReadableTextColor(color), 0.22 * opacity, 1.1);
        drawingContext.DrawRoundedRectangle(_getBrush(color, opacity), haloPen, badge, radius, radius);
        drawingContext.DrawRoundedRectangle(null, edgePen, badge, radius, radius);

        var textPoint = new WpfPoint(
            badge.Left + (badge.Width - formattedText.WidthIncludingTrailingWhitespace) / 2,
            badge.Top + (badge.Height - formattedText.Height) / 2);
        drawingContext.DrawText(formattedText, textPoint);
    }

    private void DrawLine(
        DrawingContext drawingContext,
        WpfPoint start,
        WpfPoint end,
        MediaColor color,
        double opacity,
        double thickness)
    {
        drawingContext.DrawLine(_createPen(color, opacity, thickness), start, end);
    }

    private static MediaColor GetReadableTextColor(MediaColor background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.58 ? Colors.Black : Colors.White;
    }

    private readonly record struct CachedStrokeGeometry(
        int Version,
        StrokeSmoothingLevel Smoothing,
        Geometry Geometry);
}
