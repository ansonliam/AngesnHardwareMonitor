using System.Diagnostics;
using System.IO;

namespace AngesnHardwareWidget.Services;

/// <summary>
/// Places a Start Menu shortcut for the widget, the same way the AI Usage Monitor does, so typing
/// the app's name into Windows Search finds and launches it. A plain built exe has no installer to
/// register this, so the app does it for itself on startup.
/// </summary>
public sealed class StartMenuShortcutService
{
    private const string ShortcutName = "Angesn Hardware Widget.lnk";

    /// <summary>
    /// Per-user Start Menu Programs folder, not the machine-wide one: writing there needs no
    /// elevation, and every instance of this app already runs as the signed-in user.
    /// </summary>
    private static string ShortcutPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        ShortcutName);

    /// <summary>
    /// Creates the shortcut if it is missing, and re-creates it if it points at a different
    /// executable than the one running now -- the same "moved or rebuilt" case
    /// <see cref="WindowsStartupService.EnsurePathCurrent"/> handles for the logon task. Safe to
    /// call on every startup: it is a no-op once the shortcut already points at the right place.
    /// </summary>
    public void EnsureCurrent()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return;
            }

            if (File.Exists(ShortcutPath) && TargetMatches(executablePath))
            {
                return;
            }

            CreateShortcut(executablePath);
        }
        catch (Exception exception)
        {
            // A missing Start Menu entry is a rough edge, not a reason to fail startup.
            AppLog.Warn($"Start Menu shortcut could not be created: {exception.Message}");
        }
    }

    /// <summary>
    /// Reads the shortcut's target back out via the same WScript.Shell COM object used to write
    /// it, so there is only one way of talking to .lnk files in this class rather than a second,
    /// native one just for reading.
    /// </summary>
    private static bool TargetMatches(string executablePath)
    {
        var script = $$"""
            $shell = New-Object -ComObject WScript.Shell
            $shortcut = $shell.CreateShortcut('{{Escape(ShortcutPath)}}')
            [Console]::Out.Write($shortcut.TargetPath)
            """;

        var target = RunPowerShell(script);
        return string.Equals(target.Trim(), executablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static void CreateShortcut(string executablePath)
    {
        var workingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;

        var script = $$"""
            $shell = New-Object -ComObject WScript.Shell
            $shortcut = $shell.CreateShortcut('{{Escape(ShortcutPath)}}')
            $shortcut.TargetPath = '{{Escape(executablePath)}}'
            $shortcut.WorkingDirectory = '{{Escape(workingDirectory)}}'
            $shortcut.IconLocation = '{{Escape(executablePath)}}'
            $shortcut.Description = 'Angesn Hardware Widget'
            $shortcut.Save()
            """;

        RunPowerShell(script);
        AppLog.Info($"Start Menu shortcut created for {executablePath}.");
    }

    /// <summary>PowerShell single-quoted strings only need an embedded quote doubled.</summary>
    private static string Escape(string value) => value.Replace("'", "''");

    private static string RunPowerShell(string script)
    {
        var startInfo = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command -")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return string.Empty;
        }

        process.StandardInput.Write(script);
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEnd();

        // Bounded: this must never hang app startup.
        process.WaitForExit(10_000);
        return output;
    }
}
