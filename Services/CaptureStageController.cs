using System.Windows.Threading;
using FocusTool.Win.Capture;
using FocusTool.Win.Native;
using FocusTool.Win.Overlay;
using Windows.Graphics.Capture;
using FormClosedEventArgs = System.Windows.Forms.FormClosedEventArgs;

namespace FocusTool.Win.Services;

/// <summary>
/// Owns the Capture Stage windows: standalone mirror windows that screen-share
/// and recording tools can grab via "Share window" while they carry the source
/// window's live content plus FocusTool's overlays. v1 mirrors one source per
/// stage. Overlays are refreshed on a throttled UI-thread timer (event/region
/// optimization is a later step).
/// </summary>
internal sealed class CaptureStageController : IDisposable
{
    private const int OverlayRefreshIntervalMs = 16;

    private readonly List<CaptureStageWindow> _stages = [];
    private readonly Func<ScreenRect, OverlaySnapshotData?> _overlayProvider;
    private readonly DispatcherTimer _overlayTimer;
    private bool _disposed;

    public CaptureStageController(Func<ScreenRect, OverlaySnapshotData?> overlayProvider)
    {
        _overlayProvider = overlayProvider;
        _overlayTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(OverlayRefreshIntervalMs) };
        _overlayTimer.Tick += OnOverlayTimerTick;
    }

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

        if (!_overlayTimer.IsEnabled)
        {
            _overlayTimer.Start();
        }
    }

    public void CloseAll()
    {
        foreach (var stage in _stages.ToArray())
        {
            stage.Close();
        }
    }

    private void OnOverlayTimerTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        foreach (var stage in _stages)
        {
            if (stage.TryGetSourceRect(out var rect) && _overlayProvider(rect) is { } snapshot)
            {
                stage.UpdateOverlaySnapshot(snapshot);
            }
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

        if (_stages.Count == 0)
        {
            _overlayTimer.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _overlayTimer.Stop();
        _overlayTimer.Tick -= OnOverlayTimerTick;
        foreach (var stage in _stages.ToArray())
        {
            stage.FormClosed -= OnStageClosed;
            stage.Close();
            stage.Dispose();
        }

        _stages.Clear();
    }
}
