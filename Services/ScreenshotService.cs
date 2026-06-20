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
    public void CaptureCurrentMonitor(bool copyToClipboard)
    {
        var screen = GetCursorScreen();
        using var bitmap = CaptureScreenBitmap(screen);
        var image = ToBitmapSource(bitmap);
        SavePng(bitmap);
        if (copyToClipboard)
        {
            TrySetClipboardImage(image);
        }
    }

    public ScreenBoardFrame CaptureCurrentMonitorFrame()
    {
        var screen = GetCursorScreen();
        using var bitmap = CaptureScreenBitmap(screen);
        var image = ToBitmapSource(bitmap);
        return CreateFrame(screen.Bounds, image);
    }

    public void SaveImage(BitmapSource image, bool copyToClipboard, string fileNamePrefix)
    {
        SavePng(image, fileNamePrefix);
        if (copyToClipboard)
        {
            TrySetClipboardImage(image);
        }
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

    private static ScreenBoardFrame CreateFrame(Rectangle bounds, BitmapSource image)
    {
        return new ScreenBoardFrame(
            new ScreenRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            image);
    }

    private static string SavePng(Bitmap bitmap)
    {
        var path = CreateScreenshotPath("FocusTool");
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    private static string SavePng(BitmapSource image, string fileNamePrefix)
    {
        var path = CreateScreenshotPath(fileNamePrefix);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        encoder.Save(stream);
        return path;
    }

    private static string CreateScreenshotPath(string fileNamePrefix)
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
        return Path.Combine(directory, $"{safePrefix}_{timestamp}.png");
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

    private static bool TrySetClipboardImage(BitmapSource image)
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
                Thread.Sleep(35);
            }
        }

        return false;
    }
}
