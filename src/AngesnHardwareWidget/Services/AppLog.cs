using System.Diagnostics;
using System.IO;
using System.Text;

namespace AngesnHardwareWidget.Services;

/// <summary>
/// Deliberately tiny append-only file log. Its whole job is diagnosing hardware-specific sensor
/// reports, so startup dumps and sensor selections must survive to disk; it must never throw,
/// because a logging failure cannot be allowed to take down monitoring.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object SyncRoot = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception.GetType().Name}: {exception.Message}");

    /// <summary>Writes a raw block (used for the startup sensor dump) without per-line stamping.</summary>
    public static void Block(string title, string body)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"=== {title} ===");
        builder.AppendLine(body.TrimEnd());
        Append(builder.ToString());
    }

    private static void Write(string level, string message) =>
        Append($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}");

    private static void Append(string text)
    {
        Debug.Write(text);
        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                RollIfOversized();
                File.AppendAllText(AppPaths.LogPath, text, Encoding.UTF8);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void RollIfOversized()
    {
        var current = new FileInfo(AppPaths.LogPath);
        if (!current.Exists || current.Length < MaxBytes)
        {
            return;
        }

        File.Move(AppPaths.LogPath, AppPaths.LogPath + ".1", overwrite: true);
    }
}
