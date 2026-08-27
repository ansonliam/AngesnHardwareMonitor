namespace HardwareWidget.Models;

/// <summary>
/// One reading cycle's worth of raw hardware values. Every metric is nullable because no two
/// machines expose the same sensor set, and in individual-polling mode a metric that was not due
/// this cycle is also left null. Values are raw numbers only -- formatting belongs to the UI.
/// </summary>
public sealed record HardwareSnapshot(
    double? CpuTemperature,
    double? CpuUsagePercent,
    double? MemoryUsedGb,
    double? MemoryTotalGb,
    double? MemoryUsagePercent,
    double? GpuTemperature,
    double? GpuComputeUsagePercent,
    double? GpuMemoryUsagePercent,
    double? GpuMemoryTemperature,
    double? GpuFanRpm)
{
    /// <summary>Metrics that were actually sampled this cycle. A metric can be in this set and
    /// still hold a null value, which means "we asked and the sensor was unavailable" -- that is a
    /// real observation and is persisted as a NULL reading.</summary>
    public HardwareMetrics SampledMetrics { get; init; } = HardwareMetrics.None;

    /// <summary>Stable identifier of the CPU the CPU metrics came from, for history DeviceId.</summary>
    public string? CpuDeviceId { get; init; }

    /// <summary>Stable identifier of the selected GPU, for history DeviceId.</summary>
    public string? GpuDeviceId { get; init; }

    /// <summary>Raw VRAM capacity in MB when the GPU reports it. Not shown by the compact UI;
    /// retained so future charts can use absolute VRAM as well as the percentage.</summary>
    public double? GpuMemoryUsedMb { get; init; }

    public double? GpuMemoryTotalMb { get; init; }

    public static HardwareSnapshot Empty { get; } =
        new(null, null, null, null, null, null, null, null, null, null);
}
