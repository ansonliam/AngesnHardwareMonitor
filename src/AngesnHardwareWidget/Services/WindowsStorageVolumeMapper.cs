using System.Management;

namespace AngesnHardwareWidget.Services;

/// <summary>Maps Windows physical-drive model names to their mounted volume letters. A storage
/// device can legitimately have no mounted partition, so failures deliberately return no mapping
/// and callers retain the hardware name instead.
///
/// Two drives can share the exact same model string (e.g. a dual-identical-SSD build), and WMI's
/// model name is the only thing this mapping can key on -- so labels for a repeated model are kept
/// as an ordered list rather than a single value, one entry per physical drive with that model, in
/// the order WMI enumerated them. Callers consume the list positionally (see
/// LibreHardwareMonitorService.ToOption) so that each drive gets its own label instead of every
/// same-model drive silently collapsing onto whichever one WMI enumerated last.</summary>
internal static class WindowsStorageVolumeMapper
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GetVolumeLabels()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // DeviceID is the key of Win32_DiskDrive and must be in the projection even though only
            // Model is read: a projected object without its key has no resolvable __RELPATH, so
            // GetRelated below throws "Operation is not valid due to the current state of the
            // object" for every disk and the whole mapping silently degrades to hardware names.
            using var disks = new ManagementObjectSearcher("SELECT DeviceID, Model FROM Win32_DiskDrive").Get();
            foreach (ManagementObject disk in disks)
            {
                var model = disk["Model"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(model))
                {
                    continue;
                }

                // Per disk rather than around the loop: one drive WMI cannot walk must cost that
                // drive its label, not every drive enumerated after it.
                List<string> volumes;
                try
                {
                    volumes = disk
                        .GetRelated("Win32_DiskPartition")
                        .Cast<ManagementObject>()
                        .SelectMany(partition => partition
                            .GetRelated("Win32_LogicalDisk")
                            .Cast<ManagementObject>())
                        .Select(volume => volume["DeviceID"]?.ToString()?.Trim())
                        .Where(volume => !string.IsNullOrWhiteSpace(volume))
                        .Select(volume => volume + "\\")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(volume => volume, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (Exception exception)
                {
                    AppLog.Warn(
                        $"Could not map '{model}' to a volume letter: " +
                        $"{exception.GetType().Name}: {exception.Message}");
                    continue;
                }

                if (volumes.Count > 0)
                {
                    if (!result.TryGetValue(model, out var labels))
                    {
                        labels = [];
                        result[model] = labels;
                    }

                    labels.Add(string.Join(" + ", volumes));
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Could not map storage devices to volume letters: {exception.GetType().Name}: {exception.Message}");
        }

        return result.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}
