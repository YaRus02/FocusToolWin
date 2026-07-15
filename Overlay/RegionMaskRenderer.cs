using System.Windows;
using System.Windows.Media;
using FocusTool.Win.Models;
using MediaColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace FocusTool.Win.Overlay;

internal sealed class RegionMaskRenderer
{
    private readonly Func<ScreenRect, Rect> _toRect;
    private readonly Action<DrawingContext, Rect> _drawRectHandles;
    private readonly Func<MediaColor, double, SolidColorBrush> _getBrush;
    private readonly Func<MediaColor, double, double, WpfPen> _createPen;
    private readonly Func<MediaColor, MediaColor> _getReadableTextColor;
    private readonly Func<string, MediaColor, double, double, double, FormattedText> _getFormattedText;
    private bool _wereVisibleOnSurface;

    public RegionMaskRenderer(
        Func<ScreenRect, Rect> toRect,
        Action<DrawingContext, Rect> drawRectHandles,
        Func<MediaColor, double, SolidColorBrush> getBrush,
        Func<MediaColor, double, double, WpfPen> createPen,
        Func<MediaColor, MediaColor> getReadableTextColor,
        Func<string, MediaColor, double, double, double, FormattedText> getFormattedText)
    {
        _toRect = toRect;
        _drawRectHandles = drawRectHandles;
        _getBrush = getBrush;
        _createPen = createPen;
        _getReadableTextColor = getReadableTextColor;
        _getFormattedText = getFormattedText;
    }

    public void Draw(
        DrawingContext drawingContext,
        IReadOnlyList<RegionMask> masks,
        bool showHandles,
        int selectedId,
        ScreenRect screenBounds,
        double surfaceWidth,
        double surfaceHeight,
        IReadOnlySet<int>? excludedMaskIds = null)
    {
        var drewMask = false;

        foreach (var mask in masks)
        {
            if (excludedMaskIds?.Contains(mask.Id) == true
                || !mask.Rect.Intersects(screenBounds))
            {
                continue;
            }

            var color = AppSettings.TryParseColor(mask.Color, out var parsed) ? parsed : Colors.Black;
            var rect = _toRect(mask.Rect);
            DrawMask(drawingContext, mask, color, rect);
            if (showHandles && mask.Id == selectedId)
            {
                _drawRectHandles(drawingContext, rect);
            }

            drewMask = true;
        }

        if (drewMask)
        {
            _wereVisibleOnSurface = true;
            return;
        }

        if (_wereVisibleOnSurface)
        {
            // Transparent layered windows can retain the previous non-empty frame
            // when the next scene is completely empty. Submit one imperceptible
            // full-surface frame so deleted masks do not linger visually.
            drawingContext.DrawRectangle(_getBrush(MediaColor.FromArgb(1, 0, 0, 0), 1), null, new Rect(0, 0, surfaceWidth, surfaceHeight));
            _wereVisibleOnSurface = false;
        }
    }

    private void DrawMask(DrawingContext drawingContext, RegionMask mask, MediaColor color, Rect rect)
    {
        if (rect.Width < 1 || rect.Height < 1)
        {
            return;
        }

        drawingContext.DrawRectangle(_getBrush(color, mask.Opacity), null, rect);
        var contrast = _getReadableTextColor(color);
        if (mask.Style is RegionMaskStyle.Stripes or RegionMaskStyle.StripesWithLabel)
        {
            DrawStripes(drawingContext, rect, contrast, mask.Opacity);
        }

        drawingContext.DrawRectangle(null, _createPen(contrast, 0.62, 1.35), rect);
        if (mask.Style is RegionMaskStyle.Label or RegionMaskStyle.StripesWithLabel)
        {
            DrawLabel(drawingContext, rect, contrast);
        }
    }

    private void DrawStripes(DrawingContext drawingContext, Rect rect, MediaColor color, double maskOpacity)
    {
        if (rect.Width < 8 || rect.Height < 8)
        {
            return;
        }

        var clip = new RectangleGeometry(rect);
        clip.Freeze();
        drawingContext.PushClip(clip);

        var spacing = Math.Clamp(Math.Min(rect.Width, rect.Height) / 4.2, 16, 28);
        var span = rect.Width + rect.Height;
        var opacity = Math.Clamp(0.16 + maskOpacity * 0.12, 0.18, 0.32);
        var pen = _createPen(color, opacity, 1.65);
        for (var x = rect.Left - rect.Height; x <= rect.Left + span; x += spacing)
        {
            drawingContext.DrawLine(
                pen,
                new WpfPoint(x, rect.Bottom),
                new WpfPoint(x + rect.Height, rect.Top));
        }

        drawingContext.Pop();
    }

    private void DrawLabel(DrawingContext drawingContext, Rect rect, MediaColor color)
    {
        if (rect.Width < 42 || rect.Height < 24)
        {
            return;
        }

        var fontSize = Math.Clamp(Math.Min(rect.Height * 0.32, rect.Width / 3.2), 10, 42);
        if (fontSize < 9)
        {
            return;
        }

        var lineHeight = fontSize * 1.05;
        var text = _getFormattedText("HIDE", color, 0.86, fontSize, lineHeight);
        if (text.WidthIncludingTrailingWhitespace + 12 > rect.Width || text.Height + 6 > rect.Height)
        {
            return;
        }

        var point = new WpfPoint(
            rect.Left + (rect.Width - text.WidthIncludingTrailingWhitespace) / 2,
            rect.Top + (rect.Height - text.Height) / 2);
        var haloColor = color == Colors.White ? Colors.Black : Colors.White;
        var halo = _getFormattedText("HIDE", haloColor, 0.26, fontSize, lineHeight);
        drawingContext.DrawText(halo, point + new Vector(-1, 0));
        drawingContext.DrawText(halo, point + new Vector(1, 0));
        drawingContext.DrawText(halo, point + new Vector(0, -1));
        drawingContext.DrawText(halo, point + new Vector(0, 1));
        drawingContext.DrawText(text, point);
    }
}
