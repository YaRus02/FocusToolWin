using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusTool.Win.Overlay;
using FocusTool.Win.Models;
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
            VerifyLegacyDefaultShortcutsMigrate();
            VerifyCustomizedLegacyShortcutsArePreserved();
            VerifyCustomizedLegacyHoldShortcutIsPreserved();
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
        var first = CreateBitmap(2, 1, [1, 2, 3, 4, 5, 6, 7, 8]);
        var second = CreateBitmap(2, 1, [9, 10, 11, 12, 13, 14, 15, 16]);

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

    private static void VerifyLegacyDefaultShortcutsMigrate()
    {
        var shortcuts = CreateLegacyShortcuts();
        shortcuts.Normalize();

        if (shortcuts.LayoutVersion != ShortcutSettings.CurrentLayoutVersion
            || shortcuts.ToggleAnnotate != "Ctrl+Alt+A"
            || shortcuts.ToggleClickPulse != "Ctrl+Alt+C"
            || shortcuts.HoldSpotlight != "Alt+S"
            || shortcuts.ToolPencil != "W"
            || shortcuts.Redo != "Ctrl+Shift+Z")
        {
            throw new InvalidOperationException("Legacy default shortcuts were not migrated to the left-hand layout.");
        }
    }

    private static void VerifyCustomizedLegacyShortcutsArePreserved()
    {
        var shortcuts = CreateLegacyShortcuts();
        shortcuts.ToggleAnnotate = "Ctrl+Shift+A";
        shortcuts.Normalize();

        if (shortcuts.ToggleAnnotate != "Ctrl+Shift+A"
            || shortcuts.ToggleLaserActivation != "Ctrl+Alt+L"
            || shortcuts.ToggleClickPulse != ShortcutSettings.DisabledShortcut
            || shortcuts.HoldSpotlight != ShortcutSettings.DisabledShortcut)
        {
            throw new InvalidOperationException("Customized legacy shortcuts were overwritten during migration.");
        }
    }

    private static void VerifyCustomizedLegacyHoldShortcutIsPreserved()
    {
        var settings = new AppSettings
        {
            LaserHoldShortcut = "Mouse4",
            Shortcuts = CreateLegacyShortcuts(),
        };
        settings.Normalize();

        if (settings.LaserHoldShortcut != "Mouse4"
            || settings.Shortcuts.ToggleLaserActivation != "Ctrl+Alt+L"
            || settings.Shortcuts.HoldSpotlight != ShortcutSettings.DisabledShortcut)
        {
            throw new InvalidOperationException("A customized legacy hold shortcut did not prevent automatic layout migration.");
        }
    }

    private static ShortcutSettings CreateLegacyShortcuts()
    {
        return new ShortcutSettings
        {
            LayoutVersion = 0,
            ToggleLaserActivation = "Ctrl+Alt+L",
            ToggleAnnotate = "Ctrl+Alt+D",
            PushToAnnotate = "Alt+A",
            ToggleCursorHighlight = "Ctrl+Alt+U",
            ToggleSpotlight = "Ctrl+Alt+S",
            ToggleMagnifier = "Ctrl+Alt+M",
            TogglePinnedLens = "Ctrl+Alt+P",
            ToggleRegionMask = "Ctrl+Alt+H",
            ClearRegionMasks = "Ctrl+Alt+Shift+H",
            ToggleRegionSpotlight = "Ctrl+Alt+Shift+S",
            ClearRegionSpotlights = "Ctrl+Alt+Shift+X",
            ToggleFadingAnnotations = "Ctrl+Alt+F",
            ToggleTimer = "Ctrl+Alt+N",
            ToggleToolbar = "Ctrl+Alt+T",
            TakeScreenshot = "Ctrl+Alt+C",
            TakeRegionScreenshot = "Ctrl+Alt+Shift+C",
            ToggleScreenBoard = "Ctrl+Alt+G",
            ToggleBlackScreen = "Ctrl+Alt+B",
            ToggleWhiteScreen = "Ctrl+Alt+W",
            ExitApp = "Ctrl+Alt+Q",
            ToolArrow = "A",
            ToolRectangle = "R",
            ToolEllipse = "C",
            ToolLine = "L",
            ToolPencil = "P",
            ToolHighlighter = "H",
            ToolText = "T",
            ToolMove = "M",
            ToolStep = "N",
            Color1 = "1",
            Color2 = "2",
            Color3 = "3",
            Color4 = "4",
            Color5 = "5",
            ThicknessDown = "[",
            ThicknessUp = "]",
            Undo = "Ctrl+Z",
            Redo = "Ctrl+Y",
            DeleteSelection = "Backspace",
            Clear = "Delete",
            ClearAlternate = "E",
            ExitAnnotate = "Esc",
        };
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
