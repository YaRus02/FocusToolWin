using FocusTool.Win.Models;
using FocusTool.Win.Native;
using FocusTool.Win.Overlay;

namespace FocusTool.Win.Services;

// Owns the set of floating timers (multi-instance, like pinned lenses) and a single
// shared tick that refreshes them all. Per-timer interaction lives in TimerWindow;
// this layer only spawns, closes, and persists "defaults for the next new timer".
internal sealed class TimerController : IDisposable
{
    private const int CascadeStepPixels = 28;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private readonly Func<double> _clockMs;
    private readonly Func<TimerSettings> _defaultsProvider;
    private readonly Action<TimerSettings> _onDefaultsChanged;
    private readonly Action<string> _onLabelCommitted;
    private readonly Action _onActiveCountChanged;
    private readonly List<TimerWindow> _timers = [];
    private readonly System.Windows.Threading.DispatcherTimer _tick;
    private int _spawnIndex;
    private bool _disposed;

    public TimerController(
        Func<double> clockMs,
        Func<TimerSettings> defaultsProvider,
        Action<TimerSettings> onDefaultsChanged,
        Action<string> onLabelCommitted,
        Action onActiveCountChanged)
    {
        _clockMs = clockMs;
        _defaultsProvider = defaultsProvider;
        _onDefaultsChanged = onDefaultsChanged;
        _onLabelCommitted = onLabelCommitted;
        _onActiveCountChanged = onActiveCountChanged;
        _tick = new System.Windows.Threading.DispatcherTimer { Interval = TickInterval };
        _tick.Tick += OnTick;
    }

    public int ActiveCount => _timers.Count;

    public void NewTimer()
    {
        if (_disposed)
        {
            return;
        }

        var defaults = _defaultsProvider().Clone();
        defaults.Normalize();
        var model = new TimerModel(defaults, _clockMs());
        var window = new TimerWindow(model, defaults, _clockMs);
        window.DefaultsChanged += (_, _) => _onDefaultsChanged(window.CaptureDefaults());
        window.LabelCommitted += (_, label) => _onLabelCommitted(label);
        window.Closed += (_, _) => OnTimerClosed(window);
        _timers.Add(window);
        _onActiveCountChanged();

        window.Show();
        window.UpdateLayout();
        PositionNewTimer(window);
        window.FocusTimer();

        if (!_tick.IsEnabled)
        {
            _tick.Start();
        }
    }

    public void CloseAll()
    {
        foreach (var window in _timers.ToArray())
        {
            window.Close();
        }
    }

    public void ReassertTopmost()
    {
        foreach (var window in _timers)
        {
            if (window.IsVisible)
            {
                window.ReassertTopmost();
            }
        }
    }

    public void ReconcileToWorkingArea()
    {
        foreach (var window in _timers)
        {
            if (window.IsVisible)
            {
                window.ReconcileToWorkingArea();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tick.Stop();
        _tick.Tick -= OnTick;
        CloseAll();
    }

    private void OnTimerClosed(TimerWindow window)
    {
        _timers.Remove(window);
        _onActiveCountChanged();
        if (!_disposed)
        {
            // Remember the closed timer's mode/duration/style as the default for the next one.
            _onDefaultsChanged(window.CaptureDefaults());
        }

        if (_timers.Count == 0)
        {
            _tick.Stop();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        foreach (var window in _timers)
        {
            window.Refresh();
        }
    }

    private void PositionNewTimer(TimerWindow window)
    {
        var offset = (_spawnIndex++ % 6) * CascadeStepPixels;
        int x, y;
        if (NativeMethods.GetCursorPos(out var cursor))
        {
            x = cursor.X + 16 + offset;
            y = cursor.Y + 16 + offset;
        }
        else
        {
            var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
                ?? new System.Drawing.Rectangle(0, 0, 1280, 720);
            x = area.Left + 80 + offset;
            y = area.Top + 80 + offset;
        }

        window.MoveToPhysical(x, y);
    }
}
