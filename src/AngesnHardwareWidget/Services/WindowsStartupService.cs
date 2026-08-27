using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AngesnHardwareWidget.Services;

/// <summary>
/// Starts the widget at logon via a Scheduled Task registered with highest privileges.
///
/// This deliberately does NOT use HKCU\...\Run, which is how the AI Usage Monitor does it. That key
/// cannot launch an elevated process: nothing at logon can answer a UAC prompt on the user's
/// behalf, so Windows either skips the entry or the launch fails. This app requires administrator
/// rights for its sensor driver, so the Run key is not an option.
///
/// A logon-triggered task with RunLevel HighestAvailable is the supported alternative, and it also
/// answers the "stop asking me for UAC every time" problem: a task started this way is elevated
/// with no consent dialog. Launching the widget through the task is therefore the prompt-free route.
///
/// It does NOT bypass anything else. Defender, the vulnerable-driver blocklist and SmartScreen all
/// still apply, so this has no bearing on whether WinRing0 loads and CPU temperature reads.
///
/// The task is the single source of truth for whether startup is enabled -- there is no mirrored
/// flag in settings.json, so the two cannot disagree after someone edits the task directly.
/// </summary>
public sealed class WindowsStartupService
{
    /// <summary>
    /// Registered under the user's own Task Scheduler folder rather than at the root, alongside
    /// their other tasks. schtasks creates the folder if it does not exist.
    /// </summary>
    private const string TaskName = @"Anson\AngesnHardwareWidget.Startup";

    /// <summary>
    /// Task paths earlier builds used, cleaned up whenever the current one is written. Without this
    /// the app rename would leave the old task still registered and still launching the widget, so
    /// logon would start two of them.
    /// </summary>
    private static readonly string[] LegacyTaskNames =
    [
        // Before the app was renamed to AngesnHardwareWidget.
        @"Anson\HardwareWidget.Startup",

        // Before the task moved out of the root of the task library.
        "HardwareWidget.Startup",
        "AngesnHardwareWidget.Startup",
    ];

    /// <summary>
    /// Small logon delay. Starting at the exact moment of sign-in competes with the shell for a
    /// heavily contended disk, and the sensor backend has a kernel driver to bring up.
    /// </summary>
    private const string LogonDelay = "PT10S";

    public bool IsEnabled() => Exists(TaskName) || LegacyTaskNames.Any(Exists);

    private static bool Exists(string taskName) =>
        RunSchTasks($"/Query /TN \"{taskName}\"").ExitCode == 0;

    /// <summary>
    /// Registers or removes the logon task. Returns false with a reason rather than throwing, so the
    /// settings dialog can report it and leave the checkbox showing the real state.
    /// </summary>
    public bool TrySetEnabled(bool enabled, out string? error)
    {
        error = null;

        try
        {
            return enabled ? TryRegister(out error) : TryUnregister(out error);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            AppLog.Error($"Startup task could not be {(enabled ? "registered" : "removed")}", exception);
            return false;
        }
    }

    /// <summary>
    /// Re-registers the task if it points at a different executable than the one running now.
    /// Called at startup: the widget is run from wherever it was built or installed, so an update
    /// or a move would otherwise leave the task launching a path that no longer exists. Registering
    /// is idempotent (/F overwrites), so this is safe to call every launch.
    /// </summary>
    public void EnsurePathCurrent()
    {
        try
        {
            if (!IsEnabled())
            {
                return;
            }

            var current = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(current))
            {
                return;
            }

            // A task left at the old root location is migrated by simply re-registering, which
            // writes the new path and removes the legacy one.
            if (!Exists(TaskName) && LegacyTaskNames.Any(Exists))
            {
                AppLog.Info($"Migrating the startup task to '{TaskName}'.");
                TryRegister(out _);
                return;
            }

            var registered = GetRegisteredCommand();
            if (registered is null
                || string.Equals(registered, current, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AppLog.Info($"Startup task points at '{registered}' but this build runs from '{current}'; re-registering.");
            TryRegister(out _);
        }
        catch (Exception exception)
        {
            // Never let this stop the app from starting.
            AppLog.Warn($"Startup task path check failed: {exception.Message}");
        }
    }

    /// <summary>The executable the registered task launches, or null if it cannot be determined.</summary>
    private string? GetRegisteredCommand()
    {
        var result = RunSchTasks($"/Query /TN \"{TaskName}\" /XML");
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            return null;
        }

        var match = Regex.Match(result.Output, "<Command>(?<path>.*?)</Command>", RegexOptions.Singleline);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["path"].Value
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Trim()
            .Trim('"');
    }

