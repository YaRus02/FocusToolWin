using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FocusTool.Win.Models;
using MediaColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace FocusTool.Win.Overlay;

internal sealed class OverlaySurface : FrameworkElement
{
    // Laser comet (ported from LaserMarker's TrailElement): the trail is drawn as
    // contiguous smooth bezier strokes split into age bands, and each sample's
    // "life" is derived from its real timestamp against a window that shrinks as
    // the cursor sits still - so the tail recedes into the head and stale points
    // fall out of the window instead of snapping back as phantom lines.
    private const int LaserCoreBands = 18;      // fine bands -> smooth colored-core taper
    private const int LaserGlowBands = 5;       // few coarse bands -> one soft halo, not a bead chain
    private const double LaserGraceMs = 50;     // treated as "still moving" for this long after the last sample
    private const double LaserMaxGapMs = 250;   // a time gap larger than this breaks the ribbon
    private const double LaserMinSpacingPx = 0.4;

    // Speed adaptation: a fast flick reads thinner (a sleek comet) and is naturally
    // longer in space (same time window, more pixels covered); a slow drag is
    // fuller. The blend is eased between frames so width glides instead of
    // jittering. The time window itself stays fixed at TrailLengthMs - expanding it
    // with speed would resurrect already-faded points (phantom lines), so we don't.
    // Distances are physical pixels.
    private const double LaserSpeedWindowMs = 120;
    private const double LaserSlowSpeedPxPerMs = 0.16;
    private const double LaserFastSpeedPxPerMs = 1.4;
    private const double LaserSlowThicknessFactor = 1.12;
    private const double LaserFastThicknessFactor = 0.8;
    private const double LaserSpeedEasing = 0.2;
    private const double RegionMaskHandleSize = 8;
    private const double CursorPulseDurationMs = 360;
    private const double CursorPulseExtraRadius = 26;
    private readonly TrailModel _trailModel;
    private readonly AnnotationDocument _annotations;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<InteractionMode> _modeProvider;
    private readonly Func<double> _clockProvider;
    private readonly Func<ScreenPoint?> _spotlightProvider;
    private readonly Func<CursorHighlightFrame> _cursorHighlightProvider;
    private readonly Func<ScreenBoardFrame?> _screenBoardProvider;
    private readonly Func<RectOverlayVisual?> _rectOverlayProvider;
    private readonly Func<IReadOnlyList<RegionMask>> _regionMaskProvider;
    private readonly Func<int> _regionMaskSelectionProvider;
    private readonly Func<IReadOnlyList<ScreenRect>> _spotlightRegionProvider;
    private readonly Func<int> _spotlightRegionSelectionProvider;
    private readonly ScreenRect _screenBounds;
    // Cached frozen polyline geometry per pencil/highlighter shape, so we don't
    // re-tessellate committed strokes on every laser frame. Cleared whenever the
    // annotations change (add/move/undo); rebuilt lazily on the next render.
    private readonly Dictionary<AnnotationShape, CachedStrokeGeometry> _strokeGeometryCache = [];
    private bool _annotationInputEnabled;
    private double _speedBlend;
    // Single-slot cache for the spotlight dim mask: the Exclude boolean op is
    // expensive, so reuse it while size/centre/radius are unchanged (stationary
    // cursor, fade frames, other elements animating).
    private Geometry? _spotlightDimGeometry;
    private (double Width, double Height, double X, double Y, double Radius) _spotlightDimKey;
    private Geometry? _regionSpotlightDimGeometry;
    private string? _regionSpotlightDimKey;
    // Reusable scratch buffers for the laser pipeline, cleared and refilled every
    // frame instead of allocating fresh Lists. UI-thread only and never re-entrant
    // (OnRender is synchronous), so the geometry/visual output is byte-identical to
    // the allocate-per-frame version - only the GC churn is removed.
    private readonly List<List<(WpfPoint Point, double Life)>> _laserRuns = [];
    private readonly Stack<List<(WpfPoint Point, double Life)>> _laserRunPool = new();
    private readonly List<(WpfPoint Point, double Life)> _smoothScratch = [];
    private readonly List<WpfPoint> _bandStroke = [];
    // Per-frame projection/life buffers, grown on demand and reused across frames
    // (the head index is the live count, not the buffer length).
    private WpfPoint[] _laserLocalScratch = [];
    private double[] _laserLifeScratch = [];
    private readonly RegionMaskRenderer _regionMaskRenderer;
    private readonly RectSelectionRenderer _rectSelectionRenderer;

    public OverlaySurface(
        TrailModel trailModel,
        AnnotationDocument annotations,
        Func<AppSettings> settingsProvider,
        Func<InteractionMode> modeProvider,
        Func<double> clockProvider,
        Func<ScreenPoint?> spotlightProvider,
        Func<CursorHighlightFrame> cursorHighlightProvider,
        Func<ScreenBoardFrame?> screenBoardProvider,
        Func<RectOverlayVisual?> rectOverlayProvider,
        Func<IReadOnlyList<RegionMask>> regionMaskProvider,
        Func<int> regionMaskSelectionProvider,
        Func<IReadOnlyList<ScreenRect>> spotlightRegionProvider,
        Func<int> spotlightRegionSelectionProvider,
        ScreenRect screenBounds)
    {
        _trailModel = trailModel;
        _annotations = annotations;
        _settingsProvider = settingsProvider;
        _modeProvider = modeProvider;
        _clockProvider = clockProvider;
        _spotlightProvider = spotlightProvider;
        _cursorHighlightProvider = cursorHighlightProvider;
        _screenBoardProvider = screenBoardProvider;
        _rectOverlayProvider = rectOverlayProvider;
        _regionMaskProvider = regionMaskProvider;
        _regionMaskSelectionProvider = regionMaskSelectionProvider;
        _spotlightRegionProvider = spotlightRegionProvider;
        _spotlightRegionSelectionProvider = spotlightRegionSelectionProvider;
        _screenBounds = screenBounds;
        _regionMaskRenderer = new RegionMaskRenderer(
            ToRect,
            DrawRectHandles,
            GetBrush,
            CreatePen,
            GetReadableTextColor,
            GetFormattedText);
        _rectSelectionRenderer = new RectSelectionRenderer(
            ToRect,
            DrawRectHandles,
            GetBrush,
            CreatePen,
            GetFormattedText,
            SelectionDashPen);
        _annotations.Changed += OnAnnotationsChanged;
        SnapsToDevicePixels = false;
        Focusable = false;
        IsHitTestVisible = false;
    }

