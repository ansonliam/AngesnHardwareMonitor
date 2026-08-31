namespace AngesnHardwareWidget.Models;

/// <summary>
/// A snapshot on its way to storage. Timestamp is captured once and both persisted timestamp
/// columns are derived from it, so the ISO-8601 text and the Unix-ms integer can never disagree.
/// </summary>
public sealed record HardwareHistoryRecord(
    DateTimeOffset TimestampUtc,
    double? CpuTemperature,
    double? CpuUsagePercent,
    double? MemoryUsedGb,
    double? MemoryTotalGb,
    double? MemoryUsagePercent,
    double? GpuTemperature,
    double? GpuComputeUsagePercent,
    double? GpuMemoryUsagePercent,
    double? GpuMemoryTemperature,
    double? GpuFanRpm,
    double? MotherboardTemperature,
    double? MemoryTemperature,
    double? CpuFanRpm,
    double? StorageTemperature,
    double? PowerWatts,
    double? GpuHotSpotTemperature,
    double? CpuPowerWatts,
    double? GpuPowerWatts)
{
    /// <summary>Which metrics were sampled this cycle. Metrics outside this set were not due and
    /// are not written at all, so future charts never mistake a stale repeat for a new sample.</summary>
    public HardwareMetrics SampledMetrics { get; init; } = HardwareMetrics.None;

    public string? CpuDeviceId { get; init; }

    public string? GpuDeviceId { get; init; }

    /// <summary>Raw VRAM capacity in MB, retained for future charts when the GPU reports it.</summary>
    public double? GpuMemoryUsedMb { get; init; }

    public double? GpuMemoryTotalMb { get; init; }

    public static HardwareHistoryRecord FromSnapshot(HardwareSnapshot snapshot, DateTimeOffset timestampUtc) =>
        new(
            timestampUtc,
            snapshot.CpuTemperature,
            snapshot.CpuUsagePercent,
            snapshot.MemoryUsedGb,
            snapshot.MemoryTotalGb,
            snapshot.MemoryUsagePercent,
            snapshot.GpuTemperature,
            snapshot.GpuComputeUsagePercent,
            snapshot.GpuMemoryUsagePercent,
            snapshot.GpuMemoryTemperature,
            snapshot.GpuFanRpm,
            snapshot.MotherboardTemperature,
            snapshot.MemoryTemperature,
            snapshot.CpuFanRpm,
            snapshot.StorageTemperature,
            snapshot.PowerWatts,
            snapshot.GpuHotSpotTemperature,
            snapshot.CpuPowerWatts,
            snapshot.GpuPowerWatts)
        {
            SampledMetrics = snapshot.SampledMetrics,
            CpuDeviceId = snapshot.CpuDeviceId,
            GpuDeviceId = snapshot.GpuDeviceId,
            GpuMemoryUsedMb = snapshot.GpuMemoryUsedMb,
            GpuMemoryTotalMb = snapshot.GpuMemoryTotalMb,
        };
}
