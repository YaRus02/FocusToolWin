using System.IO;
using System.Threading;

namespace FocusTool.Win.Services;

/// <summary>
/// Minimal append-only file log at %APPDATA%\FocusTool\log.txt for the few
/// places that otherwise swallow errors (settings IO, unhandled exceptions).
/// Logging never throws and never blocks startup.
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 512 * 1024;
    private const string MutexName = "FocusTool.AppLog";
    private static readonly object Gate = new();
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FocusTool",
        "log.txt");

    public static void Error(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                using var mutex = new Mutex(false, MutexName);
                var lockTaken = false;
                try
                {
                    lockTaken = mutex.WaitOne(TimeSpan.FromMilliseconds(100));
                    if (!lockTaken)
                    {
                        return;
                    }

                    WriteError(message, exception);
                }
                finally
                {
                    if (lockTaken)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }
        catch
        {
            // Logging must never throw or interfere with the app.
        }
    }

    private static void WriteError(string message, Exception? exception)
    {
        var directory = Path.GetDirectoryName(LogFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxBytes)
        {
            File.WriteAllText(LogFilePath, string.Empty);
        }

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}"
            + (exception is null ? string.Empty : Environment.NewLine + exception);

        using var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(line);
    }
}
