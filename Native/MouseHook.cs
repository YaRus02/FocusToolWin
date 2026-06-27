using System.Runtime.InteropServices;
using FocusTool.Win.Overlay;

namespace FocusTool.Win.Native;

internal sealed class MouseHook : IDisposable
{
    private readonly NativeMethods.LowLevelMouseProc _callback;
    private IntPtr _hook;
    private bool _disposed;

    public MouseHook()
    {
        _callback = HookCallback;
    }

    public event EventHandler<MouseHookClickEventArgs>? Clicked;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public bool Install()
    {
        if (_disposed)
        {
            return false;
        }

        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        var module = NativeMethods.GetModuleHandle(null);
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _callback, module, 0);
        return _hook != IntPtr.Zero;
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Uninstall();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            if (message is NativeMethods.WmLButtonDown or NativeMethods.WmRButtonDown)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MouseHookStruct>(lParam);
                var button = message == NativeMethods.WmRButtonDown
                    ? CursorClickButton.Right
                    : CursorClickButton.Left;
                Clicked?.Invoke(
                    this,
                    new MouseHookClickEventArgs(button, new ScreenPoint(data.Point.X, data.Point.Y)));
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}

internal sealed class MouseHookClickEventArgs : EventArgs
{
    public MouseHookClickEventArgs(CursorClickButton button, ScreenPoint point)
    {
        Button = button;
        Point = point;
    }

    public CursorClickButton Button { get; }
    public ScreenPoint Point { get; }
}
