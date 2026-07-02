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
    private const double RegionMaskHandleSize = 8;
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
    private readonly Func<LiveAdjustmentHudFrame?> _liveAdjustmentHudProvider;
    private readonly ScreenRect _screenBounds;
    private bool _annotationInputEnabled;
    // Single-slot cache for the spotlight dim mask: the Exclude boolean op is
    // expensive, so reuse it while size/centre/radius are unchanged (stationary
    // cursor, fade frames, other elements animating).
    private Geometry? _spotlightDimGeometry;
    private (double Width, double Height, double X, double Y, double Radius) _spotlightDimKey;
    private readonly LaserTrailRenderer _laserTrailRenderer;
    private readonly RegionMaskRenderer _regionMaskRenderer;
    private readonly RectSelectionRenderer _rectSelectionRenderer;
    private readonly RegionSpotlightRenderer _regionSpotlightRenderer;
    private readonly CursorEffectsRenderer _cursorEffectsRenderer;
    private readonly AnnotationRenderer _annotationRenderer;
    private readonly LiveAdjustmentHudRenderer _liveAdjustmentHudRenderer;

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
        Func<LiveAdjustmentHudFrame?> liveAdjustmentHudProvider,
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
        _liveAdjustmentHudProvider = liveAdjustmentHudProvider;
        _screenBounds = screenBounds;
        _laserTrailRenderer = new LaserTrailRenderer(
            _trailModel,
            _settingsProvider,
            _clockProvider,
            ToLocal,
            GetBrush,
            CreatePen,
            _screenBounds);
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
        _regionSpotlightRenderer = new RegionSpotlightRenderer(
            ToRect,
            DrawRectHandles,
            GetBrush,
            CreatePen);
        _cursorEffectsRenderer = new CursorEffectsRenderer(
            ToLocal,
            GetBrush,
            CreatePen,
            DrawRadialGlow,
            _clockProvider);
        _annotationRenderer = new AnnotationRenderer(
            _annotations,
            _clockProvider,
            ToLocal,
            ToRect,
            _rectSelectionRenderer,
            DrawRectHandles,
            GetBrush,
            CreatePen,
            GetFormattedText,
            RegionMaskHandleSize);
        _liveAdjustmentHudRenderer = new LiveAdjustmentHudRenderer(
            ToLocal,
            GetFormattedText,
            GetBrush,
            CreatePen);
        _annotations.Changed += OnAnnotationsChanged;
        SnapsToDevicePixels = false;
        Focusable = false;
        IsHitTestVisible = false;
    }

    /// <summary>Detach event handlers so the surface can be garbage collected.</summary>
    public void Detach()
    {
        _annotations.Changed -= OnAnnotationsChanged;
        _annotationRenderer.ClearStrokeGeometryCache();
    }

    private void OnAnnotationsChanged(object? sender, EventArgs e)
    {
        _annotationRenderer.TrimStrokeGeometryCache();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        // A resize or DPI change alters the screen->local projection, so cached
        // local-space geometry is no longer valid.
        base.OnRenderSizeChanged(sizeInfo);
        _annotationRenderer.ClearStrokeGeometryCache();
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
        RenderOverlay(drawingContext, OverlayRenderOptions.Live);
    }

    public void RenderSnapshot(DrawingContext drawingContext, OverlayRenderOptions options)
    {
        RenderOverlay(drawingContext, options);
    }

    private void RenderOverlay(DrawingContext drawingContext, OverlayRenderOptions options)
    {
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
        if (!options.SuppressCursorHighlight)
        {
            DrawCursorHighlight(drawingContext, _cursorHighlightProvider());
        }

        if (!options.SuppressLaser)
        {
            DrawLaserTrail(drawingContext);
        }

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

        if (!options.SuppressLiveAdjustmentHud)
        {
            DrawLiveAdjustmentHud(drawingContext);
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
        _annotationRenderer.Draw(drawingContext);
    }

    private void DrawLiveAdjustmentHud(DrawingContext drawingContext)
    {
        if (_liveAdjustmentHudProvider() is not { } frame)
        {
            return;
        }

        _liveAdjustmentHudRenderer.Draw(
            drawingContext,
            frame,
            _screenBounds,
            ActualWidth,
            ActualHeight);
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

    private static Rect CenteredRect(WpfPoint center, double size)
    {
        var half = size / 2;
        return new Rect(center.X - half, center.Y - half, size, size);
    }

    private static MediaColor GetReadableTextColor(MediaColor background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.58 ? Colors.Black : Colors.White;
    }

    private void DrawCursorHighlight(DrawingContext drawingContext, CursorHighlightFrame frame)
    {
        _cursorEffectsRenderer.Draw(drawingContext, frame, _settingsProvider(), _screenBounds);
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
        _laserTrailRenderer.Draw(drawingContext);
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
        _regionSpotlightRenderer.Draw(
            drawingContext,
            _settingsProvider(),
            _spotlightRegionProvider(),
            _modeProvider() == InteractionMode.RegionSpotlightSelect ? _spotlightRegionSelectionProvider() : -1,
            _screenBounds,
            ActualWidth,
            ActualHeight);
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
}
