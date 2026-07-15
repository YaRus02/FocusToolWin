namespace FocusTool.Win.Native;

internal sealed class WindowCaptureExclusionScope : IDisposable
{
    private readonly IReadOnlyList<(IntPtr Handle, uint PreviousAffinity)> _windows;
    private bool _disposed;

    private WindowCaptureExclusionScope(IReadOnlyList<(IntPtr Handle, uint PreviousAffinity)> windows)
    {
        _windows = windows;
    }

    public static WindowCaptureExclusionScope? TryCreate(IEnumerable<IntPtr> windowHandles)
    {
        var changed = new List<(IntPtr Handle, uint PreviousAffinity)>();
        foreach (var handle in windowHandles.Where(handle => handle != IntPtr.Zero).Distinct())
        {
            if (!NativeMethods.GetWindowDisplayAffinity(handle, out var previousAffinity)
                || !NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaExcludeFromCapture))
            {
                Restore(changed);
                return null;
            }

            changed.Add((handle, previousAffinity));
        }

        return new WindowCaptureExclusionScope(changed);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Restore(_windows);
    }

    private static void Restore(IEnumerable<(IntPtr Handle, uint PreviousAffinity)> windows)
    {
        foreach (var (handle, previousAffinity) in windows.Reverse())
        {
            if (NativeMethods.IsWindow(handle))
            {
                _ = NativeMethods.SetWindowDisplayAffinity(handle, previousAffinity);
            }
        }
    }
}
