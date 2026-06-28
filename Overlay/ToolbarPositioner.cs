using System.Windows;
using System.Windows.Interop;
using FocusTool.Win.Native;
using DrawingPoint = System.Drawing.Point;
using Forms = System.Windows.Forms;

namespace FocusTool.Win.Overlay;

// Owns placement of the toolbar window: initial near-cursor positioning,
// remembering the last user-dragged position, and clamping onto the visible
// working area. Works in physical pixels via the window handle.
internal sealed class ToolbarPositioner
{
    private readonly Window _window;
    private bool _hasSavedPosition;
    private int _savedLeft;
    private int _savedTop;

    public ToolbarPositioner(Window window)
    {
        _window = window;
    }

    private IntPtr Handle => new WindowInteropHelper(_window).Handle;

    public void PositionNearCursor()
    {
        var handle = Handle;
        if (handle == IntPtr.Zero)
        {
            var fallback = GetCursorScreen().WorkingArea;
            _window.Left = fallback.Left + 8;
            _window.Top = fallback.Top + 18;
            return;
        }

        int left, top;
        if (_hasSavedPosition)
        {
            (left, top) = ClampToWorkingArea(_savedLeft, _savedTop);
        }
        else
        {
            var area = GetCursorScreen().WorkingArea;
            var scale = GetCursorMonitorScale();
            var width = (int)Math.Round((_window.ActualWidth > 1 ? _window.ActualWidth : _window.Width) * scale);
            left = area.Left + (area.Width - width) / 2;
            top = area.Top + (int)Math.Round(18 * scale);
            (left, top) = ClampToWorkingArea(left, top);
        }

        MoveWindowPhysical(left, top);
    }

    public void SaveCurrentPosition()
    {
        var handle = Handle;
        if (handle != IntPtr.Zero && NativeMethods.GetWindowRect(handle, out var rect))
        {
            _savedLeft = rect.Left;
            _savedTop = rect.Top;
            _hasSavedPosition = true;
        }
    }

    public void ClampOntoMonitor()
    {
        var handle = Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var rect))
        {
            return;
        }

        var (left, top) = ClampToWorkingArea(rect.Left, rect.Top);
        if (left != rect.Left || top != rect.Top)
        {
            MoveWindowPhysical(left, top);
        }
    }

    private (int Left, int Top) ClampToWorkingArea(int left, int top)
    {
        var scale = GetMonitorScale(left, top);
        var width = (int)Math.Round((_window.ActualWidth > 1 ? _window.ActualWidth : _window.Width) * scale);
        var height = (int)Math.Round((_window.ActualHeight > 1 ? _window.ActualHeight : _window.Height) * scale);
        var area = Forms.Screen.FromPoint(new DrawingPoint(left, top)).WorkingArea;
        var clampedLeft = Math.Clamp(left, area.Left + 8, Math.Max(area.Left + 8, area.Right - width - 8));
        var clampedTop = Math.Clamp(top, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - height - 8));
        return (clampedLeft, clampedTop);
    }

    private void MoveWindowPhysical(int left, int top)
    {
        var handle = Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            left,
            top,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }

    private static double GetCursorMonitorScale()
    {
        return NativeMethods.GetCursorPos(out var point) ? GetMonitorScale(point.X, point.Y) : 1.0;
    }

    private static double GetMonitorScale(int x, int y)
    {
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point { X = x, Y = y }, NativeMethods.MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero
            && NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out var dpiX, out _) == 0
            && dpiX > 0)
        {
            return dpiX / 96.0;
        }

        return 1.0;
    }

    private static Forms.Screen GetCursorScreen()
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            return Forms.Screen.FromPoint(new DrawingPoint(point.X, point.Y));
        }

        return Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    }
}
