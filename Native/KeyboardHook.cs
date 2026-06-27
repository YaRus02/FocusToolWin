using System.Runtime.InteropServices;

namespace FocusTool.Win.Native;

internal sealed class KeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private IntPtr _hook;
    private bool _disposed;

    public KeyboardHook()
    {
        _callback = HookCallback;
    }

    public event EventHandler<KeyboardHookKeyEventArgs>? KeyDown;

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
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _callback, module, 0);
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
            if (message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookStruct>(lParam);
                var args = new KeyboardHookKeyEventArgs((int)data.VkCode);
                KeyDown?.Invoke(this, args);
                if (args.Handled)
                {
                    return new IntPtr(1);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}

internal sealed class KeyboardHookKeyEventArgs : EventArgs
{
    public KeyboardHookKeyEventArgs(int virtualKey)
    {
        VirtualKey = virtualKey;
    }

    public int VirtualKey { get; }
    public bool Handled { get; set; }
}
