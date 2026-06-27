using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace FocusTool.Win.Overlay;

internal sealed class RectSelectionRenderer
{
    private readonly Func<ScreenRect, Rect> _toRect;
    private readonly Action<DrawingContext, Rect> _drawRectHandles;
    private readonly Func<MediaColor, double, SolidColorBrush> _getBrush;
    private readonly Func<MediaColor, double, double, WpfPen> _createPen;
    private readonly Func<string, MediaColor, double, double, double, FormattedText> _getFormattedText;
    private readonly WpfPen _selectionDashPen;

    public RectSelectionRenderer(
        Func<ScreenRect, Rect> toRect,
        Action<DrawingContext, Rect> drawRectHandles,
        Func<MediaColor, double, SolidColorBrush> getBrush,
        Func<MediaColor, double, double, WpfPen> createPen,
        Func<string, MediaColor, double, double, double, FormattedText> getFormattedText,
        WpfPen selectionDashPen)
    {
        _toRect = toRect;
        _drawRectHandles = drawRectHandles;
        _getBrush = getBrush;
        _createPen = createPen;
        _getFormattedText = getFormattedText;
        _selectionDashPen = selectionDashPen;
    }

    public void Draw(
        DrawingContext drawingContext,
        RectOverlayVisual visual,
        ScreenRect screenBounds,
        double surfaceWidth,
        double surfaceHeight)
    {
        if (!visual.Rect.Intersects(screenBounds))
        {
            return;
        }

        var rect = _toRect(visual.Rect);
        DrawSelectionRectangle(drawingContext, rect, visual.IsDraft);
        if (visual.ShowHandles)
        {
            _drawRectHandles(drawingContext, rect);
        }

        if (visual.ShowReadout)
        {
            DrawReadout(drawingContext, visual.Rect, rect, surfaceWidth, surfaceHeight);
        }
    }

    public void DrawSelectionRectangle(DrawingContext drawingContext, ScreenRect screenRect, bool isDraft)
    {
        DrawSelectionRectangle(drawingContext, _toRect(screenRect), isDraft);
    }

    private void DrawSelectionRectangle(DrawingContext drawingContext, Rect rect, bool isDraft)
    {
        if (rect.Width < 1 || rect.Height < 1)
        {
            return;
        }

        drawingContext.DrawRectangle(_getBrush(Colors.DeepSkyBlue, isDraft ? 0.045 : 0.07), null, rect);
        drawingContext.DrawRectangle(null, _createPen(Colors.Black, 0.32, 3.2), rect);
        drawingContext.DrawRectangle(null, _selectionDashPen, rect);
    }

    private void DrawReadout(
        DrawingContext drawingContext,
        ScreenRect screenRect,
        Rect localRect,
        double surfaceWidth,
        double surfaceHeight)
    {
        if (localRect.Width < 1 || localRect.Height < 1)
        {
            return;
        }

        var width = Math.Max(1, (int)Math.Round(screenRect.Width));
        var height = Math.Max(1, (int)Math.Round(screenRect.Height));
        var x = (int)Math.Round(screenRect.Left);
        var y = (int)Math.Round(screenRect.Top);
        var text = $"{width} x {height}px  X {x}  Y {y}";
        var formatted = _getFormattedText(text, Colors.White, 0.96, 12.5, 15);
        var paddingX = 8.0;
        var paddingY = 4.0;
        var bubbleWidth = formatted.WidthIncludingTrailingWhitespace + paddingX * 2;
        var bubbleHeight = formatted.Height + paddingY * 2;
        var bubbleLeft = Math.Clamp(localRect.Left, 4, Math.Max(4, surfaceWidth - bubbleWidth - 4));
        var bubbleTop = localRect.Top - bubbleHeight - 8;
        if (bubbleTop < 4)
        {
            bubbleTop = Math.Min(surfaceHeight - bubbleHeight - 4, localRect.Bottom + 8);
        }

        var bubble = new Rect(bubbleLeft, Math.Max(4, bubbleTop), bubbleWidth, bubbleHeight);
        drawingContext.DrawRoundedRectangle(_getBrush(Colors.Black, 0.78), _createPen(Colors.White, 0.18, 1), bubble, 4, 4);
        drawingContext.DrawText(formatted, new WpfPoint(bubble.Left + paddingX, bubble.Top + paddingY));
    }
}
