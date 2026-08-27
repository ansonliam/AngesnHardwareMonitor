using System.IO;

namespace AngesnHardwareWidget.Services;

/// <summary>
/// Single place that resolves every on-disk location, always via SpecialFolder.LocalApplicationData
/// so no Windows user path is ever hard-coded and nothing is written beside the executable.
/// </summary>
public static class AppPaths
{
    private const string AppFolderName = "AngesnHardwareWidget";

    /// <summary>The folder this app used before it was renamed. Migrated once, then ignored.</summary>
    private const string LegacyAppFolderName = "HardwareWidget";

    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    public static string DataDirectory { get; } = Path.Combine(RootDirectory, "Data");

    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "Logs");

    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "hardware-history.db");

    public static string SettingsPath { get; } = Path.Combine(RootDirectory, "settings.json");

    public static string LogPath { get; } = Path.Combine(LogDirectory, "hardware-widget.log");

    private static string LegacyRootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LegacyAppFolderName);

    /// <summary>
    /// Moves the previous app folder to the current one, once, if the new one does not exist yet.
    ///
    /// Without this the rename would silently orphan the user's settings and their entire metric
    /// history: the app would start from defaults next to a folder full of data it no longer looks
    /// at, and the history gap would be permanent. Must run before anything opens a file under
    /// RootDirectory, so it is called first thing at startup.
    /// </summary>
    public static void MigrateLegacyDataIfNeeded()
    {
        try
        {
            if (Directory.Exists(RootDirectory) || !Directory.Exists(LegacyRootDirectory))
            {
                return;
            }

            // Move rather than copy: two folders of divergent history would be worse than one, and
            // a move is atomic within the same volume.
            Directory.Move(LegacyRootDirectory, RootDirectory);
            AppLog.Info($"Migrated app data from '{LegacyRootDirectory}' to '{RootDirectory}'.");
        }
        catch (Exception exception)
        {
            // Starting fresh is a bad outcome but not a fatal one; refusing to start would be worse.
            AppLog.Warn(
                $"Could not migrate app data from '{LegacyRootDirectory}': {exception.Message}. " +
                "The app will start with default settings; the old folder is untouched.");
        }
    }
}
