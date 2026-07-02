using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace FocusTool.Win.Overlay;

internal sealed class LiveAdjustmentHudRenderer(
    Func<ScreenPoint, WpfPoint> toLocal,
    Func<string, MediaColor, double, double, double, FormattedText> getFormattedText,
    Func<MediaColor, double, SolidColorBrush> getBrush,
    Func<MediaColor, double, double, WpfPen> createPen)
{
    private const double FontSize = 12.5;
    private const double LineHeight = 16;
    private const double PaddingX = 10;
    private const double PaddingY = 6;
    private const double OffsetX = 18;
    private const double OffsetY = 18;
    private const double Margin = 10;
    private const double CornerRadius = 6;

    public void Draw(
        DrawingContext drawingContext,
        LiveAdjustmentHudFrame frame,
        ScreenRect screenBounds,
        double surfaceWidth,
        double surfaceHeight)
    {
        if (frame.Opacity <= 0
            || surfaceWidth <= 1
            || surfaceHeight <= 1
            || !ContainsAnchor(screenBounds, frame.Anchor))
        {
            return;
        }

        var text = getFormattedText(frame.Text, Colors.White, 0.96 * frame.Opacity, FontSize, LineHeight);
        var width = Math.Ceiling(text.WidthIncludingTrailingWhitespace + PaddingX * 2);
        var height = Math.Ceiling(LineHeight + PaddingY * 2);
        var anchor = toLocal(frame.Anchor);
        var left = Math.Clamp(anchor.X + OffsetX, Margin, Math.Max(Margin, surfaceWidth - width - Margin));
        var top = Math.Clamp(anchor.Y + OffsetY, Margin, Math.Max(Margin, surfaceHeight - height - Margin));
        var rect = new Rect(left, top, width, height);
        var shadow = new Rect(rect.Left, rect.Top + 1.5, rect.Width, rect.Height);

        drawingContext.DrawRoundedRectangle(
            getBrush(Colors.Black, 0.28 * frame.Opacity),
            null,
            shadow,
            CornerRadius,
            CornerRadius);
        drawingContext.DrawRoundedRectangle(
            getBrush(Colors.Black, 0.78 * frame.Opacity),
            createPen(Colors.White, 0.18 * frame.Opacity, 1),
            rect,
            CornerRadius,
            CornerRadius);
        drawingContext.DrawText(text, new WpfPoint(rect.Left + PaddingX, rect.Top + PaddingY));
    }

    private static bool ContainsAnchor(ScreenRect bounds, ScreenPoint point)
    {
        return point.X >= bounds.Left
            && point.X < bounds.Right
            && point.Y >= bounds.Top
            && point.Y < bounds.Bottom;
    }
}
