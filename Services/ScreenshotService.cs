using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using FocusTool.Win.Native;
using FocusTool.Win.Overlay;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;

namespace FocusTool.Win.Services;

internal sealed class ScreenshotService
{
    public async Task CaptureCurrentMonitorAsync(bool copyToClipboard)
    {
        var capture = await Task.Run(CaptureCurrentMonitorCore);
        if (copyToClipboard)
        {
            await TrySetClipboardImageAsync(capture.Image);
        }
    }

    public async Task CaptureRegionAsync(ScreenRect rect, bool copyToClipboard)
    {
        var capture = await Task.Run(() => CaptureRegionCore(rect));
        if (copyToClipboard)
        {
            await TrySetClipboardImageAsync(capture.Image);
        }
    }

    public async Task<ScreenBoardFrame> CaptureCurrentMonitorFrameAsync()
    {
        return await Task.Run(() =>
        {
            var screen = GetCursorScreen();
            using var bitmap = CaptureScreenBitmap(screen);
            var image = ToBitmapSource(bitmap);
            return CreateFrame(screen.Bounds, image);
        });
    }

    public async Task SaveImageAsync(BitmapSource image, bool copyToClipboard, string fileNamePrefix)
    {
        var frozenImage = FreezeForBackground(image);
        await Task.Run(() => SavePng(frozenImage, fileNamePrefix));
        if (copyToClipboard)
        {
            await TrySetClipboardImageAsync(frozenImage);
        }
    }

    private static ScreenshotCapture CaptureCurrentMonitorCore()
    {
        var screen = GetCursorScreen();
        using var bitmap = CaptureScreenBitmap(screen);
        var image = ToBitmapSource(bitmap);
        SavePng(bitmap, "FocusTool");
        return new ScreenshotCapture(image);
    }

    private static ScreenshotCapture CaptureRegionCore(ScreenRect rect)
    {
        var bounds = ToIntegerBounds(rect);
        using var bitmap = CaptureScreenBitmap(bounds);
        var image = ToBitmapSource(bitmap);
        SavePng(bitmap, "FocusTool_Region");
        return new ScreenshotCapture(image);
    }

    private static Forms.Screen GetCursorScreen()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            return Forms.Screen.FromPoint(new DrawingPoint(point.X, point.Y));
        }

        return Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    }

    private static Bitmap CaptureScreenBitmap(Forms.Screen screen)
    {
        var bounds = screen.Bounds;
        return CaptureScreenBitmap(bounds);
    }

    private static Bitmap CaptureScreenBitmap(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static Rectangle ToIntegerBounds(ScreenRect rect)
    {
        var left = (int)Math.Floor(rect.Left);
        var top = (int)Math.Floor(rect.Top);
        var right = (int)Math.Ceiling(rect.Right);
        var bottom = (int)Math.Ceiling(rect.Bottom);
        var width = Math.Max(1, right - left);
        var height = Math.Max(1, bottom - top);
        return new Rectangle(left, top, width, height);
    }

    private static ScreenBoardFrame CreateFrame(Rectangle bounds, BitmapSource image)
    {
        return new ScreenBoardFrame(
            new ScreenRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            image);
    }

    private static string SavePng(Bitmap bitmap, string fileNamePrefix)
    {
        using var stream = CreateScreenshotFile(fileNamePrefix, out var path);
        bitmap.Save(stream, ImageFormat.Png);
        return path;
    }

    private static string SavePng(BitmapSource image, string fileNamePrefix)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = CreateScreenshotFile(fileNamePrefix, out var path);
        encoder.Save(stream);
        return path;
    }

    private static FileStream CreateScreenshotFile(string fileNamePrefix, out string path)
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
        {
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        var directory = Path.Combine(pictures, "FocusTool");
        Directory.CreateDirectory(directory);

        var safePrefix = string.Join("_", fileNamePrefix.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safePrefix))
        {
            safePrefix = "FocusTool";
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : $"_{attempt}";
            path = Path.Combine(directory, $"{safePrefix}_{timestamp}{suffix}.png");
            try
            {
                return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }

        throw new IOException("Could not create a unique screenshot file name.");
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(handle);
        }
    }

    private static async Task<bool> TrySetClipboardImageAsync(BitmapSource image)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            var operation = await dispatcher.InvokeAsync(() => TrySetClipboardImageOnCurrentThreadAsync(image));
            return await operation;
        }

        return await TrySetClipboardImageOnCurrentThreadAsync(image);
    }

    private static async Task<bool> TrySetClipboardImageOnCurrentThreadAsync(BitmapSource image)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetImage(image);
                return true;
            }
            catch (ExternalException)
            {
                if (attempt == 2)
                {
                    break;
                }

                await Task.Delay(35);
            }
        }

        return false;
    }

    private static BitmapSource FreezeForBackground(BitmapSource image)
    {
        if (image.IsFrozen)
        {
            return image;
        }

        var clone = image.Clone();
        clone.Freeze();
        return clone;
    }

    private sealed record ScreenshotCapture(BitmapSource Image);
}
