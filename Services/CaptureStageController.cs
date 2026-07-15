using System.Text;
using System.Windows.Threading;
using FocusTool.Win.Capture;
using FocusTool.Win.Native;
using FocusTool.Win.Overlay;
using Windows.Graphics.Capture;
using Windows.Graphics;
using Forms = System.Windows.Forms;
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
    private enum PickedWindowResolution
    {
        NotFound,
        Ambiguous,
        Resolved,
    }

    private const int OverlayRefreshIntervalMs = 33;

    private readonly List<CaptureStageWindow> _stages = [];
    private readonly Func<ScreenRect, OverlaySnapshotData?> _overlayProvider;
    private readonly DispatcherTimer _overlayTimer;
    private Forms.Form? _pickerOwner;
    private bool _pickerOpen;
    private bool _disposed;

    public CaptureStageController(Func<ScreenRect, OverlaySnapshotData?> overlayProvider)
    {
        _overlayProvider = overlayProvider;
        _overlayTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(OverlayRefreshIntervalMs) };
        _overlayTimer.Tick += OnOverlayTimerTick;
    }

    private static void ShowInfo(string text)
    {
        Forms.MessageBox.Show(text, "Capture Stage", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
    }

    public bool HasStages => _stages.Count > 0;

    public async Task StartWithPickerAsync()
    {
        if (_disposed || _pickerOpen)
        {
            return;
        }

        if (!GraphicsCaptureSession.IsSupported())
        {
            ShowInfo("Windows Graphics Capture isn't supported on this system.");
            return;
        }

        _pickerOpen = true;
        GraphicsCaptureItem? item;
        try
        {
            var owner = ShowPickerOwner();
            item = await CaptureInterop.PickItemAsync(owner);
        }
        catch (Exception ex)
        {
            AppLog.Error("Capture Stage picker failed.", ex);
            return;
        }
        finally
        {
            HidePickerOwner();
            _pickerOpen = false;
        }

        if (item is null)
        {
            return;
        }

        var resolution = ResolveWindowFromPickedItem(item, out var sourceWindow);
        if (resolution == PickedWindowResolution.NotFound)
        {
            ShowInfo($"Couldn't map \"{item.DisplayName}\" to an application window. Pick an application window - capturing a whole screen is not supported yet.");
            return;
        }

        if (resolution == PickedWindowResolution.Ambiguous)
        {
            ShowInfo($"More than one application window matches \"{item.DisplayName}\". Capture Stage was not opened because its overlays couldn't be aligned reliably.");
            return;
        }

        StartStage(item, sourceWindow, GetWindowTitle(sourceWindow));
    }

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

        GraphicsCaptureItem item;
        try
        {
            item = CaptureInterop.CreateItemForWindow(target);
        }
        catch (Exception ex)
        {
            AppLog.Error("Capture Stage could not create a capture source for the focused window.", ex);
            return;
        }

        StartStage(item, target, GetWindowTitle(target));
    }

    private void StartStage(GraphicsCaptureItem item, IntPtr sourceWindow, string sourceTitle)
    {
        var stage = new CaptureStageWindow(item, sourceWindow, sourceTitle);
        stage.FormClosed += OnStageClosed;
        _stages.Add(stage);
        stage.Show();

        if (!_overlayTimer.IsEnabled)
        {
            _overlayTimer.Start();
        }
    }

    private IntPtr ShowPickerOwner()
    {
        if (_pickerOwner is not { IsDisposed: false })
        {
            _pickerOwner = new Forms.Form
            {
                Text = "FocusTool Capture Picker",
                ShowInTaskbar = false,
                FormBorderStyle = Forms.FormBorderStyle.FixedToolWindow,
                StartPosition = Forms.FormStartPosition.Manual,
                Size = new System.Drawing.Size(1, 1),
                Opacity = 0.01,
                TopMost = true,
            };
        }

        var cursor = Forms.Cursor.Position;
        _pickerOwner.Location = new System.Drawing.Point(cursor.X, cursor.Y);
        _pickerOwner.Show();
        _pickerOwner.Activate();
        return _pickerOwner.Handle;
    }

    private void HidePickerOwner()
    {
        if (_pickerOwner is { IsDisposed: false })
        {
            _pickerOwner.Hide();
        }
    }

    private static PickedWindowResolution ResolveWindowFromPickedItem(GraphicsCaptureItem item, out IntPtr window)
    {
        if (string.IsNullOrWhiteSpace(item.DisplayName))
        {
            window = IntPtr.Zero;
            return PickedWindowResolution.NotFound;
        }

        var candidates = new List<(IntPtr Window, int Score)>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (IsWindowCandidate(hWnd) && TryScorePickedWindow(hWnd, item.DisplayName, item.Size, out var score))
            {
                candidates.Add((hWnd, score));
            }

            return true;
        }, IntPtr.Zero);

        if (candidates.Count == 0)
        {
            window = IntPtr.Zero;
            return PickedWindowResolution.NotFound;
        }

        var ordered = candidates.OrderByDescending(candidate => candidate.Score).ToArray();
        if (ordered.Length > 1 && ordered[0].Score == ordered[1].Score)
        {
            window = IntPtr.Zero;
            return PickedWindowResolution.Ambiguous;
        }

        window = ordered[0].Window;
        return PickedWindowResolution.Resolved;
    }

    private static bool IsWindowCandidate(IntPtr window)
    {
        if (!NativeMethods.IsWindow(window) || !NativeMethods.IsWindowVisible(window))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        return GetWindowTitle(window).Length > 0;
    }

    private static bool TryScorePickedWindow(IntPtr window, string displayName, SizeInt32 itemSize, out int score)
    {
        score = 0;
        var title = GetWindowTitle(window);
        if (string.Equals(title, displayName, StringComparison.CurrentCultureIgnoreCase))
        {
            score += 1000;
        }
        else if ((displayName.Length >= 4 && title.Contains(displayName, StringComparison.CurrentCultureIgnoreCase))
            || (title.Length >= 4 && displayName.Contains(title, StringComparison.CurrentCultureIgnoreCase)))
        {
            // Require a non-trivial overlap so short/generic titles don't false-match.
            score += 500;
        }
        else
        {
            return false;
        }

        if (!TryGetWindowContentBounds(window, out var bounds))
        {
            return false;
        }

        var widthDelta = Math.Abs(bounds.Right - bounds.Left - itemSize.Width);
        var heightDelta = Math.Abs(bounds.Bottom - bounds.Top - itemSize.Height);
        if (widthDelta <= 4 && heightDelta <= 4)
        {
            score += 300;
        }
        else if (widthDelta <= 32 && heightDelta <= 32)
        {
            score += 120;
        }

        return true;
    }

    private static bool TryGetWindowContentBounds(IntPtr window, out NativeMethods.Rect bounds)
    {
        return NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DwmwaExtendedFrameBounds, out bounds, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Rect>()) == 0
            || NativeMethods.GetWindowRect(window, out bounds);
    }

    private static string GetWindowTitle(IntPtr window)
    {
        var length = NativeMethods.GetWindowTextLength(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        return NativeMethods.GetWindowText(window, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
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
            if (stage.SourceAvailable && stage.TryGetSourceRect(out var rect) && _overlayProvider(rect) is { } snapshot)
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
        _pickerOwner?.Dispose();
        _pickerOwner = null;
        foreach (var stage in _stages.ToArray())
        {
            stage.FormClosed -= OnStageClosed;
            stage.Close();
            stage.Dispose();
        }

        _stages.Clear();
    }
}
