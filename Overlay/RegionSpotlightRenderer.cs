using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FocusTool.Win.Models;
using MediaColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;

namespace FocusTool.Win.Overlay;

internal sealed class RegionSpotlightRenderer
{
    private readonly Func<ScreenRect, Rect> _toRect;
    private readonly Action<DrawingContext, Rect> _drawRectHandles;
    private readonly Func<MediaColor, double, SolidColorBrush> _getBrush;
    private readonly Func<MediaColor, double, double, WpfPen> _createPen;
    private Geometry? _dimGeometry;
    private string? _dimKey;

    public RegionSpotlightRenderer(
        Func<ScreenRect, Rect> toRect,
        Action<DrawingContext, Rect> drawRectHandles,
        Func<MediaColor, double, SolidColorBrush> getBrush,
        Func<MediaColor, double, double, WpfPen> createPen)
    {
        _toRect = toRect;
        _drawRectHandles = drawRectHandles;
        _getBrush = getBrush;
        _createPen = createPen;
    }

    public void Draw(
        DrawingContext drawingContext,
        AppSettings settings,
        IReadOnlyList<ScreenRect> regions,
        int selectedIndex,
        ScreenRect screenBounds,
        double surfaceWidth,
        double surfaceHeight)
    {
        if (settings.SpotlightEnabled || regions.Count == 0)
        {
            return;
        }

        var localRects = new List<Rect>();
        var keyParts = new List<string>
        {
            surfaceWidth.ToString("0.0", CultureInfo.InvariantCulture),
            surfaceHeight.ToString("0.0", CultureInfo.InvariantCulture)
        };

        foreach (var region in regions)
        {
            keyParts.Add(region.Left.ToString("0.0", CultureInfo.InvariantCulture));
            keyParts.Add(region.Top.ToString("0.0", CultureInfo.InvariantCulture));
            keyParts.Add(region.Right.ToString("0.0", CultureInfo.InvariantCulture));
            keyParts.Add(region.Bottom.ToString("0.0", CultureInfo.InvariantCulture));

            if (region.Intersects(screenBounds))
            {
                var rect = _toRect(region);
                if (rect.Width > 0.5 && rect.Height > 0.5)
                {
                    localRects.Add(rect);
                }
            }
        }

        var key = string.Join("|", keyParts);
        if (_dimGeometry is null || !string.Equals(_dimKey, key, StringComparison.Ordinal))
        {
            Geometry dimGeometry = new RectangleGeometry(new Rect(0, 0, surfaceWidth, surfaceHeight));
            foreach (var rect in localRects)
            {
                dimGeometry = new CombinedGeometry(
                    GeometryCombineMode.Exclude,
                    dimGeometry,
                    new RectangleGeometry(rect));
            }

            dimGeometry.Freeze();
            _dimGeometry = dimGeometry;
            _dimKey = key;
        }

        drawingContext.DrawGeometry(_getBrush(Colors.Black, settings.SpotlightOpacity), null, _dimGeometry);

        var edgePen = _createPen(Colors.White, 0.30, 1.2);
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            if (!region.Intersects(screenBounds))
            {
                continue;
            }

            var rect = _toRect(region);
            if (rect.Width <= 0.5 || rect.Height <= 0.5)
            {
                continue;
            }

            drawingContext.DrawRectangle(null, edgePen, rect);
            if (i == selectedIndex)
            {
                _drawRectHandles(drawingContext, rect);
            }
        }
    }
}
