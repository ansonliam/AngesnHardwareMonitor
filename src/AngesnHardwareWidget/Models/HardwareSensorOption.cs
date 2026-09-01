namespace AngesnHardwareWidget.Models;

/// <summary>A stable LibreHardwareMonitor sensor identifier and a user-facing device label.
///
/// <paramref name="FallbackLabel"/> is the label this sensor would carry with no Windows help --
/// the hardware's own name. It is kept alongside the best label so that a stored display name can
/// be recognised as one the app generated rather than one the user typed: an auto-generated name
/// may be replaced when the better label becomes available, a user's name never may.</summary>
public sealed record HardwareSensorOption(string Id, string Label, string FallbackLabel = "")
{
    public static HardwareSensorOption Automatic { get; } = new(string.Empty, "Automatic (recommended)");

    /// <summary>Whether <paramref name="name"/> is a label this app generated for the sensor.</summary>
    public bool IsGeneratedLabel(string? name) =>
        string.Equals(name, Label, StringComparison.Ordinal)
        || (FallbackLabel.Length > 0 && string.Equals(name, FallbackLabel, StringComparison.Ordinal));

    public override string ToString() => Label;
}

public sealed record HardwareSensorCatalog(
    IReadOnlyList<HardwareSensorOption> DriveTemperatureSensors,
    IReadOnlyList<HardwareSensorOption> CpuFanSensors);
