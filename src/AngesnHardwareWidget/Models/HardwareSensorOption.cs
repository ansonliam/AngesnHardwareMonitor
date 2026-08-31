namespace AngesnHardwareWidget.Models;

/// <summary>A stable LibreHardwareMonitor sensor identifier and a user-facing device label.</summary>
public sealed record HardwareSensorOption(string Id, string Label)
{
    public static HardwareSensorOption Automatic { get; } = new(string.Empty, "Automatic (recommended)");

    public override string ToString() => Label;
}

public sealed record HardwareSensorCatalog(
    IReadOnlyList<HardwareSensorOption> DriveTemperatureSensors,
    IReadOnlyList<HardwareSensorOption> CpuFanSensors);
