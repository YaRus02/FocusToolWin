using System.Windows.Threading;
using System.Windows.Input;
using FocusTool.Win.Models;
using FocusTool.Win.Native;
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
    private readonly KeyboardHook _deleteKeyHook = new();
    private readonly MouseHook _selectionMouseHook = new();
    private PinnedLensHostWindow? _selectedHost;

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
        _deleteKeyHook.KeyDown += OnDeleteKeyHookKeyDown;
        _selectionMouseHook.Clicked += OnSelectionMouseHookClicked;
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
        host.Selected += OnHostSelected;
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
            ClearSelection();
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

    public bool DeleteSelected()
    {
        if (_selectedHost is null || !_hosts.Contains(_selectedHost))
        {
            ClearSelection();
            return false;
        }

        RemoveHost(_selectedHost);
        ClearSelection();
        if (!_isDisposed())
        {
            _stateChanged();
        }

        return true;
    }

    public bool TryAdjustZoomAt(ScreenPoint point, int delta, ModifierKeys modifiers)
    {
        if (delta == 0
            || (modifiers & ModifierKeys.Control) == 0
            || (modifiers & ~ModifierKeys.Control) != 0)
        {
            return false;
        }

        var target = FindHostAt(point) ?? (_selectedHost is not null && _hosts.Contains(_selectedHost) ? _selectedHost : null);
        if (target is null)
        {
            return false;
        }

        target.AdjustZoom(Math.Sign(delta) * PinnedLensHostWindow.ZoomStep);
        SelectHost(target);
        return true;
    }

    public bool HasLiveControlTargetAt(ScreenPoint point)
    {
        return FindHostAt(point) is not null
            || _selectedHost is not null && _hosts.Contains(_selectedHost);
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

    private void OnHostSelected(object? sender, EventArgs e)
    {
        if (sender is PinnedLensHostWindow host)
        {
            SelectHost(host);
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
        host.Selected -= OnHostSelected;
        if (ReferenceEquals(_selectedHost, host))
        {
            ClearSelection();
        }

        host.Dispose();
        UpdateRefreshTimer();
    }

    private void SelectHost(PinnedLensHostWindow host)
    {
        if (!_hosts.Contains(host))
        {
            return;
        }

        if (ReferenceEquals(_selectedHost, host))
        {
            host.SetSelected(true);
            EnsureSelectionHooks();
            return;
        }

        _selectedHost?.SetSelected(false);
        _selectedHost = host;
        _selectedHost.SetSelected(true);
        EnsureSelectionHooks();
        _stateChanged();
    }

    private PinnedLensHostWindow? FindHostAt(ScreenPoint point)
    {
        for (var i = _hosts.Count - 1; i >= 0; i--)
        {
            if (_hosts[i].Contains(point))
            {
                return _hosts[i];
            }
        }

        return null;
    }

    private void ClearSelection()
    {
        if (_selectedHost is not null)
        {
            _selectedHost.SetSelected(false);
            _selectedHost = null;
        }

        _deleteKeyHook.Uninstall();
        _selectionMouseHook.Uninstall();
    }

    private void EnsureSelectionHooks()
    {
        if (!_deleteKeyHook.Install())
        {
            AppLog.Error("Could not install low-level keyboard hook for pinned lens deletion.");
        }

        if (!_selectionMouseHook.Install())
        {
            AppLog.Error("Could not install low-level mouse hook for pinned lens selection.");
        }
    }

    private void OnDeleteKeyHookKeyDown(object? sender, KeyboardHookKeyEventArgs e)
    {
        if (_selectedHost is null)
        {
            return;
        }

        if (e.VirtualKey is 0x08 or 0x2E)
        {
            e.Handled = true;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null)
            {
                dispatcher.BeginInvoke(DeleteSelected);
            }
            else
            {
                DeleteSelected();
            }
        }
    }

    private void OnSelectionMouseHookClicked(object? sender, MouseHookClickEventArgs e)
    {
        if (_selectedHost is null)
        {
            return;
        }

        if (_hosts.Any(host => host.Contains(e.Point)))
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(ClearSelection);
        }
        else
        {
            ClearSelection();
        }
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
        _deleteKeyHook.KeyDown -= OnDeleteKeyHookKeyDown;
        _selectionMouseHook.Clicked -= OnSelectionMouseHookClicked;
        _deleteKeyHook.Dispose();
        _selectionMouseHook.Dispose();
        foreach (var host in _hosts.ToArray())
        {
            RemoveHost(host);
        }
    }
}
