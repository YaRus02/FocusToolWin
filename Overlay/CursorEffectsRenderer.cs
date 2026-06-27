using System.Windows.Media;
using FocusTool.Win.Models;
using MediaColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace FocusTool.Win.Overlay;

internal sealed class CursorEffectsRenderer
{
    private const double CursorPulseDurationMs = 360;
    private const double CursorPulseExtraRadius = 26;
    private readonly Func<ScreenPoint, WpfPoint> _toLocal;
    private readonly Func<MediaColor, double, SolidColorBrush> _getBrush;
    private readonly Func<MediaColor, double, double, WpfPen> _createPen;
    private readonly Action<DrawingContext, WpfPoint, MediaColor, double, double> _drawRadialGlow;
    private readonly Func<double> _clockProvider;

    public CursorEffectsRenderer(
        Func<ScreenPoint, WpfPoint> toLocal,
        Func<MediaColor, double, SolidColorBrush> getBrush,
        Func<MediaColor, double, double, WpfPen> createPen,
        Action<DrawingContext, WpfPoint, MediaColor, double, double> drawRadialGlow,
        Func<double> clockProvider)
    {
        _toLocal = toLocal;
        _getBrush = getBrush;
        _createPen = createPen;
        _drawRadialGlow = drawRadialGlow;
        _clockProvider = clockProvider;
    }

    public void Draw(
        DrawingContext drawingContext,
        CursorHighlightFrame frame,
        AppSettings settings,
        ScreenRect screenBounds)
    {
        if (frame.Cursor is null && frame.Pulses.Count == 0)
        {
            return;
        }

        var color = AppSettings.TryParseColor(settings.CursorHighlightColor, out var parsedColor)
            ? parsedColor
            : MediaColor.FromArgb(0xBF, 0xFF, 0xD4, 0x00);
        var radius = settings.CursorHighlightRadius;
        var thickness = settings.CursorHighlightThickness;

        if (frame.Cursor is { } cursor
            && CursorEffectTouchesScreen(cursor, radius + CursorPulseExtraRadius, screenBounds))
        {
            DrawCursorHighlightCore(
                drawingContext,
                _toLocal(cursor),
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
            if (progress >= 1 || !CursorEffectTouchesScreen(pulse.Point, radius + CursorPulseExtraRadius + 4, screenBounds))
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
        _drawRadialGlow(drawingContext, center, color, 0.24, radius * 1.55);
        drawingContext.DrawEllipse(_getBrush(color, 0.052), null, center, radius * 0.9, radius * 0.9);
        drawingContext.DrawEllipse(null, _createPen(Colors.Black, 0.30, thickness + 2.4), center, radius, radius);
        drawingContext.DrawEllipse(null, _createPen(color, 0.95, thickness), center, radius, radius);
        drawingContext.DrawEllipse(null, _createPen(Colors.White, 0.16, 1.0), center, innerRadius, innerRadius);
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
        var center = _toLocal(pulse.Point);
        var line = Math.Max(1.3, thickness * (0.78 + 0.25 * (1 - progress)));

        _drawRadialGlow(drawingContext, center, color, 0.14 * fade, radius + 7);
        drawingContext.DrawEllipse(null, _createPen(Colors.Black, 0.18 * fade, line + 1.4), center, radius, radius);
        drawingContext.DrawEllipse(null, _createPen(color, 0.74 * fade, line), center, radius, radius);
    }

    private static bool CursorEffectTouchesScreen(ScreenPoint point, double radius, ScreenRect screenBounds)
    {
        return new ScreenRect(
            point.X - radius,
            point.Y - radius,
            point.X + radius,
            point.Y + radius).Intersects(screenBounds);
    }

    private static double EaseOutCubic(double value)
    {
        var inverse = 1 - Math.Clamp(value, 0, 1);
        return 1 - inverse * inverse * inverse;
    }
}
