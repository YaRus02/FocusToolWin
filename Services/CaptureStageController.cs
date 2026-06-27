using FocusTool.Win.Capture;
using FocusTool.Win.Native;
using Windows.Graphics.Capture;
using FormClosedEventArgs = System.Windows.Forms.FormClosedEventArgs;

namespace FocusTool.Win.Services;

/// <summary>
/// Owns the Capture Stage windows: standalone mirror windows that screen-share
/// and recording tools can grab via "Share window" while they carry the source
/// window's live content. v1 mirrors a single source per stage, view-only.
/// </summary>
internal sealed class CaptureStageController : IDisposable
{
    private readonly List<CaptureStageWindow> _stages = [];
    private bool _disposed;

    public bool HasStages => _stages.Count > 0;

    public void StartForWindow(IntPtr sourceWindow)
    {
        if (_disposed || sourceWindow == IntPtr.Zero)
        {
            return;
        }

        if (!GraphicsCaptureSession.IsSupported())
        {
            AppLog.Error("Capture Stage: Windows Graphics Capture is not supported on this system.");
            return;
        }

        var target = NativeMethods.GetAncestor(sourceWindow, NativeMethods.GaRoot);
        if (target == IntPtr.Zero)
        {
            target = sourceWindow;
        }

        if (!NativeMethods.IsWindow(target))
        {
            return;
        }

        var stage = new CaptureStageWindow(target);
        stage.FormClosed += OnStageClosed;
        _stages.Add(stage);
        stage.Show();
    }

    public void CloseAll()
    {
        foreach (var stage in _stages.ToArray())
        {
            stage.Close();
        }
    }

    private void OnStageClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is not CaptureStageWindow stage)
        {
            return;
        }

        stage.FormClosed -= OnStageClosed;
        _stages.Remove(stage);
        stage.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var stage in _stages.ToArray())
        {
            stage.FormClosed -= OnStageClosed;
            stage.Close();
            stage.Dispose();
        }

        _stages.Clear();
    }
}
