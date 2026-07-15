using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusTool.Win.Overlay;
using FocusTool.Win.Models;
using FocusTool.Win.Services;

namespace FocusTool.Verification;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--benchmark-highlighter", StringComparer.OrdinalIgnoreCase))
            {
                RunHighlighterBenchmark();
                return 0;
            }

            VerifyHalfTransparentPrivacyPixel();
            VerifyOpaquePrivacyPixel();
            VerifyMismatchedDimensionsFail();
            VerifyOverlaySegmentsArePlacedInSourceCoordinates();
            VerifyLegacyDefaultShortcutsMigrate();
            VerifyCustomizedLegacyShortcutsArePreserved();
            VerifyCustomizedLegacyHoldShortcutIsPreserved();
            VerifyEraserShortcutMigrationAvoidsConflict();
            VerifyObjectEraserGestureUndoRedo();
            VerifyStrokeSmoothingPreservesEndpointsAndCorners();
            VerifyHighlighterUsesFixedRectangularNib();
            VerifyHighlighterDrawAndHoldLocksAndTracksEndpoint();
            Console.WriteLine("FocusTool verification checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RunHighlighterBenchmark()
    {
        const int sampleCount = 800;
        var raw = new List<ScreenPoint>(sampleCount);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var generatedSweepPoints = 0L;
        for (var index = 0; index < sampleCount; index++)
        {
            raw.Add(new ScreenPoint(
                index * 4.5,
                300 + Math.Sin(index * 0.08) * 80 + (index % 2 == 0 ? 0.8 : -0.8)));
            if (raw.Count < 3)
            {
                continue;
            }

            var smoothed = AnnotationStrokeGeometry.Smooth(raw, StrokeSmoothingLevel.Strong, finalize: false);
            var geometry = AnnotationStrokeGeometry.BuildFixedNibGeometry(smoothed, 4, 24);
            generatedSweepPoints += geometry.Points.Count;
        }

        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            $"Highlighter live replay: {sampleCount} samples, {stopwatch.ElapsedMilliseconds} ms, "
            + $"{allocated / (1024.0 * 1024.0):0.0} MiB allocated, {generatedSweepPoints} generated vertices.");

        const int repeatCount = 100;
        var phaseWatch = System.Diagnostics.Stopwatch.StartNew();
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        IReadOnlyList<ScreenPoint> finalSmoothed = raw;
        for (var index = 0; index < repeatCount; index++)
        {
            finalSmoothed = AnnotationStrokeGeometry.Smooth(raw, StrokeSmoothingLevel.Strong, finalize: false);
        }

        phaseWatch.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            $"  smoothing x{repeatCount}: {phaseWatch.ElapsedMilliseconds} ms, {allocated / (1024.0 * 1024.0):0.0} MiB");

        phaseWatch.Restart();
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < repeatCount; index++)
        {
            _ = AnnotationStrokeGeometry.BuildFixedNibGeometry(finalSmoothed, 4, 24);
        }

        phaseWatch.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            $"  nib geometry x{repeatCount}: {phaseWatch.ElapsedMilliseconds} ms, {allocated / (1024.0 * 1024.0):0.0} MiB");
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

    private static void VerifyEraserShortcutMigrationAvoidsConflict()
    {
        var available = new ShortcutSettings { LayoutVersion = 1 };
        available.Normalize();
        if (available.ToolEraser != "E")
        {
            throw new InvalidOperationException("Eraser did not receive the available E shortcut.");
        }

        var occupied = new ShortcutSettings
        {
            LayoutVersion = 1,
            ClearAlternate = "E"
        };
        occupied.Normalize();
        if (occupied.ToolEraser != ShortcutSettings.DisabledShortcut)
        {
            throw new InvalidOperationException("Eraser migration introduced a shortcut conflict.");
        }
    }

    private static void VerifyObjectEraserGestureUndoRedo()
    {
        var document = new AnnotationDocument(() => 1000);
        var settings = new AppSettings();
        AddLine(document, settings, 20, "#FFFF0000");
        AddLine(document, settings, 60, "#FF00FF00");
        AddLine(document, settings, 100, "#FF0000FF");

        document.BeginEraseGesture(new ScreenPoint(20, 50));
        document.ContinueEraseGesture(new ScreenPoint(100, 50));
        document.EndEraseGesture(new ScreenPoint(100, 50));
        if (document.Shapes.Count != 0)
        {
            throw new InvalidOperationException("Eraser drag did not remove each crossed object.");
        }

        document.Undo();
        if (document.Shapes.Count != 3
            || document.Shapes[0].Color != "#FFFF0000"
            || document.Shapes[2].Color != "#FF0000FF")
        {
            throw new InvalidOperationException("One eraser gesture was not restored as one ordered undo operation.");
        }

        document.Redo();
        if (document.Shapes.Count != 0)
        {
            throw new InvalidOperationException("Redo did not repeat the eraser gesture.");
        }

        var overlap = new AnnotationDocument(() => 1000);
        AddLine(overlap, settings, 40, "#FFFF0000");
        AddLine(overlap, settings, 40, "#FF0000FF");
        overlap.BeginEraseGesture(new ScreenPoint(40, 50));
        overlap.EndEraseGesture(new ScreenPoint(40, 50));
        if (overlap.Shapes.Count != 1 || overlap.Shapes[0].Color != "#FFFF0000")
        {
            throw new InvalidOperationException("Eraser click did not remove only the topmost object.");
        }

        var imageOverlap = new AnnotationDocument(() => 1000);
        AddLine(imageOverlap, settings, 40, "#FFFF0000");
        var image = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[] { 255, 255, 255, 255 },
            4);
        imageOverlap.AddPastedImage(image, new ScreenRect(0, 0, 80, 100));
        imageOverlap.BeginEraseGesture(new ScreenPoint(40, 50));
        imageOverlap.EndEraseGesture(new ScreenPoint(40, 50));
        if (imageOverlap.Shapes.Count != 1 || imageOverlap.Shapes[0].Tool != AnnotationTool.Image)
        {
            throw new InvalidOperationException("Image blocked erasing an annotation beneath it or was erased itself.");
        }

        imageOverlap.BeginEraseGesture(new ScreenPoint(40, 50));
        imageOverlap.EndEraseGesture(new ScreenPoint(40, 50));
        if (imageOverlap.Shapes.Count != 1
            || imageOverlap.Shapes[0].Tool != AnnotationTool.Image
            || imageOverlap.EraserHoverShape is not null)
        {
            throw new InvalidOperationException("Eraser treated a pasted image as an erasable target.");
        }
    }

    private static void VerifyStrokeSmoothingPreservesEndpointsAndCorners()
    {
        var raw = new[]
        {
            new ScreenPoint(0, 0),
            new ScreenPoint(20, 1),
            new ScreenPoint(40, 0),
            new ScreenPoint(40, 30),
            new ScreenPoint(40, 60)
        };
        var smoothed = AnnotationStrokeGeometry.Smooth(raw, StrokeSmoothingLevel.Strong, finalize: true);
        if (smoothed[0] != raw[0]
            || smoothed[^1] != raw[^1]
            || smoothed.Min(point => point.DistanceTo(raw[2])) > 2)
        {
            throw new InvalidOperationException("Stroke smoothing changed an endpoint or rounded away a sharp corner.");
        }

        var noisy = Enumerable.Range(0, 31)
            .Select(index => new ScreenPoint(index * 4, index is 0 or 30 ? 0 : index % 2 == 0 ? 2 : -2))
            .ToArray();
        var stabilized = AnnotationStrokeGeometry.Smooth(noisy, StrokeSmoothingLevel.Strong, finalize: true);
        var rawNoise = noisy.Skip(2).SkipLast(2).Average(point => Math.Abs(point.Y));
        var stabilizedNoise = stabilized.Skip(4).SkipLast(4).Average(point => Math.Abs(point.Y));
        if (stabilizedNoise >= rawNoise * 0.4)
        {
            throw new InvalidOperationException("Strong final smoothing did not suppress high-frequency pointer noise.");
        }
    }

    private static void VerifyHighlighterUsesFixedRectangularNib()
    {
        var horizontal = AnnotationStrokeGeometry.BuildFixedNibGeometry(
            [new ScreenPoint(10, 20), new ScreenPoint(110, 20)],
            4,
            24);
        var vertical = AnnotationStrokeGeometry.BuildFixedNibGeometry(
            [new ScreenPoint(10, 20), new ScreenPoint(10, 120)],
            4,
            24);
        var horizontalThickness = horizontal.Points.Max(point => point.Y) - horizontal.Points.Min(point => point.Y);
        var verticalThickness = vertical.Points.Max(point => point.X) - vertical.Points.Min(point => point.X);
        if (Math.Abs(horizontalThickness - 24) > 0.001
            || Math.Abs(verticalThickness - 4) > 0.001)
        {
            throw new InvalidOperationException("Highlighter tip rotated with the stroke instead of staying fixed.");
        }

        var corner = AnnotationStrokeGeometry.BuildFixedNibGeometry(
            [new ScreenPoint(10, 20), new ScreenPoint(110, 20), new ScreenPoint(110, 120)],
            4,
            24);
        if (corner.FigureEnds.Count != 2
            || corner.FigureEnds[0] < 4
            || corner.FigureEnds[1] - corner.FigureEnds[0] < 4)
        {
            throw new InvalidOperationException("Fixed highlighter sweeps left a disconnected corner.");
        }
    }

    private static void VerifyHighlighterDrawAndHoldLocksAndTracksEndpoint()
    {
        var nowMs = 0.0;
        var document = new AnnotationDocument(() => nowMs);
        var settings = new AppSettings();
        document.BeginStroke(AnnotationTool.Highlighter, new ScreenPoint(10, 10), settings);
        document.UpdateStroke(new ScreenPoint(90, 30), shift: false);
        nowMs = 479;
        if (document.TryLockHighlighterHold(nowMs))
        {
            throw new InvalidOperationException("Highlighter hold locked before the threshold.");
        }

        nowMs = 480;
        if (!document.TryLockHighlighterHold(nowMs) || document.Draft?.HighlighterStraightened != true)
        {
            throw new InvalidOperationException("Highlighter hold did not lock at the threshold.");
        }

        document.UpdateStroke(new ScreenPoint(120, 45), shift: false);
        if (document.Draft?.End != new ScreenPoint(120, 45))
        {
            throw new InvalidOperationException("Locked highlighter endpoint could not be adjusted before release.");
        }
    }

    private static void AddLine(AnnotationDocument document, AppSettings settings, double x, string color)
    {
        settings.AnnotationColor = color;
        document.BeginStroke(AnnotationTool.Line, new ScreenPoint(x, 10), settings);
        document.UpdateStroke(new ScreenPoint(x, 90), shift: false);
        document.CommitStroke();
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
