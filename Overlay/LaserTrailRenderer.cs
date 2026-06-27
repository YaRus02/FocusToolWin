using System.Windows;
using System.Windows.Media;
using FocusTool.Win.Models;
using MediaColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace FocusTool.Win.Overlay;

internal sealed class LaserTrailRenderer
{
    // Laser comet (ported from LaserMarker's TrailElement): the trail is drawn as
    // contiguous smooth bezier strokes split into age bands, and each sample's
    // "life" is derived from its real timestamp against a window that shrinks as
    // the cursor sits still - so the tail recedes into the head and stale points
    // fall out of the window instead of snapping back as phantom lines.
    private const int LaserCoreBands = 18;
    private const int LaserGlowBands = 5;
    private const double LaserGraceMs = 50;
    private const double LaserMaxGapMs = 250;
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

    private readonly TrailModel _trailModel;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<double> _clockProvider;
    private readonly Func<TrailPoint, WpfPoint> _toLocal;
    private readonly Func<MediaColor, double, SolidColorBrush> _getBrush;
    private readonly Func<MediaColor, double, double, WpfPen> _createPen;
    private readonly ScreenRect _screenBounds;
    private double _speedBlend;

    // Reusable scratch buffers for the laser pipeline, cleared and refilled every
    // frame instead of allocating fresh Lists. UI-thread only and never re-entrant
    // (OnRender is synchronous), so the geometry/visual output is byte-identical to
    // the allocate-per-frame version - only the GC churn is removed.
    private readonly List<List<(WpfPoint Point, double Life)>> _laserRuns = [];
    private readonly Stack<List<(WpfPoint Point, double Life)>> _laserRunPool = new();
    private readonly List<(WpfPoint Point, double Life)> _smoothScratch = [];
    private readonly List<WpfPoint> _bandStroke = [];
    private WpfPoint[] _laserLocalScratch = [];
    private double[] _laserLifeScratch = [];

    public LaserTrailRenderer(
        TrailModel trailModel,
        Func<AppSettings> settingsProvider,
        Func<double> clockProvider,
        Func<TrailPoint, WpfPoint> toLocal,
        Func<MediaColor, double, SolidColorBrush> getBrush,
        Func<MediaColor, double, double, WpfPen> createPen,
        ScreenRect screenBounds)
    {
        _trailModel = trailModel;
        _settingsProvider = settingsProvider;
        _clockProvider = clockProvider;
        _toLocal = toLocal;
        _getBrush = getBrush;
        _createPen = createPen;
        _screenBounds = screenBounds;
    }

    public void Draw(DrawingContext drawingContext)
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

        if (!TrailTouchesScreen(points, dot))
        {
            return;
        }

        var color = settings.ToMediaColor();
        var targetBlend = EstimateSpeedBlend(points, now);
        _speedBlend += (targetBlend - _speedBlend) * LaserSpeedEasing;
        var thicknessScale = Lerp(LaserSlowThicknessFactor, LaserFastThicknessFactor, _speedBlend);
        var trailMs = Math.Max(1, settings.TrailLengthMs);

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
            local[i] = _toLocal(points[i]);
            life[i] = 1.0 - (now - points[i].TimeMs) / window;
        }

        var headDirection = EstimateHeadDirection(local, count);
        var pulse = 1 + 0.30 * _speedBlend;
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

            for (var band = 0; band < LaserCoreBands; band++)
            {
                var bandLife = (band + 0.5) / LaserCoreBands;
                var shaped = Math.Pow(bandLife, 0.7);
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
                    break;
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

    private void DrawSmoothLaserStroke(DrawingContext drawingContext, List<WpfPoint> points, MediaColor color, double opacity, double thickness)
    {
        if (points.Count < 2 || opacity <= 0 || thickness <= 0)
        {
            return;
        }

        drawingContext.DrawGeometry(null, _createPen(color, opacity, thickness), BuildSmoothLaserGeometry(points));
    }

    private static Geometry BuildSmoothLaserGeometry(IReadOnlyList<WpfPoint> points)
    {
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

    private void DrawLaserHead(
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
            if (speedBlend > 0.05 && direction.LengthSquared > 1e-6)
            {
                direction.Normalize();
                var tail = center - direction * radius * Lerp(0, 2.4, speedBlend);
                DrawLine(drawingContext, tail, center, color, globalDim * 0.20 * speedBlend, Math.Max(1, radius * 0.7));
            }

            DrawRadialGlow(drawingContext, center, color, globalDim * 0.42, radius * 3.2);
            DrawRadialGlow(drawingContext, center, color, globalDim * 0.24, radius * 1.9);
        }

        drawingContext.DrawEllipse(_getBrush(color, globalDim * 0.95 * pulse), null, center, radius, radius);
        drawingContext.DrawEllipse(_getBrush(Colors.White, globalDim * 0.85 * pulse * colorOpacity), null, center, Math.Max(1.2, radius * 0.4), Math.Max(1.2, radius * 0.4));
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

    private static void DrawRadialGlow(DrawingContext drawingContext, WpfPoint center, MediaColor color, double opacity, double radius)
    {
        if (opacity <= 0 || radius <= 0)
        {
            return;
        }

        drawingContext.DrawEllipse(GetGlowBrush(color, opacity), null, center, radius, radius);
    }

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
}
