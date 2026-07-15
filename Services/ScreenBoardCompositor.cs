using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FocusTool.Win.Services;

internal static class ScreenBoardCompositor
{
    public static BitmapSource CompositePrivacyLayer(BitmapSource background, BitmapSource privacyLayer)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(privacyLayer);

        if (background.PixelWidth != privacyLayer.PixelWidth
            || background.PixelHeight != privacyLayer.PixelHeight)
        {
            throw new InvalidOperationException(
                $"Screen Board privacy layer size {privacyLayer.PixelWidth}x{privacyLayer.PixelHeight} " +
                $"does not match background size {background.PixelWidth}x{background.PixelHeight}.");
        }

        var baseImage = ConvertToPbgra32(background);
        var overlayImage = ConvertToPbgra32(privacyLayer);
        var stride = checked(baseImage.PixelWidth * 4);
        var byteCount = checked(stride * baseImage.PixelHeight);
        var basePixels = new byte[byteCount];
        var overlayPixels = new byte[byteCount];
        baseImage.CopyPixels(basePixels, stride, 0);
        overlayImage.CopyPixels(overlayPixels, stride, 0);

        for (var offset = 0; offset < byteCount; offset += 4)
        {
            var overlayAlpha = overlayPixels[offset + 3];
            if (overlayAlpha == 0)
            {
                continue;
            }

            var inverseAlpha = 255 - overlayAlpha;
            basePixels[offset] = CompositePremultiplied(overlayPixels[offset], basePixels[offset], inverseAlpha);
            basePixels[offset + 1] = CompositePremultiplied(overlayPixels[offset + 1], basePixels[offset + 1], inverseAlpha);
            basePixels[offset + 2] = CompositePremultiplied(overlayPixels[offset + 2], basePixels[offset + 2], inverseAlpha);
            basePixels[offset + 3] = CompositePremultiplied(overlayAlpha, basePixels[offset + 3], inverseAlpha);
        }

        var result = BitmapSource.Create(
            baseImage.PixelWidth,
            baseImage.PixelHeight,
            PositiveDpi(background.DpiX),
            PositiveDpi(background.DpiY),
            PixelFormats.Pbgra32,
            palette: null,
            basePixels,
            stride);
        result.Freeze();
        return result;
    }

    private static byte CompositePremultiplied(byte foreground, byte background, int inverseAlpha)
    {
        return (byte)Math.Min(255, foreground + ((background * inverseAlpha + 127) / 255));
    }

    private static BitmapSource ConvertToPbgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Pbgra32)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, destinationPalette: null, alphaThreshold: 0);
        converted.Freeze();
        return converted;
    }

    private static double PositiveDpi(double dpi) => dpi > 0 ? dpi : 96;
}
