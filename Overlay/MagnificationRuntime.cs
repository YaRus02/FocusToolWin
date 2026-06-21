using System.Runtime.InteropServices;
using FocusTool.Win.Native;
using FocusTool.Win.Services;

namespace FocusTool.Win.Overlay;

internal sealed class MagnificationRuntime : IDisposable
{
    private static readonly object RuntimeLock = new();
    private static int _references;
    private bool _acquired;

    private MagnificationRuntime()
    {
    }

    public static MagnificationRuntime? Acquire(string ownerName)
    {
        lock (RuntimeLock)
        {
            if (_references == 0 && !NativeMethods.MagInitialize())
            {
                AppLog.Error(
                    $"Could not initialize Windows Magnification API for {ownerName}.",
                    new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                return null;
            }

            _references++;
            return new MagnificationRuntime { _acquired = true };
        }
    }

    public void Dispose()
    {
        if (!_acquired)
        {
            return;
        }

        lock (RuntimeLock)
        {
            if (_references > 0)
            {
                _references--;
                if (_references == 0)
                {
                    NativeMethods.MagUninitialize();
                }
            }
        }

        _acquired = false;
    }
}
