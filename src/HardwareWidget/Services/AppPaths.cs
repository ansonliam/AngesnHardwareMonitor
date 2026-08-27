using System.IO;

namespace HardwareWidget.Services;

/// <summary>
/// Single place that resolves every on-disk location, always via SpecialFolder.LocalApplicationData
/// so no Windows user path is ever hard-coded and nothing is written beside the executable.
/// </summary>
public static class AppPaths
{
    private const string AppFolderName = "HardwareWidget";

    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    public static string DataDirectory { get; } = Path.Combine(RootDirectory, "Data");

    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "Logs");

    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "hardware-history.db");

    public static string SettingsPath { get; } = Path.Combine(RootDirectory, "settings.json");

    public static string LogPath { get; } = Path.Combine(LogDirectory, "hardware-widget.log");
}
