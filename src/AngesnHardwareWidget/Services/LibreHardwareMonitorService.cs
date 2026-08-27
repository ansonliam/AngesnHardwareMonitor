using System.Globalization;
using System.Text;
using AngesnHardwareWidget.Models;
using LibreHardwareMonitor.Hardware;

namespace AngesnHardwareWidget.Services;

/// <summary>
/// Reads the eight logical metrics out of LibreHardwareMonitor.
///
/// Two rules drive the whole design. First, sensor selection is never "find the sensor named
/// exactly X" -- it is hardware type, then sensor type, then a prioritised candidate-name list,
/// then a conservative fallback, so the same code works across NVIDIA/AMD/Intel and across
/// hardware generations. Second, discovery happens once and the chosen ISensor references are
/// cached; a refresh only calls Update() on the hardware objects that are actually needed.
/// </summary>
public sealed class LibreHardwareMonitorService : IHardwareMonitorService
{
    private const int MaxConsecutiveFailuresBeforeRediscovery = 3;

    private readonly object _syncRoot = new();

    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsMemoryEnabled = true,
        IsGpuEnabled = true,
    };

    private bool _opened;
    private bool _disposed;
    private int _consecutiveFailures;
    private bool _warnedAboutGpuMemoryLoadSensor;
    private bool _warnedAboutZeroTemperature;
    private SensorCache _cache = SensorCache.Empty;

    public string? CpuDeviceId => _cache.CpuDeviceId;

    public string? GpuDeviceId => _cache.GpuDeviceId;

    /// <summary>
    /// Opens the backend and performs first discovery. Called once at startup; the Computer
    /// instance then stays open for the entire application lifetime.
    /// </summary>
    public void Initialize()
    {
        lock (_syncRoot)
        {
            EnsureOpenedLocked();
            DumpDiscoveredHardwareLocked();
            _cache = BuildCacheLocked();
            LogSelectionLocked();
        }
    }

    public HardwareSnapshot Read() => Read(HardwareMetrics.All);

    public HardwareSnapshot Read(HardwareMetrics metrics)
    {
        if (metrics == HardwareMetrics.None)
        {
            return HardwareSnapshot.Empty;
        }

        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            try
            {
                EnsureOpenedLocked();
                if (_cache.IsEmpty)
                {
                    _cache = BuildCacheLocked();
                    LogSelectionLocked();
                }

                var snapshot = ReadLocked(metrics);
                TrackReadHealthLocked(snapshot, metrics);
                return snapshot;
            }
            catch (Exception exception)
            {
                // A failing sensor, a GPU mid-driver-reset or a transient backend fault must never
                // reach the scheduler as an exception; the cycle degrades to "--" and we retry.
                AppLog.Error("Hardware read failed", exception);
                _consecutiveFailures++;
                RediscoverIfUnhealthyLocked();
                return HardwareSnapshot.Empty with { SampledMetrics = metrics };
            }
        }
    }

    public void Rediscover()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            AppLog.Info("Rediscovering hardware and sensors.");
            _cache = SensorCache.Empty;
            _consecutiveFailures = 0;

            try
            {
                _cache = BuildCacheLocked();

                // If the GPU vanished entirely, the backend's hardware collection itself is stale
                // (typical after a driver reset or a resume from sleep). A Close/Open cycle on the
                // same Computer instance rebuilds it without recreating the instance.
                if (_cache.Gpu is null)
                {
                    AppLog.Warn("No GPU after re-enumeration; cycling the backend.");
                    CloseLocked();
                    EnsureOpenedLocked();
                    _cache = BuildCacheLocked();
                }

                LogSelectionLocked();
            }
            catch (Exception exception)
            {
                AppLog.Error("Rediscovery failed", exception);
            }
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CloseLocked();
        }
    }

    // ---------------------------------------------------------------- reading

    private HardwareSnapshot ReadLocked(HardwareMetrics metrics)
    {
        // Coalesce hardware updates: each hardware object is refreshed at most once per cycle no
        // matter how many of its sensors are due.
        if (_cache.Cpu is not null && metrics.Includes(HardwareMetrics.Cpu))
        {
            SafeUpdate(_cache.Cpu);
        }

        if (_cache.Memory is not null && metrics.Includes(HardwareMetrics.Memory))
        {
            SafeUpdate(_cache.Memory);
        }

        if (_cache.Gpu is not null && metrics.Includes(HardwareMetrics.Gpu))
        {
            SafeUpdate(_cache.Gpu);
        }

        var memoryDue = metrics.Includes(HardwareMetrics.MemoryUsage);
        var memoryUsedGb = memoryDue ? Value(_cache.MemoryUsed) : null;
        var memoryAvailableGb = memoryDue ? Value(_cache.MemoryAvailable) : null;
        var memoryTotalGb = memoryUsedGb is not null && memoryAvailableGb is not null
            ? memoryUsedGb + memoryAvailableGb
            : null;

        // Prefer the backend's own physical-memory load sensor; derive only as a fallback.
        var memoryUsagePercent = memoryDue
            ? Value(_cache.MemoryLoad) ?? Percent(memoryUsedGb, memoryTotalGb)
            : null;

        var gpuMemoryDue = metrics.Includes(HardwareMetrics.GpuMemoryUsage);
        var gpuMemoryUsedMb = gpuMemoryDue ? Value(_cache.GpuMemoryUsed) : null;
        var gpuMemoryTotalMb = gpuMemoryDue ? Value(_cache.GpuMemoryTotal) : null;
        var gpuMemoryUsagePercent = gpuMemoryDue
            ? ResolveGpuMemoryUsagePercent(gpuMemoryUsedMb, gpuMemoryTotalMb)
            : null;

        return new HardwareSnapshot(
            CpuTemperature: metrics.Includes(HardwareMetrics.CpuTemperature) ? Temperature(_cache.CpuTemperature) : null,
            CpuUsagePercent: metrics.Includes(HardwareMetrics.CpuUsage) ? Value(_cache.CpuLoad) : null,
            MemoryUsedGb: memoryUsedGb,
            MemoryTotalGb: memoryTotalGb,
            MemoryUsagePercent: memoryUsagePercent,
            GpuTemperature: metrics.Includes(HardwareMetrics.GpuTemperature) ? Temperature(_cache.GpuTemperature) : null,
            GpuComputeUsagePercent: metrics.Includes(HardwareMetrics.GpuComputeUsage) ? Value(_cache.GpuComputeLoad) : null,
            GpuMemoryUsagePercent: gpuMemoryUsagePercent,
            GpuMemoryTemperature: metrics.Includes(HardwareMetrics.GpuMemoryTemperature) ? Temperature(_cache.GpuMemoryTemperature) : null,
            GpuFanRpm: metrics.Includes(HardwareMetrics.GpuFan) ? Value(_cache.GpuFan) : null)
        {
            SampledMetrics = metrics,
            CpuDeviceId = _cache.CpuDeviceId,
            GpuDeviceId = _cache.GpuDeviceId,
            GpuMemoryUsedMb = gpuMemoryUsedMb,
            GpuMemoryTotalMb = gpuMemoryTotalMb,
        };
    }

    /// <summary>
    /// VRAM capacity usage, meaning how full VRAM is -- not memory-controller activity.
    ///
    /// Used/total is preferred over the native Load percentage, which inverts the order one might
    /// expect. The reason is that the "GPU Memory" Load sensor does not mean capacity on every
    /// vendor: on an RX 7900 XT it drifts between 0% and 1% while SmallData concurrently reports
    /// 3645 of 20464 MB allocated (17.8%), i.e. it is reporting activity, and activity is exactly
    /// what this metric must not be. Used/total is an unambiguous capacity measurement wherever it
    /// exists, so it wins; the native sensor is the fallback for GPUs that expose no capacity data.
    /// </summary>
    private double? ResolveGpuMemoryUsagePercent(double? usedMb, double? totalMb)
    {
        var derived = Percent(usedMb, totalMb);
        var native = Value(_cache.GpuMemoryLoad);

        if (derived is null)
        {
            return native;
        }

        if (native is not null && Math.Abs(native.Value - derived.Value) > 5d && !_warnedAboutGpuMemoryLoadSensor)
        {
            _warnedAboutGpuMemoryLoadSensor = true;
            AppLog.Warn(
                $"GPU memory load sensor '{_cache.GpuMemoryLoad?.Name}' reports {native:0.#}% but " +
                $"{usedMb:0}/{totalMb:0} MB are allocated ({derived:0.#}%); using the capacity figure.");
        }

        return derived;
    }

    /// <summary>
    /// Counts cycles where every sensor we believed we had came back empty. One null reading is
    /// normal; a run of totally blank cycles means the cached references went stale.
    /// </summary>
    private void TrackReadHealthLocked(HardwareSnapshot snapshot, HardwareMetrics metrics)
    {
        var expectedAnything =
            (metrics.Includes(HardwareMetrics.CpuTemperature) && _cache.CpuTemperature is not null) ||
            (metrics.Includes(HardwareMetrics.CpuUsage) && _cache.CpuLoad is not null) ||
            (metrics.Includes(HardwareMetrics.MemoryUsage) && (_cache.MemoryLoad is not null || _cache.MemoryUsed is not null)) ||
            (metrics.Includes(HardwareMetrics.GpuTemperature) && _cache.GpuTemperature is not null) ||
            (metrics.Includes(HardwareMetrics.GpuComputeUsage) && _cache.GpuComputeLoad is not null) ||
            (metrics.Includes(HardwareMetrics.GpuMemoryUsage) && (_cache.GpuMemoryLoad is not null || _cache.GpuMemoryUsed is not null)) ||
            (metrics.Includes(HardwareMetrics.GpuMemoryTemperature) && _cache.GpuMemoryTemperature is not null) ||
            (metrics.Includes(HardwareMetrics.GpuFan) && _cache.GpuFan is not null);

        if (!expectedAnything)
        {
            return;
        }

        var gotSomething = snapshot.CpuTemperature is not null
            || snapshot.CpuUsagePercent is not null
            || snapshot.MemoryUsagePercent is not null
            || snapshot.MemoryUsedGb is not null
            || snapshot.GpuTemperature is not null
            || snapshot.GpuComputeUsagePercent is not null
            || snapshot.GpuMemoryUsagePercent is not null
            || snapshot.GpuMemoryTemperature is not null
            || snapshot.GpuFanRpm is not null;

        if (gotSomething)
        {
            _consecutiveFailures = 0;
            return;
        }

        _consecutiveFailures++;
        AppLog.Warn($"Read produced no values while sensors were expected ({_consecutiveFailures} in a row).");
        RediscoverIfUnhealthyLocked();
    }

    private void RediscoverIfUnhealthyLocked()
    {
        if (_consecutiveFailures < MaxConsecutiveFailuresBeforeRediscovery)
        {
            return;
        }

        _consecutiveFailures = 0;
        _cache = SensorCache.Empty;
        AppLog.Warn("Repeated read failures; sensor cache invalidated and will be rebuilt.");
    }

    private static void SafeUpdate(IHardware hardware)
    {
        try
        {
            hardware.Update();
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Update failed for '{hardware.Name}': {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static double? Value(ISensor? sensor)
    {
        if (sensor?.Value is not { } value || float.IsNaN(value) || float.IsInfinity(value))
        {
            return null;
        }

        return value;
    }

    private static double? Percent(double? used, double? total) =>
        used is not null && total is > 0 ? used / total * 100d : null;

    /// <summary>
    /// Temperature sensors get one extra rule: a flat 0.00 is treated as unavailable rather than as
    /// a measurement. A powered-on CPU or GPU is never at 0 C, and LibreHardwareMonitor publishes
    /// exactly 0 for a sensor it enumerated but cannot actually read -- most commonly the Ryzen
    /// Tctl/Tdie sensor when its kernel driver failed to load. Showing "--" there is honest;
    /// showing "0" reads as a real measurement. Fan RPM deliberately does NOT get this rule,
    /// because 0 RPM is a genuine reading in zero-fan idle mode.
    /// </summary>
    private double? Temperature(ISensor? sensor)
    {
        var value = Value(sensor);
        if (value is not 0d)
        {
            return value;
        }

        if (!_warnedAboutZeroTemperature)
        {
            _warnedAboutZeroTemperature = true;
            AppLog.Warn(
                $"Temperature sensor '{sensor?.Name}' reports exactly 0 C, which means it could not " +
                "be read rather than that the part is at freezing point. Reporting it as " +
                "unavailable. For CPU temperature this is almost always the kernel driver: " +
                "LibreHardwareMonitor extracts WinRing0 next to the executable as " +
                "<AppName>.sys, and Windows Defender deletes it on sight as " +
                "VulnerableDriver:WinNT/Winring0, so the sensor is enumerated but never populated. " +
                "Elevation does not help -- check Defender's protection history, and note that " +
                "Windows' vulnerable-driver blocklist may refuse the driver even if it is excluded.");
        }

        return null;
    }

    // -------------------------------------------------------------- lifecycle

    private void EnsureOpenedLocked()
    {
        if (_opened)
        {
            return;
        }

        _computer.Open();
        _opened = true;
        AppLog.Info("LibreHardwareMonitor backend opened (CPU, memory and GPU only).");
    }

    private void CloseLocked()
    {
        if (!_opened)
        {
            return;
        }

        try
        {
            _computer.Close();
        }
        catch (Exception exception)
        {
            AppLog.Error("Closing the backend failed", exception);
        }
        finally
        {
            _opened = false;
            _cache = SensorCache.Empty;
        }
    }

    // -------------------------------------------------------------- discovery

    private SensorCache BuildCacheLocked()
    {
        var cpu = _computer.Hardware.FirstOrDefault(hardware => hardware.HardwareType == HardwareType.Cpu);
        var memory = _computer.Hardware.FirstOrDefault(hardware => hardware.HardwareType == HardwareType.Memory);
        var gpu = SelectGpu(_computer.Hardware);

        // Sensor collections are only fully populated after a first update on some backends.
        if (cpu is not null)
        {
            SafeUpdate(cpu);
        }

        if (memory is not null)
        {
            SafeUpdate(memory);
        }

        if (gpu is not null)
        {
            SafeUpdate(gpu);
        }

        return new SensorCache
        {
            Cpu = cpu,
            Memory = memory,
            Gpu = gpu,

            CpuTemperature = SelectCpuTemperature(cpu),
            CpuLoad = SelectCpuLoad(cpu),

            MemoryLoad = Select(memory, SensorType.Load, ["Memory", "Memory Load", "Physical Memory"]),
            MemoryUsed = Select(memory, SensorType.Data, ["Memory Used", "Physical Memory Used"]),
            MemoryAvailable = Select(memory, SensorType.Data, ["Memory Available", "Memory Free", "Physical Memory Available"]),

            GpuTemperature = SelectGpuTemperature(gpu),
            GpuComputeLoad = SelectGpuComputeLoad(gpu),
            GpuMemoryLoad = Select(gpu, SensorType.Load, ["GPU Memory", "GPU Memory Load", "D3D Dedicated Memory Load"]),
            GpuMemoryUsed = Select(gpu, SensorType.SmallData, ["GPU Memory Used", "D3D Dedicated Memory Used", "GPU Memory Dedicated Used"]),
            GpuMemoryTotal = Select(gpu, SensorType.SmallData, ["GPU Memory Total", "D3D Dedicated Memory Total", "GPU Memory Dedicated Total"]),
            GpuMemoryTemperature = SelectGpuMemoryTemperature(gpu),
            GpuFan = Select(gpu, SensorType.Fan, ["GPU Fan", "GPU Fan 1", "Fan 1", "Fan"], fallback: _ => true),
        };
    }

    /// <summary>
    /// Prefers a discrete GPU. NVIDIA first, then AMD, then Intel, with integrated parts scored
    /// below discrete ones. Selection is intentionally a pure function of the hardware list so a
    /// manual "which GPU?" setting can be layered on later without reworking anything else.
    /// </summary>
    private static IHardware? SelectGpu(IEnumerable<IHardware> hardware)
    {
        var candidates = hardware
            .Where(item => item.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .ToList();

        if (candidates.Count <= 1)
        {
            return candidates.FirstOrDefault();
        }

        var selected = candidates
            .Select((item, index) => (Item: item, Index: index, Score: ScoreGpu(item)))
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Index)
            .First();

        AppLog.Info($"{candidates.Count} GPUs present; chose '{selected.Item.Name}' (score {selected.Score}).");
        return selected.Item;
    }

    private static int ScoreGpu(IHardware gpu)
    {
        var integrated = LooksIntegrated(gpu.Name);
        return gpu.HardwareType switch
        {
            HardwareType.GpuNvidia => integrated ? 250 : 400,
            HardwareType.GpuAmd => integrated ? 150 : 300,
            HardwareType.GpuIntel => integrated ? 100 : 200,
            _ => 0,
        };
    }

    private static bool LooksIntegrated(string name)
    {
        string[] integratedMarkers =
        [
            "uhd graphics", "hd graphics", "iris", "vega", "radeon(tm) graphics",
            "radeon graphics", "integrated", "apu",
        ];

        // Arc, RX, Pro and FirePro parts are discrete even when a generic marker also matches.
        string[] discreteMarkers = ["arc ", "radeon rx", "radeon pro", "firepro", "quadro"];

        var lowered = name.ToLowerInvariant();
        return !discreteMarkers.Any(lowered.Contains) && integratedMarkers.Any(lowered.Contains);
    }

    private static ISensor? SelectCpuTemperature(IHardware? cpu) => Select(
        cpu,
        SensorType.Temperature,
        ["CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "CPU Core", "Core Max", "Core Average"],
        // Never average the individual cores: prefer any package-level sensor over a per-core one.
        fallback: sensor => !sensor.Name.Contains('#'));

    private static ISensor? SelectCpuLoad(IHardware? cpu) => Select(
        cpu,
        SensorType.Load,
        ["CPU Total", "CPU Load Total", "Total"],
        // Per-core sensors are named "CPU Core #N"; anything core-specific is not overall usage.
        fallback: sensor => !sensor.Name.Contains('#')
            && !sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));

    private static ISensor? SelectGpuTemperature(IHardware? gpu) => Select(
        gpu,
        SensorType.Temperature,
        ["GPU Core", "GPU Temperature", "GPU"],
        // Hot spot and VRAM junction are real sensors but they are not the core temperature.
        fallback: sensor => !ContainsAny(sensor.Name, "hot spot", "hotspot", "memory", "junction", "vrm", "vrsoc"));

    private static ISensor? SelectGpuComputeLoad(IHardware? gpu) => Select(
        gpu,
        SensorType.Load,
        ["GPU Core", "GPU Render/Compute", "GPU Core Load", "GPU Usage", "D3D 3D", "GPU Graphics"],
        // Video engines, copy engines, bus activity and the memory controller are not compute load.
        fallback: sensor => !ContainsAny(
            sensor.Name,
            "memory", "video", "decode", "encode", "copy", "media", "bus", "board",
            "power", "controller", "fan", "frame buffer"));

    private static ISensor? SelectGpuMemoryTemperature(IHardware? gpu) => Select(
        gpu,
        SensorType.Temperature,
        [
            "GPU Memory", "GPU Memory Junction", "GPU Memory Junction Temperature",
            "Memory Junction", "GPU VRAM", "VRAM",
        ],
        // No fallback on purpose: GPU core temperature must never stand in for VRAM temperature.
        fallback: null);

    /// <summary>
    /// The one sensor-matching primitive: filter by sensor type, take the first exact candidate-name
    /// match in priority order, and only then fall back to a supplied predicate.
    /// </summary>
    private static ISensor? Select(
        IHardware? hardware,
        SensorType sensorType,
        string[] candidateNames,
        Func<ISensor, bool>? fallback = null)
    {
        if (hardware is null)
        {
            return null;
        }

        var sensors = hardware.Sensors.Where(sensor => sensor.SensorType == sensorType).ToList();
        if (sensors.Count == 0)
        {
            return null;
        }

        foreach (var candidate in candidateNames)
        {
            var match = sensors.FirstOrDefault(sensor =>
                string.Equals(sensor.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return fallback is null ? null : sensors.FirstOrDefault(fallback);
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- logging

    private void DumpDiscoveredHardwareLocked()
    {
        var builder = new StringBuilder();
        foreach (var hardware in _computer.Hardware)
        {
            SafeUpdate(hardware);
            builder.AppendLine($"[Hardware] {hardware.HardwareType} :: {hardware.Name} ({hardware.Identifier})");

            foreach (var group in hardware.Sensors
                .GroupBy(sensor => sensor.SensorType)
                .OrderBy(group => group.Key.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"  [{group.Key}]");
                foreach (var sensor in group.OrderBy(sensor => sensor.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var value = sensor.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "null";
                    builder.AppendLine($"    {sensor.Name} = {value}");
                }
            }
        }

        AppLog.Block("Sensor dump", builder.Length == 0 ? "(no hardware reported)" : builder.ToString());
    }

    private void LogSelectionLocked()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Selected CPU: {Describe(_cache.Cpu)}");
        builder.AppendLine($"Selected CPU temperature: {Describe(_cache.CpuTemperature)}");
        builder.AppendLine($"Selected CPU usage: {Describe(_cache.CpuLoad)}");
        builder.AppendLine($"Selected RAM usage: {Describe(_cache.MemoryLoad)}");
        builder.AppendLine($"Selected RAM used: {Describe(_cache.MemoryUsed)}");
        builder.AppendLine($"Selected RAM available: {Describe(_cache.MemoryAvailable)}");
        builder.AppendLine($"Selected GPU: {Describe(_cache.Gpu)}");
        builder.AppendLine($"Selected GPU temperature: {Describe(_cache.GpuTemperature)}");
        builder.AppendLine($"Selected GPU compute usage: {Describe(_cache.GpuComputeLoad)}");
        builder.AppendLine($"Selected GPU memory usage: {Describe(_cache.GpuMemoryLoad)}");
        builder.AppendLine($"Selected GPU memory used: {Describe(_cache.GpuMemoryUsed)}");
        builder.AppendLine($"Selected GPU memory total: {Describe(_cache.GpuMemoryTotal)}");
        builder.AppendLine($"Selected GPU memory temperature: {Describe(_cache.GpuMemoryTemperature)}");
        builder.AppendLine($"Selected GPU fan: {Describe(_cache.GpuFan)}");
        AppLog.Block("Sensor selection", builder.ToString());
    }

    private static string Describe(IHardware? hardware) => hardware?.Name ?? "(none)";

    private static string Describe(ISensor? sensor) => sensor?.Name ?? "(none)";

    private sealed class SensorCache
    {
        public static SensorCache Empty { get; } = new();

        public IHardware? Cpu { get; init; }
        public IHardware? Memory { get; init; }
        public IHardware? Gpu { get; init; }

        public ISensor? CpuTemperature { get; init; }
        public ISensor? CpuLoad { get; init; }

        public ISensor? MemoryLoad { get; init; }
        public ISensor? MemoryUsed { get; init; }
        public ISensor? MemoryAvailable { get; init; }

        public ISensor? GpuTemperature { get; init; }
        public ISensor? GpuComputeLoad { get; init; }
        public ISensor? GpuMemoryLoad { get; init; }
        public ISensor? GpuMemoryUsed { get; init; }
        public ISensor? GpuMemoryTotal { get; init; }
        public ISensor? GpuMemoryTemperature { get; init; }
        public ISensor? GpuFan { get; init; }

        public string? CpuDeviceId => Cpu?.Identifier.ToString();

        public string? GpuDeviceId => Gpu?.Identifier.ToString();

        public bool IsEmpty => Cpu is null && Memory is null && Gpu is null;
    }
}