    /// <summary>Detach event handlers so the surface can be garbage collected.</summary>
    public void Detach()
    {
        _annotations.Changed -= OnAnnotationsChanged;
        _strokeGeometryCache.Clear();
    }

    private void OnAnnotationsChanged(object? sender, EventArgs e)
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

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        // A resize or DPI change alters the screen->local projection, so cached
        // local-space geometry is no longer valid.
        base.OnRenderSizeChanged(sizeInfo);
        _strokeGeometryCache.Clear();
    }

    public void SetAnnotationInputEnabled(bool enabled)
    {
        _annotationInputEnabled = enabled;
        Focusable = enabled;
        IsHitTestVisible = enabled;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        return _annotationInputEnabled
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : base.HitTestCore(hitTestParameters);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        // ToLocal/PointFromScreen require a live PresentationSource; during teardown
        // or a source/DPI transition it returns NaN, which throws inside WPF
        // geometry/text draw calls. Skip the frame rather than feed NaN downstream.
        if (PresentationSource.FromVisual(this) is null)
        {
            return;
        }

        var mode = _modeProvider();
        var settings = _settingsProvider();
        var lensPoint = _spotlightProvider();
        var blankScreen = IsBlankScreenMode(mode);
        var magnifierActive = settings.MagnifierEnabled && !blankScreen && lensPoint is not null;
        if (mode == InteractionMode.ScreenBoard)
        {
            DrawScreenBoard(drawingContext, _screenBoardProvider());
        }

        if (blankScreen)
        {
            DrawBlankScreen(drawingContext, mode);
        }

        if (mode is InteractionMode.Annotate
            or InteractionMode.PinnedLensSelect
            or InteractionMode.RegionMaskSelect
            or InteractionMode.ScreenshotRegionSelect
            or InteractionMode.RegionSpotlightSelect)
        {
            DrawInputCatcher(drawingContext);
        }

        if (!blankScreen)
        {
            DrawRegionMasks(drawingContext);
        }

        DrawAnnotations(drawingContext);
        DrawCursorHighlight(drawingContext, _cursorHighlightProvider());
        DrawLaserTrail(drawingContext);
        if (!blankScreen)
        {
            DrawRegionSpotlights(drawingContext);
        }

        if (!blankScreen || magnifierActive)
        {
            DrawSpotlight(drawingContext, lensPoint, magnifierActive);
        }

        if (mode is InteractionMode.PinnedLensSelect
            or InteractionMode.RegionMaskSelect
            or InteractionMode.ScreenshotRegionSelect
            or InteractionMode.RegionSpotlightSelect)
        {
            DrawRectSelection(drawingContext);
        }

        if (IsAnnotationMode(mode))
        {
            DrawAnnotateBorder(drawingContext);
        }
    }

    private void DrawInputCatcher(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(GetBrush(MediaColor.FromArgb(1, 0, 0, 0), 1), null, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    private void DrawBlankScreen(DrawingContext drawingContext, InteractionMode mode)
    {
        var color = mode == InteractionMode.WhiteScreen ? Colors.White : Colors.Black;
        drawingContext.DrawRectangle(GetBrush(color, 1), null, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    private void DrawScreenBoard(DrawingContext drawingContext, ScreenBoardFrame? frame)
    {
        if (frame is not null && frame.Bounds.Intersects(_screenBounds))
        {
            drawingContext.DrawImage(frame.Image, new Rect(0, 0, ActualWidth, ActualHeight));
            return;
        }

        drawingContext.DrawRectangle(GetBrush(Colors.Black, 1), null, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    private static bool IsAnnotationMode(InteractionMode mode)
    {
        return mode is InteractionMode.Annotate or InteractionMode.ScreenBoard or InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
    }

    private static bool IsBlankScreenMode(InteractionMode mode)
    {
        return mode is InteractionMode.BlackScreen or InteractionMode.WhiteScreen;
    }

    private void DrawAnnotations(DrawingContext drawingContext)
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
        var haloPen = CreatePen(Colors.Black, haloOpacity, shape.Thickness + 2.2);
        var pen = CreatePen(color, opacity, shape.Thickness);

        switch (shape.Tool)
        {
            case AnnotationTool.Arrow:
                DrawArrow(drawingContext, ToLocal(shape.Start), ToLocal(shape.End), color, shape.Thickness, opacity);
                break;
            case AnnotationTool.Rectangle:
                drawingContext.DrawRectangle(null, haloPen, ToRect(shape.Start, shape.End));
                drawingContext.DrawRectangle(null, pen, ToRect(shape.Start, shape.End));
                break;
            case AnnotationTool.Ellipse:
                var ellipseRect = ToRect(shape.Start, shape.End);
                var ellipseCenter = new WpfPoint(
                    ellipseRect.Left + ellipseRect.Width / 2,
                    ellipseRect.Top + ellipseRect.Height / 2);
                drawingContext.DrawEllipse(null, haloPen, ellipseCenter, ellipseRect.Width / 2, ellipseRect.Height / 2);
                drawingContext.DrawEllipse(null, pen, ellipseCenter, ellipseRect.Width / 2, ellipseRect.Height / 2);
                break;
            case AnnotationTool.Line:
                drawingContext.DrawLine(haloPen, ToLocal(shape.Start), ToLocal(shape.End));
                drawingContext.DrawLine(pen, ToLocal(shape.Start), ToLocal(shape.End));
                break;
            case AnnotationTool.Pencil:
                DrawPencil(drawingContext, shape, haloPen);
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
            case AnnotationTool.Move:
                break;
        }
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

    // The dashed selection outline is identical every frame, so build it once and
    // reuse the frozen pen instead of allocating brush + pen + DashStyle per draw.
    private static readonly WpfPen SelectionDashPen = CreateSelectionDashPen();

    private static WpfPen CreateSelectionDashPen()
    {
        var brush = new SolidColorBrush(Colors.White) { Opacity = 0.98 };
        brush.Freeze();
        var pen = new WpfPen(brush, 1.2)
        {
            DashStyle = new DashStyle([4, 3], 0),
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat,
            LineJoin = PenLineJoin.Miter
        };
        pen.Freeze();
        return pen;
    }

    private void DrawRectSelection(DrawingContext drawingContext)
    {
        if (_rectOverlayProvider() is not { } visual)
        {
            return;
        }

        _rectSelectionRenderer.Draw(
            drawingContext,
            visual,
            _screenBounds,
            ActualWidth,
            ActualHeight);
    }

    private void DrawRegionMasks(DrawingContext drawingContext)
    {
        var showHandles = _modeProvider() == InteractionMode.RegionMaskSelect;
        _regionMaskRenderer.Draw(
            drawingContext,
            _regionMaskProvider(),
            showHandles,
            showHandles ? _regionMaskSelectionProvider() : -1,
            _screenBounds,
            ActualWidth,
            ActualHeight);
    }

    private static void DrawRectHandles(DrawingContext drawingContext, Rect rect)
    {
        if (rect.Width < 1 || rect.Height < 1)
        {
            return;
        }

        var fill = GetBrush(Colors.White, 0.92);
        var pen = CreatePen(Colors.Black, 0.68, 1.2);
        drawingContext.DrawRectangle(fill, pen, CenteredRect(new WpfPoint(rect.Left, rect.Top), RegionMaskHandleSize));
        drawingContext.DrawRectangle(fill, pen, CenteredRect(new WpfPoint(rect.Right, rect.Top), RegionMaskHandleSize));
        drawingContext.DrawRectangle(fill, pen, CenteredRect(new WpfPoint(rect.Left, rect.Bottom), RegionMaskHandleSize));
        drawingContext.DrawRectangle(fill, pen, CenteredRect(new WpfPoint(rect.Right, rect.Bottom), RegionMaskHandleSize));
    }

    private void DrawObjectEditHandles(DrawingContext drawingContext, AnnotationShape shape)
    {
        if (shape.Tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.StepRect or AnnotationTool.Image)
        {
            DrawRectHandles(drawingContext, ToRect(shape.Start, shape.End));
            return;
        }

        if (shape.Tool is AnnotationTool.Line or AnnotationTool.Arrow)
        {
            DrawPointHandle(drawingContext, ToLocal(shape.Start));
            DrawPointHandle(drawingContext, ToLocal(shape.End));
        }
    }

    private static void DrawPointHandle(DrawingContext drawingContext, WpfPoint center)
    {
        var fill = GetBrush(Colors.White, 0.94);
        var pen = CreatePen(Colors.Black, 0.72, 1.2);
        drawingContext.DrawEllipse(fill, pen, center, RegionMaskHandleSize / 2, RegionMaskHandleSize / 2);
    }

    private void DrawImageAnnotation(DrawingContext drawingContext, AnnotationShape shape, double opacity)
    {
        if (shape.Image is null)
        {
            return;
        }

        var rect = ToRect(shape.Start, shape.End);
        if (rect.Width < 1 || rect.Height < 1)
        {
            return;
        }

        drawingContext.PushOpacity(opacity);
        drawingContext.DrawImage(shape.Image, rect);
        drawingContext.Pop();
    }

    private static Rect CenteredRect(WpfPoint center, double size)
    {
        var half = size / 2;
        return new Rect(center.X - half, center.Y - half, size, size);
    }

    private void DrawHighlighter(DrawingContext drawingContext, AnnotationShape shape, MediaColor color, bool isDraft, double opacityScale)
    {
        if (shape.Points.Count < 2)
        {
            return;
        }

        var brush = new SolidColorBrush(color) { Opacity = (isDraft ? 0.28 : 0.36) * opacityScale };
        brush.Freeze();
        var pen = new WpfPen(brush, Math.Max(12, shape.Thickness * 4.2))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        DrawPencil(drawingContext, shape, pen);
    }

    private void DrawPencil(DrawingContext drawingContext, AnnotationShape shape, WpfPen pen)
    {
        if (shape.Points.Count < 2)
        {
            return;
        }

        drawingContext.DrawGeometry(null, pen, GetStrokeGeometry(shape));
    }

    private Geometry GetStrokeGeometry(AnnotationShape shape)
    {
        if (ReferenceEquals(_annotations.Draft, shape))
        {
            return BuildStrokeGeometry(shape);
        }

        if (_strokeGeometryCache.TryGetValue(shape, out var cached)
            && cached.Version == shape.GeometryVersion)
        {
            return cached.Geometry;
        }

        var geometry = BuildStrokeGeometry(shape);
        _strokeGeometryCache[shape] = new CachedStrokeGeometry(
            shape.GeometryVersion,
            geometry);
        return geometry;
    }

    private Geometry BuildStrokeGeometry(AnnotationShape shape)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToLocal(shape.Points[0]), isFilled: false, isClosed: false);
            for (var i = 1; i < shape.Points.Count; i++)
            {
                context.LineTo(ToLocal(shape.Points[i]), isStroked: true, isSmoothJoin: true);
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

        var penBrush = new SolidColorBrush(color) { Opacity = Math.Clamp(opacity, 0, 1) };
        penBrush.Freeze();
        var shaftPen = new WpfPen(penBrush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Flat,
            LineJoin = PenLineJoin.Round
        };
        shaftPen.Freeze();
        drawingContext.DrawLine(shaftPen, start, shaftEnd);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(end, isFilled: true, isClosed: true);
            context.LineTo(point1, isStroked: true, isSmoothJoin: true);
            context.LineTo(point2, isStroked: true, isSmoothJoin: true);
        }

        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        geometry.Freeze();
        drawingContext.DrawGeometry(brush, null, geometry);
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

        var formattedText = GetFormattedText(text, color, opacity, shape.FontSize, shape.TextLineHeight);
        drawingContext.DrawText(formattedText, ToLocal(shape.Start) + offset);
    }

    private void DrawStepOval(DrawingContext drawingContext, AnnotationShape shape, MediaColor color, double opacity, int number)
    {
        var center = ToLocal(shape.Start);
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
        var rect = ToRect(shape.Start, shape.End);
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
        return GetFormattedText(
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

    private static void DrawStepBadge(
        DrawingContext drawingContext,
        Rect badge,
        MediaColor color,
        double opacity,
        FormattedText formattedText)
    {
        var radius = badge.Height / 2;
        var haloPen = CreatePen(Colors.Black, 0.36 * opacity, Math.Max(2, badge.Height * 0.09));
        var edgePen = CreatePen(GetReadableTextColor(color), 0.22 * opacity, 1.1);
        drawingContext.DrawRoundedRectangle(GetBrush(color, opacity), haloPen, badge, radius, radius);
        drawingContext.DrawRoundedRectangle(null, edgePen, badge, radius, radius);

        var textPoint = new WpfPoint(
            badge.Left + (badge.Width - formattedText.WidthIncludingTrailingWhitespace) / 2,
            badge.Top + (badge.Height - formattedText.Height) / 2);
        drawingContext.DrawText(formattedText, textPoint);
    }

    private static MediaColor GetReadableTextColor(MediaColor background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.58 ? Colors.Black : Colors.White;
    }

    private void DrawCursorHighlight(DrawingContext drawingContext, CursorHighlightFrame frame)
    {
        if (frame.Cursor is null && frame.Pulses.Count == 0)
        {
            return;
        }

        var settings = _settingsProvider();
        var color = AppSettings.TryParseColor(settings.CursorHighlightColor, out var parsedColor)
            ? parsedColor
            : MediaColor.FromArgb(0xBF, 0xFF, 0xD4, 0x00);
        var radius = settings.CursorHighlightRadius;
        var thickness = settings.CursorHighlightThickness;

        if (frame.Cursor is { } cursor
            && CursorEffectTouchesScreen(cursor, radius + CursorPulseExtraRadius))
        {
            DrawCursorHighlightCore(
                drawingContext,
                ToLocal(cursor),
                color,
                radius,
                thickness);
        }

        if (frame.Pulses.Count == 0)
        {
            return;
        }

        var nowMs = _clockProvider();
        foreach (var pulse in frame.Pulses)
        {
            var progress = Math.Clamp((nowMs - pulse.StartedAtMs) / CursorPulseDurationMs, 0, 1);
            if (progress >= 1 || !CursorEffectTouchesScreen(pulse.Point, radius + CursorPulseExtraRadius + 4))
            {
                continue;
            }

            DrawCursorClickPulse(
                drawingContext,
                pulse,
                color,
                radius,
                thickness,
                progress);
        }
    }

    private void DrawCursorHighlightCore(
        DrawingContext drawingContext,
        WpfPoint center,
        MediaColor color,
        double radius,
        double thickness)
    {
        var innerRadius = Math.Max(1, radius - thickness * 1.45);
        DrawRadialGlow(drawingContext, center, color, 0.24, radius * 1.55);
        drawingContext.DrawEllipse(GetBrush(color, 0.052), null, center, radius * 0.9, radius * 0.9);
        drawingContext.DrawEllipse(null, CreatePen(Colors.Black, 0.30, thickness + 2.4), center, radius, radius);
        drawingContext.DrawEllipse(null, CreatePen(color, 0.95, thickness), center, radius, radius);
        drawingContext.DrawEllipse(null, CreatePen(Colors.White, 0.16, 1.0), center, innerRadius, innerRadius);
    }

    private void DrawCursorClickPulse(
        DrawingContext drawingContext,
        CursorClickPulse pulse,
        MediaColor highlightColor,
        double baseRadius,
        double thickness,
        double progress)
    {
        var eased = EaseOutCubic(progress);
        var fade = Math.Pow(1 - progress, 1.65);
        var radius = baseRadius * 0.82 + eased * (CursorPulseExtraRadius + baseRadius * 0.14);
        var color = pulse.Button == CursorClickButton.Right
            ? MediaColor.FromArgb(highlightColor.A, 0x66, 0xCC, 0xFF)
            : highlightColor;
        var center = ToLocal(pulse.Point);
        var line = Math.Max(1.3, thickness * (0.78 + 0.25 * (1 - progress)));

        DrawRadialGlow(drawingContext, center, color, 0.14 * fade, radius + 7);
        drawingContext.DrawEllipse(null, CreatePen(Colors.Black, 0.18 * fade, line + 1.4), center, radius, radius);
        drawingContext.DrawEllipse(null, CreatePen(color, 0.74 * fade, line), center, radius, radius);
    }

    private bool CursorEffectTouchesScreen(ScreenPoint point, double radius)
    {
        return new ScreenRect(
            point.X - radius,
            point.Y - radius,
            point.X + radius,
            point.Y + radius).Intersects(_screenBounds);
    }

    private static double EaseOutCubic(double value)
    {
        var inverse = 1 - Math.Clamp(value, 0, 1);
        return 1 - inverse * inverse * inverse;
    }

    // FormattedText glyph layout is one of the most expensive WPF objects to build;
    // committed text shapes have stable content, so cache by the inputs that affect
    // layout/appearance and reuse across frames. The draft (text + caret) changes
    // each keystroke, so it inserts a short-lived entry every keystroke; the 512-entry
    // cap clears the cache before it can grow unbounded. UI-thread only.
    private static readonly Dictionary<string, FormattedText> TextCache = [];

    private FormattedText GetFormattedText(string text, MediaColor color, double opacity, double fontSize, double lineHeight)
    {
        var alpha = ToAlpha(opacity * color.A / 255.0);
        var argb = ((uint)alpha << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var key = string.Concat(
            pixelsPerDip.ToString(CultureInfo.InvariantCulture), "|",
            fontSize.ToString(CultureInfo.InvariantCulture), "|",
            lineHeight.ToString(CultureInfo.InvariantCulture), "|",
            argb.ToString(CultureInfo.InvariantCulture), "|",
            text);

        if (!TextCache.TryGetValue(key, out var formattedText))
        {
            if (TextCache.Count > 512)
            {
                TextCache.Clear();
            }

            formattedText = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                fontSize,
                GetBrush(color, opacity),
                pixelsPerDip)
            {
                LineHeight = lineHeight
            };
            TextCache[key] = formattedText;
        }

        return formattedText;
    }

    // ------------------------------------------------------------------ laser ----
    private void DrawLaserTrail(DrawingContext drawingContext)
    {
        var points = _trailModel.Points;
        if (points.Count == 0 || _trailModel.LastMovementMs < 0)
        {
            return;
        }

        var settings = _settingsProvider();
        var now = _clockProvider();
        var fadeMs = Math.Max(1, settings.FadeDurationMs);
        var dot = settings.PointSize;

        // Skip monitors the trail doesn't touch - on a multi-monitor setup only one
        // surface should run the full projection/smoothing/geometry pipeline.
        if (!TrailTouchesScreen(points, dot))
        {
            return;
        }

        var color = settings.ToMediaColor();

        // Ease the speed blend toward the recent average speed so width changes
        // smoothly. Faster -> thinner, slower -> fuller. The time window is fixed.
        var targetBlend = EstimateSpeedBlend(points, now);
        _speedBlend += (targetBlend - _speedBlend) * LaserSpeedEasing;
        var thicknessScale = Lerp(LaserSlowThicknessFactor, LaserFastThicknessFactor, _speedBlend);
        var trailMs = Math.Max(1, settings.TrailLengthMs);

        // While the head is fresh the comet is full length. Once it stops being
        // refreshed the visible window shrinks to zero over the fade duration: the
        // tail recedes into the head and the whole thing dims out together.
        var stationary = now - _trailModel.LastMovementMs;
        double window, globalDim;
        if (stationary <= LaserGraceMs)
        {
            window = trailMs;
            globalDim = 1.0;
        }
        else
        {
            var p = Math.Clamp((stationary - LaserGraceMs) / fadeMs, 0, 1);
            window = trailMs * (1 - p);
            globalDim = 1 - p;
            if (window < 1 || globalDim <= 0.003)
            {
                return;
            }
        }

        // Project each sample and compute its life (1 = head, 0 = window edge) from
        // its real timestamp - stale points end up with life <= 0 and drop out.
        var count = points.Count;
        if (_laserLocalScratch.Length < count)
        {
            _laserLocalScratch = new WpfPoint[count];
            _laserLifeScratch = new double[count];
        }

        var local = _laserLocalScratch;
        var life = _laserLifeScratch;
        for (var i = 0; i < count; i++)
        {
            local[i] = ToLocal(points[i]);
            life[i] = 1.0 - (now - points[i].TimeMs) / window;
        }

        var headDirection = EstimateHeadDirection(local, count);
        var pulse = 1 + 0.30 * _speedBlend;   // brighter core/head on fast flicks
        var colorOpacity = color.A / 255.0;
        DrawLaserBands(drawingContext, points, local, life, globalDim, color, colorOpacity, dot, thicknessScale, pulse, settings.GlowEnabled);
        DrawLaserHead(drawingContext, local[count - 1], globalDim, color, colorOpacity, dot, _speedBlend, pulse, headDirection, settings.GlowEnabled);
    }

    private void DrawLaserBands(
        DrawingContext drawingContext,
        IReadOnlyList<TrailPoint> points,
        WpfPoint[] local,
        double[] life,
        double globalDim,
        MediaColor color,
        double colorOpacity,
        double dot,
        double thicknessScale,
        double pulse,
        bool glow)
    {
        if (points.Count < 2)
        {
            return;
        }

        BuildLaserRunsInto(points, local, life);
        if (_laserRuns.Count == 0)
        {
            return;
        }

        try
        {
            for (var i = 0; i < _laserRuns.Count; i++)
            {
                SmoothLaserRunInPlace(_laserRuns[i]);
            }

            // Soft glow underlay first, in few coarse bands so it reads as one
            // continuous halo instead of a glowing bead at every band junction.
            if (glow)
            {
                for (var band = 0; band < LaserGlowBands; band++)
                {
                    var bandLife = (band + 0.5) / LaserGlowBands;
                    var shaped = Math.Pow(bandLife, 0.7);
                    var thickness = Math.Max(0.8, dot * (0.12 + shaped * 0.52) * thicknessScale);

                    foreach (var run in _laserRuns)
                    {
                        var stroke = ClipRunToBand(run, (double)band / LaserGlowBands, (double)(band + 1) / LaserGlowBands);
                        if (stroke.Count < 2)
                        {
                            continue;
                        }

                        DrawSmoothLaserStroke(drawingContext, stroke, color, shaped * globalDim * 0.10, thickness * 4.0);
                        DrawSmoothLaserStroke(drawingContext, stroke, color, shaped * globalDim * 0.16, thickness * 2.1);
                    }
                }
            }

            // Crisp colored core on top, in many fine bands for a smooth taper, plus a
            // hot white-ish centre that concentrates toward the head (laser-beam look).
            for (var band = 0; band < LaserCoreBands; band++)
            {
                var bandLife = (band + 0.5) / LaserCoreBands;
                var shaped = Math.Pow(bandLife, 0.7);   // softer-than-linear taper
                var opacity = shaped * globalDim;
                if (opacity <= 0.01)
                {
                    continue;
                }

                var thickness = Math.Max(0.8, dot * (0.12 + shaped * 0.52) * thicknessScale);
                var hotOpacity = globalDim * shaped * shaped * 0.45 * pulse * colorOpacity;
                var hotThickness = Math.Max(0.8, thickness * 0.34);

                foreach (var run in _laserRuns)
                {
                    var stroke = ClipRunToBand(run, (double)band / LaserCoreBands, (double)(band + 1) / LaserCoreBands);
                    if (stroke.Count < 2)
                    {
                        continue;
                    }

                    DrawSmoothLaserStroke(drawingContext, stroke, color, opacity * 0.90 * pulse, thickness);
                    if (hotOpacity > 0.01)
                    {
                        DrawSmoothLaserStroke(drawingContext, stroke, Colors.White, hotOpacity, hotThickness);
                    }
                }
            }
        }
        finally
        {
            ReturnLaserRuns();
        }
    }

    /// <summary>
    /// Builds contiguous (point, life) runs into the reusable <see cref="_laserRuns"/>
    /// buffer, split only on large time gaps. Inner lists are rented from the pool
    /// and returned by <see cref="ReturnLaserRuns"/> after the frame is drawn.
    /// </summary>
    private void BuildLaserRunsInto(IReadOnlyList<TrailPoint> points, WpfPoint[] local, double[] life)
    {
        ReturnLaserRuns();
        var current = RentRunList();
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0 && points[i].TimeMs - points[i - 1].TimeMs > LaserMaxGapMs)
            {
                if (current.Count >= 2)
                {
                    _laserRuns.Add(current);
                    current = RentRunList();
                }
                else
                {
                    // Discarded sub-2-point segment: reuse the same buffer.
                    current.Clear();
                }
            }

            current.Add((local[i], life[i]));
        }

        if (current.Count >= 2)
        {
            _laserRuns.Add(current);
        }
        else
        {
            ReturnRunList(current);
        }
    }

    private List<(WpfPoint Point, double Life)> RentRunList()
    {
        if (_laserRunPool.Count > 0)
        {
            var list = _laserRunPool.Pop();
            list.Clear();
            return list;
        }

        return [];
    }

    private void ReturnRunList(List<(WpfPoint Point, double Life)> list)
    {
        _laserRunPool.Push(list);
    }

    private void ReturnLaserRuns()
    {
        foreach (var run in _laserRuns)
        {
            _laserRunPool.Push(run);
        }

        _laserRuns.Clear();
    }

    /// <summary>
    /// Clip a run to the life range (lo, hi], interpolating exactly at the band
    /// edges so neighbouring bands share endpoints (continuous ribbon, no seams).
    /// Life increases monotonically along the run.
    /// </summary>
    private List<WpfPoint> ClipRunToBand(List<(WpfPoint Point, double Life)> run, double lo, double hi)
    {
        var stroke = _bandStroke;
        stroke.Clear();
        for (var k = 1; k < run.Count; k++)
        {
            var (pa, la) = run[k - 1];
            var (pb, lb) = run[k];
            var segLo = Math.Min(la, lb);
            var segHi = Math.Max(la, lb);
            var enter = Math.Max(lo, segLo);
            var exit = Math.Min(hi, segHi);
            if (enter > exit)
            {
                if (stroke.Count > 0)
                {
                    break; // monotonic: we've passed the band
                }

                continue;
            }

            AddStrokePoint(stroke, LerpByLife(pa, la, pb, lb, enter));
            AddStrokePoint(stroke, LerpByLife(pa, la, pb, lb, exit));
        }

        return stroke;
    }

    private static WpfPoint LerpByLife(WpfPoint pa, double la, WpfPoint pb, double lb, double target)
    {
        var denom = lb - la;
        var t = Math.Abs(denom) < 1e-9 ? 1 : Math.Clamp((target - la) / denom, 0, 1);
        return new WpfPoint(pa.X + (pb.X - pa.X) * t, pa.Y + (pb.Y - pa.Y) * t);
    }

    private static void AddStrokePoint(List<WpfPoint> stroke, WpfPoint point)
    {
        if (stroke.Count == 0 || Distance(stroke[^1], point) >= LaserMinSpacingPx)
        {
            stroke.Add(point);
        }
    }

    /// <summary>
    /// Corner-cutting (Chaikin) smoothing so fast, jerky polylines round into
    /// natural curves before they become beziers. Life is interpolated alongside
    /// position and stays monotonic, so band clipping still works.
    /// </summary>
    private void SmoothLaserRunInPlace(List<(WpfPoint Point, double Life)> run)
    {
        if (run.Count < 3)
        {
            return;
        }

        var passes = run.Count > 90 ? 1 : 2;
        var scratch = _smoothScratch;
        for (var pass = 0; pass < passes; pass++)
        {
            // Read the whole run into the scratch buffer, then copy it back, so the
            // run list itself is reused frame-to-frame with no allocation. The point
            // sequence is identical to the allocate-a-new-list version.
            scratch.Clear();
            scratch.Add(run[0]);
            for (var i = 0; i < run.Count - 1; i++)
            {
                var (pa, la) = run[i];
                var (pb, lb) = run[i + 1];
                scratch.Add((LerpPoint(pa, pb, 0.25), Lerp(la, lb, 0.25)));
                scratch.Add((LerpPoint(pa, pb, 0.75), Lerp(la, lb, 0.75)));
            }

            scratch.Add(run[^1]);
            run.Clear();
            run.AddRange(scratch);
            if (run.Count > 260)
            {
                break;
            }
        }
    }

    private static WpfPoint LerpPoint(WpfPoint a, WpfPoint b, double t)
    {
        return new WpfPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    private static void DrawSmoothLaserStroke(DrawingContext drawingContext, List<WpfPoint> points, MediaColor color, double opacity, double thickness)
    {
        if (points.Count < 2 || opacity <= 0 || thickness <= 0)
        {
            return;
        }

        drawingContext.DrawGeometry(null, GetPen(color, opacity, thickness), BuildSmoothLaserGeometry(points));
    }

    private static Geometry BuildSmoothLaserGeometry(IReadOnlyList<WpfPoint> points)
    {
        // Quadratic beziers through segment midpoints turn the polyline into a
        // single smooth curve, so the ribbon reads as one continuous comet.
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            if (points.Count == 2)
            {
                context.LineTo(points[1], isStroked: true, isSmoothJoin: true);
            }
            else
            {
                for (var i = 1; i < points.Count - 1; i++)
                {
                    var mid = new WpfPoint((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2);
                    context.QuadraticBezierTo(points[i], mid, isStroked: true, isSmoothJoin: true);
                }

                context.LineTo(points[^1], isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static void DrawLaserHead(
        DrawingContext drawingContext,
        WpfPoint center,
        double globalDim,
        MediaColor color,
        double colorOpacity,
        double dot,
        double speedBlend,
        double pulse,
        Vector direction,
        bool glow)
    {
        if (globalDim <= 0.003)
        {
            return;
        }

        var radius = dot / 2.0 * Lerp(1.0, 1.06, speedBlend);

        if (glow)
        {
            // Subtle motion streak: only when moving, thin and short - a crisp
            // sharpening of the head in the travel direction, not a glow blob.
            if (speedBlend > 0.05 && direction.LengthSquared > 1e-6)
            {
                direction.Normalize();
                var tail = center - direction * radius * Lerp(0, 2.4, speedBlend);
                DrawLine(drawingContext, tail, center, color, globalDim * 0.20 * speedBlend, Math.Max(1, radius * 0.7));
            }

            DrawRadialGlow(drawingContext, center, color, globalDim * 0.42, radius * 3.2);
            DrawRadialGlow(drawingContext, center, color, globalDim * 0.24, radius * 1.9);
        }

        drawingContext.DrawEllipse(GetBrush(color, globalDim * 0.95 * pulse), null, center, radius, radius);
        drawingContext.DrawEllipse(GetBrush(Colors.White, globalDim * 0.85 * pulse * colorOpacity), null, center, Math.Max(1.2, radius * 0.4), Math.Max(1.2, radius * 0.4));
    }

    private void DrawAnnotateBorder(DrawingContext drawingContext)
    {
        var color = _settingsProvider().ToAnnotationMediaColor();
        var pen = CreatePen(color, 0.9, 2);
        var inset = pen.Thickness / 2;
        drawingContext.DrawRectangle(null, pen, new Rect(inset, inset, Math.Max(0, ActualWidth - pen.Thickness), Math.Max(0, ActualHeight - pen.Thickness)));
    }

    private void DrawSpotlight(DrawingContext drawingContext, ScreenPoint? screenPoint, bool magnifierActive)
    {
        if (screenPoint is not { } point)
        {
            return;
        }

        var settings = _settingsProvider();
        var mode = _modeProvider();
        var spotlightActive = settings.SpotlightEnabled && !IsBlankScreenMode(mode);
        if (!spotlightActive && !magnifierActive)
        {
            return;
        }

        var center = ToLocal(point);
        var radius = magnifierActive ? settings.MagnifierRadius : settings.SpotlightRadius;
        if (spotlightActive)
        {
            var key = (ActualWidth, ActualHeight, Math.Round(center.X, 1), Math.Round(center.Y, 1), Math.Round(radius, 1));
            if (_spotlightDimGeometry is null || _spotlightDimKey != key)
            {
                var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
                var hole = new EllipseGeometry(center, radius, radius);
                var dimGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
                dimGeometry.Freeze();
                _spotlightDimGeometry = dimGeometry;
                _spotlightDimKey = key;
            }

            drawingContext.DrawGeometry(GetBrush(Colors.Black, settings.SpotlightOpacity), null, _spotlightDimGeometry);
        }

        if (magnifierActive)
        {
            DrawMagnifierFrame(drawingContext, center, radius);
            return;
        }

        var edgePen = CreatePen(Colors.White, 0.26, 1.4);
        drawingContext.DrawEllipse(null, edgePen, center, radius, radius);
    }

    private void DrawRegionSpotlights(DrawingContext drawingContext)
    {
        var settings = _settingsProvider();
        if (settings.SpotlightEnabled)
        {
            return;
        }

        var regions = _spotlightRegionProvider();
        if (regions.Count == 0)
        {
            return;
        }

        var localRects = new List<Rect>();
        var keyParts = new List<string>
        {
            ActualWidth.ToString("0.0", CultureInfo.InvariantCulture),
            ActualHeight.ToString("0.0", CultureInfo.InvariantCulture)
        };

        var selectedIndex = _modeProvider() == InteractionMode.RegionSpotlightSelect
            ? _spotlightRegionSelectionProvider()
            : -1;

        foreach (var region in regions)
        {
            keyParts.Add(region.Left.ToString("0.0", CultureInfo.InvariantCulture));
            keyParts.Add(region.Top.ToString("0.0", CultureInfo.InvariantCulture));
            keyParts.Add(region.Right.ToString("0.0", CultureInfo.InvariantCulture));
            keyParts.Add(region.Bottom.ToString("0.0", CultureInfo.InvariantCulture));

            if (region.Intersects(_screenBounds))
            {
                var rect = ToRect(region);
                if (rect.Width > 0.5 && rect.Height > 0.5)
                {
                    localRects.Add(rect);
                }
            }
        }

        var key = string.Join("|", keyParts);
        if (_regionSpotlightDimGeometry is null || !string.Equals(_regionSpotlightDimKey, key, StringComparison.Ordinal))
        {
            Geometry dimGeometry = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
            foreach (var rect in localRects)
            {
                dimGeometry = new CombinedGeometry(
                    GeometryCombineMode.Exclude,
                    dimGeometry,
                    new RectangleGeometry(rect));
            }

            dimGeometry.Freeze();
            _regionSpotlightDimGeometry = dimGeometry;
            _regionSpotlightDimKey = key;
        }

        drawingContext.DrawGeometry(GetBrush(Colors.Black, settings.SpotlightOpacity), null, _regionSpotlightDimGeometry);

        var edgePen = CreatePen(Colors.White, 0.30, 1.2);
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (!region.Intersects(_screenBounds))
            {
                continue;
            }

            var rect = ToRect(region);
            if (rect.Width <= 0.5 || rect.Height <= 0.5)
            {
                continue;
            }

            drawingContext.DrawRectangle(null, edgePen, rect);
            if (i == selectedIndex)
            {
                DrawRectHandles(drawingContext, rect);
            }
        }
    }

    private void DrawMagnifierFrame(DrawingContext drawingContext, WpfPoint center, double radius)
    {
        drawingContext.DrawEllipse(null, CreatePen(Colors.Black, 0.34, 3.2), center, radius, radius);
        drawingContext.DrawEllipse(null, CreatePen(Colors.White, 0.82, 1.4), center, radius, radius);
    }

    private Rect ToRect(ScreenPoint start, ScreenPoint end)
    {
        return new Rect(ToLocal(start), ToLocal(end));
    }

    private Rect ToRect(ScreenRect rect)
    {
        return new Rect(ToLocal(new ScreenPoint(rect.Left, rect.Top)), ToLocal(new ScreenPoint(rect.Right, rect.Bottom)));
    }

    private WpfPoint ToLocal(ScreenPoint point)
    {
        return PointFromScreen(new WpfPoint(point.X, point.Y));
    }

    private WpfPoint ToLocal(TrailPoint point)
    {
        return PointFromScreen(new WpfPoint(point.X, point.Y));
    }

    // Frozen brushes/pens are immutable, so we cache and reuse them across frames
    // and surfaces instead of allocating ~30 per laser frame. Access is UI-thread
    // only. Opacity is baked into the colour's alpha; thickness is quantized.
    private static readonly Dictionary<uint, SolidColorBrush> BrushCache = [];
    private static readonly Dictionary<long, WpfPen> PenCache = [];

    private static SolidColorBrush GetBrush(MediaColor color, double opacity)
    {
        var alpha = ToAlpha(opacity * color.A / 255.0);
        var key = ((uint)alpha << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        if (!BrushCache.TryGetValue(key, out var brush))
        {
            if (BrushCache.Count > 8192)
            {
                BrushCache.Clear();
            }

            brush = new SolidColorBrush(MediaColor.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            BrushCache[key] = brush;
        }

        return brush;
    }

    private static WpfPen GetPen(MediaColor color, double opacity, double thickness)
    {
        var alpha = ToAlpha(opacity * color.A / 255.0);
        var thicknessKey = (uint)Math.Clamp(Math.Round(thickness * 4), 0, 0xFFFF); // 0.25px steps
        var key = ((long)alpha << 48) | ((long)color.R << 40) | ((long)color.G << 32) | ((long)color.B << 24) | thicknessKey;
        if (!PenCache.TryGetValue(key, out var pen))
        {
            if (PenCache.Count > 8192)
            {
                PenCache.Clear();
            }

            pen = new WpfPen(GetBrush(color, opacity), thicknessKey / 4.0)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            PenCache[key] = pen;
        }

        return pen;
    }

    private static WpfPen CreatePen(MediaColor color, double opacity, double thickness)
    {
        return GetPen(color, opacity, thickness);
    }

    private static void DrawLine(
        DrawingContext drawingContext,
        WpfPoint start,
        WpfPoint end,
        MediaColor color,
        double opacity,
        double thickness)
    {
        drawingContext.DrawLine(CreatePen(color, opacity, thickness), start, end);
    }

    private static void DrawRadialGlow(DrawingContext drawingContext, WpfPoint center, MediaColor color, double opacity, double radius)
    {
        if (opacity <= 0 || radius <= 0)
        {
            return;
        }

        // The gradient brush lives in unit space (center 0.5, radius 0.5) and is
        // independent of the draw radius, so it depends only on colour+opacity and
        // can be cached/reused across frames instead of rebuilt every glow draw.
        drawingContext.DrawEllipse(GetGlowBrush(color, opacity), null, center, radius, radius);
    }

    // Frozen radial-gradient brushes keyed by colour + quantized opacity. The two
    // derived stops scale deterministically with the head alpha, so the baked alpha
    // is a sufficient key. UI-thread only, same as BrushCache/PenCache.
    private static readonly Dictionary<uint, RadialGradientBrush> GlowBrushCache = [];

    private static RadialGradientBrush GetGlowBrush(MediaColor color, double opacity)
    {
        var colorOpacity = color.A / 255.0;
        var alpha = ToAlpha(opacity * colorOpacity);
        var key = ((uint)alpha << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        if (!GlowBrushCache.TryGetValue(key, out var brush))
        {
            if (GlowBrushCache.Count > 4096)
            {
                GlowBrushCache.Clear();
            }

            brush = new RadialGradientBrush
            {
                Center = new WpfPoint(0.5, 0.5),
                GradientOrigin = new WpfPoint(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            brush.GradientStops.Add(new GradientStop(MediaColor.FromArgb(alpha, color.R, color.G, color.B), 0));
            brush.GradientStops.Add(new GradientStop(MediaColor.FromArgb(ToAlpha(opacity * 0.38 * colorOpacity), color.R, color.G, color.B), 0.42));
            brush.GradientStops.Add(new GradientStop(MediaColor.FromArgb(0, color.R, color.G, color.B), 1));
            brush.Freeze();
            GlowBrushCache[key] = brush;
        }

        return brush;
    }

    private static byte ToAlpha(double opacity)
    {
        return (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
    }

    private static double Distance(WpfPoint a, WpfPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Distance(TrailPoint a, TrailPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Cheap physical-pixel bounds test: does the trail's bounding box (padded by
    /// the dot size) intersect this surface's monitor? Trail points are stored in
    /// virtual-screen pixels, same frame as the monitor bounds.
    /// </summary>
    private bool TrailTouchesScreen(IReadOnlyList<TrailPoint> points, double dot)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        foreach (var point in points)
        {
            if (point.X < minX) minX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.X > maxX) maxX = point.X;
            if (point.Y > maxY) maxY = point.Y;
        }

        return new ScreenRect(minX, minY, maxX, maxY).Inflate(dot + 2).Intersects(_screenBounds);
    }

    /// <summary>
    /// Average cursor speed (physical px/ms) over the most recent window, mapped to
    /// 0 (slow) .. 1 (fast). Walks back from the head until the time window is
    /// covered or a ribbon-breaking gap is hit.
    /// </summary>
    private static double EstimateSpeedBlend(IReadOnlyList<TrailPoint> points, double now)
    {
        if (points.Count < 2)
        {
            return 0;
        }

        var last = points[^1];
        var windowStart = now - LaserSpeedWindowMs;
        var firstIndex = points.Count - 1;

        for (var i = points.Count - 1; i > 0; i--)
        {
            if (points[i].TimeMs - points[i - 1].TimeMs > LaserMaxGapMs)
            {
                firstIndex = i;
                break;
            }

            firstIndex = i - 1;
            if (points[firstIndex].TimeMs <= windowStart)
            {
                break;
            }
        }

        if (firstIndex >= points.Count - 1)
        {
            return 0;
        }

        var elapsedMs = Math.Max(1, last.TimeMs - points[firstIndex].TimeMs);
        var distance = 0.0;
        for (var i = firstIndex + 1; i < points.Count; i++)
        {
            distance += Distance(points[i - 1], points[i]);
        }

        var speedPxPerMs = distance / elapsedMs;
        return Math.Clamp((speedPxPerMs - LaserSlowSpeedPxPerMs) / (LaserFastSpeedPxPerMs - LaserSlowSpeedPxPerMs), 0, 1);
    }

    private static double Lerp(double start, double end, double amount)
    {
        return start + (end - start) * Math.Clamp(amount, 0, 1);
    }

    private static Vector EstimateHeadDirection(WpfPoint[] local, int count)
    {
        for (var i = count - 1; i > 0; i--)
        {
            var direction = local[i] - local[i - 1];
            if (direction.Length >= 0.8)
            {
                return direction;
            }
        }

        return default;
    }

    private readonly record struct CachedStrokeGeometry(int Version, Geometry Geometry);
}
