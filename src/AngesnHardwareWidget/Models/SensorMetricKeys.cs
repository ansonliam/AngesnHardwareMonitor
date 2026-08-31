namespace AngesnHardwareWidget.Models;

/// <summary>Stable display keys for per-sensor rows. The identifier after the prefix is the
/// LibreHardwareMonitor sensor identifier, so a selected drive or fan keeps its own order,
/// visibility and history while the widget is running.</summary>
public static class SensorMetricKeys
{
    private const string DrivePrefix = "sensor.drive:";
    private const string CpuFanPrefix = "sensor.cpu-fan:";

    public static string Drive(string sensorId) => DrivePrefix + sensorId;

    public static string CpuFan(string sensorId) => CpuFanPrefix + sensorId;

    public static bool IsDrive(string key) => key.StartsWith(DrivePrefix, StringComparison.Ordinal);

    public static bool IsCpuFan(string key) => key.StartsWith(CpuFanPrefix, StringComparison.Ordinal);

    public static bool IsKnown(string key) => IsDrive(key) || IsCpuFan(key);
}