    private bool TryRegister(out string? error)
    {
        error = null;

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            error = "The application path could not be determined.";
            return false;
        }

        // schtasks /XML requires UTF-16; it rejects a UTF-8 file outright.
        //
        // The filename is a literal rather than derived from TaskName: TaskName contains a folder
        // separator now, which Path.Combine would turn into a subdirectory that does not exist.
        var xmlPath = Path.Combine(Path.GetTempPath(), $"AngesnHardwareWidget-task-{Guid.NewGuid():N}.xml");

        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(executablePath), Encoding.Unicode);

            // /F makes this an upsert, which is what keeps repeated enables and path fixes safe.
            var result = RunSchTasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
            if (result.ExitCode == 0)
            {
                AppLog.Info($"Startup task '{TaskName}' registered for {executablePath}.");
                RemoveLegacyTasks();
                return true;
            }

            error = Describe(result);
            AppLog.Warn($"Startup task registration failed: {error}");
            return false;
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private bool TryUnregister(out string? error)
    {
        error = null;

        var result = RunSchTasks($"/Delete /TN \"{TaskName}\" /F");
        RemoveLegacyTasks();

        // Deleting a task that is already gone is success from the caller's point of view, so the
        // check is whether anything is still registered rather than the exit code alone.
        if (!IsEnabled())
        {
            AppLog.Info("Startup task removed.");
            return true;
        }

        error = Describe(result);
        AppLog.Warn($"Startup task removal failed: {error}");
        return false;
    }

    private static void RemoveLegacyTasks()
    {
        foreach (var legacy in LegacyTaskNames)
        {
            if (!Exists(legacy))
            {
                continue;
            }

            var result = RunSchTasks($"/Delete /TN \"{legacy}\" /F");
            AppLog.Info(result.ExitCode == 0
                ? $"Removed the legacy '{legacy}' task."
                : $"Could not remove the legacy '{legacy}' task: {Describe(result)}");
        }
    }

    /// <summary>
    /// Element order follows what Windows itself emits when exporting a task (battery settings
    /// first, MultipleInstancesPolicy third) rather than the order the schema documentation lists.
    /// Task Scheduler accepts either -- its own built-in tasks use this order, so it cannot be
    /// enforcing the documented sequence -- but matching the exporter removes any doubt.
    /// </summary>
    private static string BuildTaskXml(string executablePath)
    {
        var userId = Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        var workingDirectory = Escape(Path.GetDirectoryName(executablePath) ?? string.Empty);

        // ExecutionTimeLimit PT0S means "no limit"; the default would kill the widget after three
        // days. The battery settings matter on a laptop, where the defaults refuse to start on
        // battery and stop the task when unplugged.
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts Hardware Widget at logon, elevated, without a UAC prompt.</Description>
                <URI>\{TaskName}</URI>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{userId}</UserId>
                  <Delay>{LogonDelay}</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{userId}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <AllowHardTerminate>true</AllowHardTerminate>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
                <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Escape(executablePath)}</Command>
                  <WorkingDirectory>{workingDirectory}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    private static string Describe(SchTasksResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        return string.IsNullOrWhiteSpace(message)
            ? $"schtasks exited with code {result.ExitCode}."
            : message.Trim();
    }

    private static SchTasksResult RunSchTasks(string arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new SchTasksResult(-1, string.Empty, "schtasks could not be started.");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            // Bounded: schtasks is quick, and a hang must not freeze the settings dialog.
            if (!process.WaitForExit(15_000))
            {
                return new SchTasksResult(-1, output, "schtasks did not exit in time.");
            }

            return new SchTasksResult(process.ExitCode, output, error);
        }
        catch (Exception exception)
        {
            return new SchTasksResult(-1, string.Empty, exception.Message);
        }
    }

    private readonly record struct SchTasksResult(int ExitCode, string Output, string Error);
}
