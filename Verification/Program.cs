using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusTool.Win.Overlay;
using FocusTool.Win.Services;

namespace FocusTool.Verification;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            VerifyHalfTransparentPrivacyPixel();
            VerifyOpaquePrivacyPixel();
            VerifyMismatchedDimensionsFail();
            VerifyOverlaySegmentsArePlacedInSourceCoordinates();
            Console.WriteLine("FocusTool verification checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void VerifyHalfTransparentPrivacyPixel()
    {
        var background = CreateBitmap(2, 1,
        [
            100, 100, 100, 255,
            90, 80, 70, 255
        ]);
        var privacyLayer = CreateBitmap(2, 1,
        [
            0, 0, 128, 128,
            0, 0, 0, 0
        ]);

        var result = ScreenBoardCompositor.CompositePrivacyLayer(background, privacyLayer);
        AssertPixels(result,
        [
            50, 50, 178, 255,
            90, 80, 70, 255
        ], "Half-transparent privacy layer");
    }

    private static void VerifyOpaquePrivacyPixel()
    {
        var background = CreateBitmap(1, 1, [100, 110, 120, 255]);
        var privacyLayer = CreateBitmap(1, 1, [20, 30, 40, 255]);
        var result = ScreenBoardCompositor.CompositePrivacyLayer(background, privacyLayer);
        AssertPixels(result, [20, 30, 40, 255], "Opaque privacy layer");
    }

    private static void VerifyMismatchedDimensionsFail()
    {
        var background = CreateBitmap(1, 1, [0, 0, 0, 255]);
        var privacyLayer = CreateBitmap(2, 1, [0, 0, 0, 0, 0, 0, 0, 0]);
        try
        {
            _ = ScreenBoardCompositor.CompositePrivacyLayer(background, privacyLayer);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("Mismatched compositor dimensions were accepted.");
    }

    private static void VerifyOverlaySegmentsArePlacedInSourceCoordinates()
    {
        var destination = new byte[4 * 2 * 4];
        var first = new OverlayLayer([1, 2, 3, 4, 5, 6, 7, 8], 2, 1, 8);
        var second = new OverlayLayer([9, 10, 11, 12, 13, 14, 15, 16], 2, 1, 8);

        OverlayLayerComposer.CopyInto(first, destination, 4, 2, 0, 0);
        OverlayLayerComposer.CopyInto(second, destination, 4, 2, 2, 1);

        var expected = new byte[]
        {
            1, 2, 3, 4, 5, 6, 7, 8, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 9, 10, 11, 12, 13, 14, 15, 16,
        };
        if (!destination.SequenceEqual(expected))
        {
            throw new InvalidOperationException("Multi-monitor overlay segments were placed incorrectly.");
        }
    }

    private static BitmapSource CreateBitmap(int width, int height, byte[] pixels)
    {
        var stride = width * 4;
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32,
            palette: null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static void AssertPixels(BitmapSource bitmap, byte[] expected, string scenario)
    {
        var actual = new byte[expected.Length];
        bitmap.CopyPixels(actual, bitmap.PixelWidth * 4, 0);
        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"{scenario} failed. Expected [{string.Join(", ", expected)}], " +
                $"actual [{string.Join(", ", actual)}].");
        }
    }
}
