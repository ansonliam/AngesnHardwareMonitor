namespace AngesnHardwareWidget.Models;

/// <summary>
/// Persisted MetricType keys. These are part of the on-disk history contract: once a key has
/// shipped it must not be renamed without a migration, because historical rows would stop
/// matching. Stored as stable strings rather than an enum ordinal so the database stays
/// inspectable and new metrics can be added without disturbing existing rows.
/// </summary>
public static class MetricTypes
{
    public const string CpuTemperature = "cpu.temperature";
    public const string CpuUsage = "cpu.usage";
    public const string MemoryUsedGb = "memory.used_gb";
    public const string MemoryTotalGb = "memory.total_gb";
    public const string MemoryUsagePercent = "memory.usage_percent";
    public const string GpuTemperature = "gpu.temperature";
    public const string GpuComputeUsage = "gpu.compute_usage";
    public const string GpuMemoryUsage = "gpu.memory_usage";
    public const string GpuMemoryTemperature = "gpu.memory_temperature";
    public const string GpuFanRpm = "gpu.fan_rpm";

    /// <summary>Optional raw VRAM capacity metrics, persisted when the GPU exposes them.</summary>
    public const string GpuMemoryUsedMb = "gpu.memory_used_mb";
    public const string GpuMemoryTotalMb = "gpu.memory_total_mb";

    /// <summary>Schema documents MetricType/DeviceId as VARCHAR(255). SQLite's type affinity does
    /// not enforce that, so the repository validates length in code before insertion.</summary>
    public const int MaxKeyLength = 255;

    /// <summary>
    /// The key each displayed metric is graded and coloured by. RAM maps to its percentage because
    /// that is the value the widget shows; used/total GB are history-only.
    /// </summary>
    public static string DisplayKeyOf(HardwareMetrics metric) => metric switch
    {
        HardwareMetrics.CpuTemperature => CpuTemperature,
        HardwareMetrics.CpuUsage => CpuUsage,
        HardwareMetrics.MemoryUsage => MemoryUsagePercent,
        HardwareMetrics.GpuTemperature => GpuTemperature,
        HardwareMetrics.GpuComputeUsage => GpuComputeUsage,
        HardwareMetrics.GpuMemoryUsage => GpuMemoryUsage,
        HardwareMetrics.GpuMemoryTemperature => GpuMemoryTemperature,
        HardwareMetrics.GpuFan => GpuFanRpm,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Not a displayed metric."),
    };
}
