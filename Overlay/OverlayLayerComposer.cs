using System.Windows;
using System.Windows.Media.Imaging;

namespace FocusTool.Win.Overlay;

/// <summary>
/// Places monitor-local overlay captures into one source-window-sized pixel buffer.
/// Coordinates and dimensions are physical pixels.
/// </summary>
internal static class OverlayLayerComposer
{
    public static void CopyInto(BitmapSource source, byte[] destination, int destinationWidth, int destinationHeight, int left, int top)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (destinationWidth <= 0 || destinationHeight <= 0 || destination.Length < destinationWidth * destinationHeight * 4)
        {
            throw new ArgumentException("Destination buffer dimensions are invalid.", nameof(destination));
        }

        var sourceLeft = Math.Max(0, -left);
        var sourceTop = Math.Max(0, -top);
        var destinationLeft = Math.Max(0, left);
        var destinationTop = Math.Max(0, top);
        var copyWidth = Math.Min(source.PixelWidth - sourceLeft, destinationWidth - destinationLeft);
        var copyHeight = Math.Min(source.PixelHeight - sourceTop, destinationHeight - destinationTop);
        if (copyWidth <= 0 || copyHeight <= 0)
        {
            return;
        }

        var destinationStride = destinationWidth * 4;
        source.CopyPixels(
            new Int32Rect(sourceLeft, sourceTop, copyWidth, copyHeight),
            destination,
            destinationStride,
            destinationTop * destinationStride + destinationLeft * 4);
    }
}
