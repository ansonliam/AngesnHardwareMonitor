using System.IO;
using System.Text.Json;
using HardwareWidget.Settings;

namespace HardwareWidget.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON. Same shape as the AI Usage Monitor stores:
/// temp file plus atomic File.Move, so a crash or power loss mid-write cannot corrupt settings.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly object _syncRoot = new();
    private readonly string _settingsPath;

    private AppSettings _current;

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? AppPaths.SettingsPath;
        _current = LoadFromDisk();
    }

    /// <summary>Raised after a successful save so the scheduler can rebuild its schedule.</summary>
    public event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>A snapshot of the active settings. Always a copy, so callers cannot mutate the
    /// live instance behind the scheduler's back.</summary>
    public AppSettings Current
    {
        get
        {
            lock (_syncRoot)
            {
                return _current.Clone();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        var normalized = settings.Clone().Normalized();

        lock (_syncRoot)
        {
            _current = normalized;
            WriteToDisk(normalized);
        }

        SettingsChanged?.Invoke(this, normalized.Clone());
    }

    private AppSettings LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings())
                .Normalized();
        }
        catch (JsonException exception)
        {
            AppLog.Warn($"Settings file is not valid JSON; using defaults: {exception.Message}");
            return new AppSettings();
        }
        catch (IOException exception)
        {
            AppLog.Warn($"Settings file could not be read; using defaults: {exception.Message}");
            return new AppSettings();
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Warn($"Settings file is not accessible; using defaults: {exception.Message}");
            return new AppSettings();
        }
    }

    private void WriteToDisk(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (IOException exception)
        {
            AppLog.Warn($"Settings could not be saved: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Warn($"Settings could not be saved: {exception.Message}");
        }
    }
}
