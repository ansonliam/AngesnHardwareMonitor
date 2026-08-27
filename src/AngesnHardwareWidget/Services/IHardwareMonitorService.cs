using AngesnHardwareWidget.Models;

namespace AngesnHardwareWidget.Services;

/// <summary>
/// Abstraction over the sensor backend. Only the LibreHardwareMonitor implementation exists today;
/// the interface is here so an HWiNFO-backed provider could be added later without touching the
/// scheduler, the ViewModel or the history repository.
/// </summary>
public interface IHardwareMonitorService : IDisposable
{
    /// <summary>Identifier of the CPU the CPU metrics come from, or null if no CPU was found.</summary>
    string? CpuDeviceId { get; }

    /// <summary>Identifier of the selected GPU, or null if no GPU was found.</summary>
    string? GpuDeviceId { get; }

    /// <summary>Samples every metric.</summary>
    HardwareSnapshot Read();

    /// <summary>
    /// Samples only the requested metrics. Hardware updates are coalesced: several GPU metrics due
    /// in the same cycle cost exactly one GPU update, because LibreHardwareMonitor refreshes at the
    /// hardware-object level rather than per sensor.
    /// </summary>
    HardwareSnapshot Read(HardwareMetrics metrics);

    /// <summary>Discards the cached sensor references and re-enumerates hardware. Called after a
    /// GPU driver reset, a resume from sleep, or repeated read failures.</summary>
    void Rediscover();
}
