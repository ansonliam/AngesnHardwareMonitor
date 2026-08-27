namespace AngesnHardwareWidget.Models;

/// <summary>
/// The eight logical metrics the widget schedules independently. RAM is one logical metric even
/// though it yields three raw values (used GB, total GB, percent), because they all come from a
/// single Memory hardware update.
/// </summary>
[Flags]
public enum HardwareMetrics
{
    None = 0,
    CpuTemperature = 1 << 0,
    CpuUsage = 1 << 1,
    MemoryUsage = 1 << 2,
    GpuTemperature = 1 << 3,
    GpuComputeUsage = 1 << 4,
    GpuMemoryUsage = 1 << 5,
    GpuMemoryTemperature = 1 << 6,
    GpuFan = 1 << 7,

    Cpu = CpuTemperature | CpuUsage,
    Memory = MemoryUsage,
    Gpu = GpuTemperature | GpuComputeUsage | GpuMemoryUsage | GpuMemoryTemperature | GpuFan,
    All = Cpu | Memory | Gpu,
}

public static class HardwareMetricsExtensions
{
    public static bool Includes(this HardwareMetrics metrics, HardwareMetrics metric) =>
        (metrics & metric) != 0;

    /// <summary>The eight logical metrics in display order.</summary>
    public static IReadOnlyList<HardwareMetrics> Individual { get; } =
    [
        HardwareMetrics.CpuTemperature,
        HardwareMetrics.CpuUsage,
        HardwareMetrics.MemoryUsage,
        HardwareMetrics.GpuTemperature,
        HardwareMetrics.GpuComputeUsage,
        HardwareMetrics.GpuMemoryUsage,
        HardwareMetrics.GpuMemoryTemperature,
        HardwareMetrics.GpuFan,
    ];
}
