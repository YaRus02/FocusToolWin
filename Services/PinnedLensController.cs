using System.Windows.Threading;
using FocusTool.Win.Models;
using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

internal sealed class PinnedLensController : IDisposable
{
    private readonly List<PinnedLensHostWindow> _hosts = [];
    private readonly DispatcherTimer _refreshTimer;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<IReadOnlyList<IntPtr>> _excludedWindowsProvider;
    private readonly Func<bool> _isDisposed;
    private readonly Func<Task> _waitForScreenRefreshAsync;
    private readonly Action _reassertOverlayTopmost;
    private readonly Action<string, string> _showMessage;
    private readonly Action _stateChanged;

    public PinnedLensController(
        Func<AppSettings> settingsProvider,
        Func<IReadOnlyList<IntPtr>> excludedWindowsProvider,
        Func<bool> isDisposed,
        Func<Task> waitForScreenRefreshAsync,
        Action reassertOverlayTopmost,
        Action<string, string> showMessage,
        Action stateChanged)
    {
        _settingsProvider = settingsProvider;
        _excludedWindowsProvider = excludedWindowsProvider;
        _isDisposed = isDisposed;
        _waitForScreenRefreshAsync = waitForScreenRefreshAsync;
        _reassertOverlayTopmost = reassertOverlayTopmost;
        _showMessage = showMessage;
        _stateChanged = stateChanged;
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Render);
        _refreshTimer.Tick += OnRefreshTick;
        UpdateRefreshInterval();
    }

    public IReadOnlyList<PinnedLensHostWindow> Hosts => _hosts;
    public int Count => _hosts.Count;
    public bool HasLenses => _hosts.Count > 0;

    public void Open(ScreenRect sourceRect)
    {
        var host = new PinnedLensHostWindow(sourceRect, _settingsProvider(), CloseAll, CaptureFreezeFrameAsync);
        if (!host.IsAvailable)
        {
            host.Dispose();
            _showMessage("Pinned lens failed", "Could not create the pinned lens window.");
            return;
        }

        _hosts.Add(host);
        host.Closed += OnHostClosed;
        host.FreezeStateChanged += OnHostFreezeStateChanged;
        host.Show();
        UpdateHosts();
        UpdateRefreshTimer();
        _reassertOverlayTopmost();
        _stateChanged();
    }

    public void CloseAll()
    {
        _refreshTimer.Stop();
        if (_hosts.Count == 0)
        {
            return;
        }

        foreach (var host in _hosts.ToArray())
        {
            RemoveHost(host);
        }

        if (!_isDisposed())
        {
            _stateChanged();
        }
    }

    public void UpdateRefreshInterval()
    {
        var fps = Math.Clamp(_settingsProvider().PinnedLensRefreshFps, 10, 60);
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
    }

    public void UpdateHosts()
    {
        if (_hosts.Count == 0)
        {
            _refreshTimer.Stop();
            return;
        }

        if (!_hosts.Any(host => !host.IsFrozen))
        {
            _refreshTimer.Stop();
            return;
        }

        var excludedWindows = _excludedWindowsProvider();
        foreach (var host in _hosts.ToArray())
        {
            if (host.IsFrozen)
            {
                continue;
            }

            host.UpdateLens(excludedWindows);
        }
    }

    public void ReconcileToWorkingArea()
    {
        foreach (var host in _hosts.ToArray())
        {
            host.ReconcileToWorkingArea();
        }
    }

    public void HideForBoard()
    {
        foreach (var host in _hosts)
        {
            host.HideForDesktopCapture();
        }
    }

    public void RestoreAfterBoard()
    {
        if (_isDisposed())
        {
            return;
        }

        foreach (var host in _hosts)
        {
            host.RestoreAfterDesktopCapture();
        }
    }

    public void ReassertTopmost()
    {
        foreach (var host in _hosts)
        {
            host.ReassertTopmost();
        }
    }

    public void ReassertContextMenuTopmost()
    {
        foreach (var host in _hosts)
        {
            host.ReassertContextMenuTopmost();
        }
    }

    private async Task<bool> CaptureFreezeFrameAsync(PinnedLensHostWindow target, Func<bool> capture)
    {
        if (_isDisposed() || !_hosts.Contains(target))
        {
            return false;
        }

        var hiddenHosts = new List<PinnedLensHostWindow>();
        foreach (var host in _hosts.ToArray())
        {
            if (host.HideForDesktopCapture())
            {
                hiddenHosts.Add(host);
            }
        }

        await _waitForScreenRefreshAsync();

        try
        {
            return !_isDisposed() && _hosts.Contains(target) && capture();
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not freeze the pinned lens frame.", ex);
            return false;
        }
        finally
        {
            foreach (var host in hiddenHosts)
            {
                if (_hosts.Contains(host))
                {
                    host.RestoreAfterDesktopCapture();
                }
            }

            _reassertOverlayTopmost();
        }
    }

    private void OnHostClosed(object? sender, EventArgs e)
    {
        if (sender is PinnedLensHostWindow host)
        {
            RemoveHost(host);
            if (!_isDisposed())
            {
                _stateChanged();
            }
        }
    }

    private void OnHostFreezeStateChanged(object? sender, EventArgs e)
    {
        UpdateRefreshTimer();
        if (!_isDisposed())
        {
            _stateChanged();
        }
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (_isDisposed())
        {
            return;
        }

        UpdateHosts();
    }

    private void RemoveHost(PinnedLensHostWindow host)
    {
        if (!_hosts.Remove(host))
        {
            return;
        }

        host.Closed -= OnHostClosed;
        host.FreezeStateChanged -= OnHostFreezeStateChanged;
        host.Dispose();
        UpdateRefreshTimer();
    }

    private void UpdateRefreshTimer()
    {
        if (_isDisposed() || !_hosts.Any(host => !host.IsFrozen))
        {
            _refreshTimer.Stop();
            return;
        }

        _refreshTimer.Start();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTick;
        foreach (var host in _hosts.ToArray())
        {
            RemoveHost(host);
        }
    }
}
