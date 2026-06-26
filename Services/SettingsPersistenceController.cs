using System.Windows.Threading;
using FocusTool.Win.Models;

namespace FocusTool.Win.Services;

internal sealed class SettingsPersistenceController : IDisposable
{
    private readonly SettingsStore _store = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<bool> _isDisposed;

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
        // A snapshot is serialized so the background write never races UI-thread
        // mutations of the live Settings; Dispose still flushes synchronously.
        var snapshot = _settingsProvider().Clone();
        _ = Task.Run(() => _store.Save(snapshot));
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTick;
    }
}
