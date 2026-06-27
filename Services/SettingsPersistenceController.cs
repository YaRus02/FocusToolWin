using System.Windows.Threading;
using FocusTool.Win.Models;

namespace FocusTool.Win.Services;

internal sealed class SettingsPersistenceController : IDisposable
{
    private readonly SettingsStore _store = new();
    private readonly object _saveQueueGate = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<bool> _isDisposed;
    private Task _saveQueue = Task.CompletedTask;

    public SettingsPersistenceController(Func<AppSettings> settingsProvider, Func<bool> isDisposed)
    {
        _settingsProvider = settingsProvider;
        _isDisposed = isDisposed;
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _saveTimer.Tick += OnSaveTick;
    }

    public string SettingsFilePath => _store.SettingsFilePath;
    public bool WasCreatedOnLoad => _store.WasCreatedOnLoad;

    public AppSettings Load()
    {
        return _store.Load();
    }

    public void SaveDebounced()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Flush()
    {
        _saveTimer.Stop();
        Task pendingSave;
        lock (_saveQueueGate)
        {
            pendingSave = _saveQueue;
        }

        pendingSave.GetAwaiter().GetResult();
        _store.Save(_settingsProvider());
    }

    private void OnSaveTick(object? sender, EventArgs e)
    {
        if (_isDisposed())
        {
            return;
        }

        _saveTimer.Stop();

        // Write off the UI thread so a slow disk / AV scan can't hitch the overlay.
        // Saves are chained in snapshot order so an older background write cannot
        // complete after a newer one and overwrite it with stale settings.
        var snapshot = _settingsProvider().Clone();
        lock (_saveQueueGate)
        {
            _saveQueue = _saveQueue.ContinueWith(
                _ => _store.Save(snapshot),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTick;
    }
}
