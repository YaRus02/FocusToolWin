using System.IO;
using System.Text.Json;
using FocusTool.Win.Models;

namespace FocusTool.Win.Services;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    // Serializes concurrent writes: the debounced save now runs on a thread-pool
    // thread, and Dispose still saves synchronously on the UI thread.
    private readonly object _saveLock = new();

    public string SettingsFilePath { get; }
    public bool WasCreatedOnLoad { get; private set; }

    public SettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        SettingsFilePath = Path.Combine(appData, "FocusTool", "settings.json");
    }

    public AppSettings Load()
    {
        WasCreatedOnLoad = false;

        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty(nameof(AppSettings.Shortcuts), out var shortcutsElement)
                    || !shortcutsElement.TryGetProperty(nameof(ShortcutSettings.LayoutVersion), out _))
                {
                    settings.Shortcuts ??= new ShortcutSettings();
                    settings.Shortcuts.LayoutVersion = 0;
                }

                settings.Normalize();
                return settings;
            }
        }
        catch (Exception ex)
        {
            // A broken settings file should not prevent the pointer from starting.
            AppLog.Error($"Could not load settings from {SettingsFilePath}", ex);
        }

        WasCreatedOnLoad = true;
        var defaults = new AppSettings();
        Save(defaults);
        return defaults;
    }

    public void Save(AppSettings settings)
    {
        lock (_saveLock)
        {
            string? temporaryPath = null;
            try
            {
                settings.Normalize();
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(settings, JsonOptions);
                temporaryPath = SettingsFilePath + $".{Environment.ProcessId}.tmp";
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, SettingsFilePath, overwrite: true);
                temporaryPath = null;
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not save settings", ex);
            }
            finally
            {
                if (temporaryPath is not null)
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch
                    {
                        // Cleanup must not hide the original settings error.
                    }
                }
            }
        }
    }
}
