using System.Globalization;
using System.Text;
using AngesnHardwareWidget.Models;
using LibreHardwareMonitor.Hardware;

namespace AngesnHardwareWidget.Services;

/// <summary>
/// Reads the widget's logical metrics out of LibreHardwareMonitor.
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
    private readonly SettingsService _settings;

    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsMemoryEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true,
    };

    private bool _opened;
    private bool _disposed;
    private int _consecutiveFailures;
    private bool _warnedAboutGpuMemoryLoadSensor;
    private bool _warnedAboutZeroTemperature;
    private SensorCache _cache = SensorCache.Empty;

    public LibreHardwareMonitorService(SettingsService settings)
    {
        _settings = settings;
    }

    public string? CpuDeviceId => _cache.CpuDeviceId;

    public string? GpuDeviceId => _cache.GpuDeviceId;

    public HardwareSensorCatalog GetSensorCatalog()
    {
        lock (_syncRoot)
        {
            var driveVolumes = WindowsStorageVolumeMapper.GetVolumeLabels();
            var volumeCursor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            return new HardwareSensorCatalog(
                _cache.AllStorageTemperatures
                    .Select(sensor => ToOption(sensor, driveVolumes, volumeCursor))
                    .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
                _cache.CpuFans
                    .Select(sensor => ToOption(sensor))
                    .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
                    .ToList());
        }
    }

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
        // Coalesce updates by owning hardware object, including motherboard sub-hardware and
        // multiple SSDs. A metric can therefore use several sensors without multiplying updates.
        var hardwareToUpdate = new HashSet<IHardware>();
        void Add(IHardware? hardware)
        {
            if (hardware is not null)
            {
                hardwareToUpdate.Add(hardware);
            }
        }

        void AddSensor(ISensor? sensor) => Add(sensor?.Hardware);
        void AddMany(IEnumerable<ISensor> sensors)
        {
            foreach (var sensor in sensors)
            {
                AddSensor(sensor);
            }
        }

        if (metrics.Includes(HardwareMetrics.Cpu) || IncludesAnyPower(metrics)) Add(_cache.Cpu);
        if (metrics.Includes(HardwareMetrics.MemoryUsage)) Add(_cache.Memory);
        if (metrics.Includes(HardwareMetrics.Gpu) || IncludesAnyPower(metrics)) Add(_cache.Gpu);
        if (metrics.Includes(HardwareMetrics.MotherboardTemperature)) AddSensor(_cache.MotherboardTemperature);
        if (metrics.Includes(HardwareMetrics.MemoryTemperature)) AddMany(_cache.MemoryTemperatures);
        // Refresh every fan/drive's owning hardware, not just the one selected for the aggregate
        // metric below -- the per-sensor tiles read sensorValues from the full CpuFans/
        // AllStorageTemperatures sets further down, so a fan or drive that isn't the selected one
        // would otherwise never have Update() called on it and its tile would freeze.
        if (metrics.Includes(HardwareMetrics.CpuFan)) AddMany(_cache.CpuFans);
        if (metrics.Includes(HardwareMetrics.StorageTemperature)) AddMany(_cache.AllStorageTemperatures);

        foreach (var hardware in hardwareToUpdate)
        {
            SafeUpdate(hardware);
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

        var powerDue = IncludesAnyPower(metrics);
        var cpuPower = powerDue ? Value(_cache.CpuPower) : null;
        var gpuPower = powerDue ? Value(_cache.GpuPower) : null;
        var sensorValues = new Dictionary<string, double?>();
        if (metrics.Includes(HardwareMetrics.CpuFan))
        {
            foreach (var sensor in _cache.CpuFans)
            {
                sensorValues[sensor.Identifier.ToString()] = Value(sensor);
            }
        }

        if (metrics.Includes(HardwareMetrics.StorageTemperature))
        {
            foreach (var sensor in _cache.AllStorageTemperatures)
            {
                sensorValues[sensor.Identifier.ToString()] = Temperature(sensor);
            }
        }

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
            GpuFanRpm: metrics.Includes(HardwareMetrics.GpuFan) ? Value(_cache.GpuFan) : null,
            MotherboardTemperature: metrics.Includes(HardwareMetrics.MotherboardTemperature) ? Temperature(_cache.MotherboardTemperature) : null,
            MemoryTemperature: metrics.Includes(HardwareMetrics.MemoryTemperature) ? MaximumTemperature(_cache.MemoryTemperatures) : null,
            CpuFanRpm: metrics.Includes(HardwareMetrics.CpuFan) ? Value(_cache.CpuFan) : null,
            StorageTemperature: metrics.Includes(HardwareMetrics.StorageTemperature) ? MaximumTemperature(_cache.StorageTemperatures) : null,
            PowerWatts: metrics.Includes(HardwareMetrics.Power) ? SumAvailable(cpuPower, gpuPower) : null,
            GpuHotSpotTemperature: metrics.Includes(HardwareMetrics.GpuHotSpotTemperature) ? Temperature(_cache.GpuHotSpotTemperature) : null,
            CpuPowerWatts: metrics.Includes(HardwareMetrics.CpuPower) ? cpuPower : null,
            GpuPowerWatts: metrics.Includes(HardwareMetrics.GpuPower) ? gpuPower : null)
        {
            SampledMetrics = metrics,
            CpuDeviceId = _cache.CpuDeviceId,
            GpuDeviceId = _cache.GpuDeviceId,
            GpuMemoryUsedMb = gpuMemoryUsedMb,
            GpuMemoryTotalMb = gpuMemoryTotalMb,
            SensorValues = sensorValues,
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
            (metrics.Includes(HardwareMetrics.GpuFan) && _cache.GpuFan is not null) ||
            (metrics.Includes(HardwareMetrics.MotherboardTemperature) && _cache.MotherboardTemperature is not null) ||
            (metrics.Includes(HardwareMetrics.MemoryTemperature) && _cache.MemoryTemperatures.Count > 0) ||
            (metrics.Includes(HardwareMetrics.CpuFan) && _cache.CpuFan is not null) ||
            (metrics.Includes(HardwareMetrics.StorageTemperature) && _cache.StorageTemperatures.Count > 0) ||
            (IncludesAnyPower(metrics) && (_cache.CpuPower is not null || _cache.GpuPower is not null)) ||
            (metrics.Includes(HardwareMetrics.GpuHotSpotTemperature) && _cache.GpuHotSpotTemperature is not null);

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
            || snapshot.GpuFanRpm is not null
            || snapshot.MotherboardTemperature is not null
            || snapshot.MemoryTemperature is not null
            || snapshot.CpuFanRpm is not null
            || snapshot.StorageTemperature is not null
            || snapshot.PowerWatts is not null
            || snapshot.CpuPowerWatts is not null
            || snapshot.GpuPowerWatts is not null
            || snapshot.GpuHotSpotTemperature is not null;

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

    private static double? SumAvailable(double? first, double? second) => (first, second) switch
    {
        (null, null) => null,
        ({ } value, null) => value,
        (null, { } value) => value,
        ({ } left, { } right) => left + right,
    };

    private double? MaximumTemperature(IEnumerable<ISensor> sensors)
    {
        var values = sensors.Select(Temperature).Where(value => value is not null).Select(value => value!.Value);
        return values.Any() ? values.Max() : null;
    }

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
        AppLog.Info("LibreHardwareMonitor backend opened (CPU, memory, GPU, motherboard and storage).");
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

        // Sensor collections and motherboard sub-hardware are only fully populated after an
        // update on some backends. Update the complete tree once, then enumerate it again.
        foreach (var hardware in _computer.Hardware)
        {
            UpdateTree(hardware);
        }

        var allHardware = EnumerateHardware(_computer.Hardware).ToList();
        var boardHardware = allHardware.Where(hardware => hardware.HardwareType is
            HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController).ToList();
        var allStorageHardware = allHardware.Where(hardware => hardware.HardwareType == HardwareType.Storage).ToList();
        var storageHardware = allStorageHardware.Where(LooksLikeSolidStateStorage).ToList();
        if (storageHardware.Count == 0)
        {
            // Some vendors expose no useful name or identifier marker. A storage temperature is
            // still more useful than "--" on those machines, but known HDDs never compete when a
            // positively identified SSD/NVMe device exists.
            storageHardware = allStorageHardware;
        }

        var memoryTemperatures = allHardware
            .Where(hardware => hardware.HardwareType is not
                (HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia or HardwareType.Storage))
            .SelectMany(hardware => hardware.Sensors)
            .Where(sensor => sensor.SensorType == SensorType.Temperature
                && ContainsAny(sensor.Name, "dimm", "dram", "memory module", "ram temperature"))
            .ToList();

        var storageTemperatures = storageHardware
            .SelectMany(hardware => hardware.Sensors)
            .Where(sensor => sensor.SensorType == SensorType.Temperature)
            .ToList();

        // One selectable source per physical drive. NVMe devices often expose composite, sensor 1
        // and sensor 2 temperatures; presenting all three would make one drive look like three.
        var allStorageTemperatures = allStorageHardware
            .Select(SelectStorageTemperature)
            .Where(sensor => sensor is not null)
            .Cast<ISensor>()
            .ToList();

        var cpuFans = boardHardware
            .SelectMany(hardware => hardware.Sensors)
            .Where(sensor => sensor.SensorType == SensorType.Fan
                && sensor.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var configured = _settings.Current;
        var selectedStorage = FindConfigured(allStorageTemperatures, configured.StorageTemperatureSensorId);
        var selectedCpuFan = FindConfigured(cpuFans, configured.CpuFanSensorId);

        return new SensorCache
        {
            Cpu = cpu,
            Memory = memory,
            Gpu = gpu,

            CpuTemperature = SelectCpuTemperature(cpu),
            CpuLoad = SelectCpuLoad(cpu),
            CpuPower = Select(cpu, SensorType.Power, ["CPU Package", "Package", "CPU PPT", "Total Power"],
                fallback: sensor => !ContainsAny(sensor.Name, "core #", "soc", "uncore")),

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
            GpuHotSpotTemperature = Select(gpu, SensorType.Temperature,
                ["GPU Hot Spot", "GPU Hotspot", "Hot Spot", "Hotspot"], fallback: null),
            GpuPower = Select(gpu, SensorType.Power,
                ["GPU Package", "GPU Power", "GPU Board Power", "GPU Chip Power", "Board Power"],
                fallback: sensor => !ContainsAny(sensor.Name, "memory", "core", "soc")),

            MotherboardTemperature = SelectAcross(
                boardHardware,
                SensorType.Temperature,
                ["Motherboard", "Mainboard", "System", "System #1", "Board"],
                fallback: sensor => ContainsAny(sensor.Name, "motherboard", "mainboard", "system")),
            MemoryTemperatures = memoryTemperatures,
            CpuFan = selectedCpuFan ?? SelectAcross(
                boardHardware,
                SensorType.Fan,
                ["CPU Fan", "CPU Fan #1", "Fan CPU", "CPU"],
                fallback: sensor => sensor.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase)),
            StorageTemperatures = selectedStorage is null ? storageTemperatures : [selectedStorage],
            AllStorageTemperatures = allStorageTemperatures,
            CpuFans = cpuFans,
        };
    }

    private static void UpdateTree(IHardware hardware)
    {
        SafeUpdate(hardware);
        foreach (var child in hardware.SubHardware)
        {
            UpdateTree(child);
        }
    }

    private static IEnumerable<IHardware> EnumerateHardware(IEnumerable<IHardware> roots)
    {
        foreach (var hardware in roots)
        {
            yield return hardware;
            foreach (var child in EnumerateHardware(hardware.SubHardware))
            {
                yield return child;
            }
        }
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

    private static bool LooksLikeSolidStateStorage(IHardware hardware) =>
        ContainsAny(hardware.Name, "ssd", "nvme", "solid state")
        || ContainsAny(hardware.Identifier.ToString(), "/ssd/", "/nvme/");

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

    private static ISensor? SelectStorageTemperature(IHardware hardware) => Select(
        hardware,
        SensorType.Temperature,
        ["Temperature", "Drive Temperature", "Composite Temperature", "Composite"],
        fallback: _ => true);

    private static bool IncludesAnyPower(HardwareMetrics metrics) =>
        metrics.Includes(HardwareMetrics.Power)
        || metrics.Includes(HardwareMetrics.CpuPower)
        || metrics.Includes(HardwareMetrics.GpuPower);

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

    private static ISensor? SelectAcross(
        IEnumerable<IHardware> hardware,
        SensorType sensorType,
        string[] candidateNames,
        Func<ISensor, bool>? fallback)
    {
        var sensors = hardware
            .SelectMany(item => item.Sensors)
            .Where(sensor => sensor.SensorType == sensorType)
            .ToList();

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

    private static ISensor? FindConfigured(IEnumerable<ISensor> sensors, string? identifier) =>
        string.IsNullOrWhiteSpace(identifier)
            ? null
            : sensors.FirstOrDefault(sensor => string.Equals(
                sensor.Identifier.ToString(), identifier, StringComparison.Ordinal));

    // Two physical drives can report the exact same WMI model string, so driveVolumes holds one
    // label per drive sharing that model rather than a single value. volumeCursor (one shared
    // instance per GetSensorCatalog call) tracks how many same-model drives this call has already
    // labelled, so each drive picks the next unused label instead of every same-model drive
    // collapsing onto the same one.
    private static HardwareSensorOption ToOption(
        ISensor sensor,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? driveVolumes = null,
        Dictionary<string, int>? volumeCursor = null)
    {
        var source = sensor.Hardware.Name;
        if (driveVolumes is not null
            && driveVolumes.TryGetValue(sensor.Hardware.Name, out var volumeLabels))
        {
            var index = volumeCursor is null ? 0 : volumeCursor.GetValueOrDefault(sensor.Hardware.Name);
            if (index < volumeLabels.Count)
            {
                source = volumeLabels[index];
            }

            if (volumeCursor is not null)
            {
                volumeCursor[sensor.Hardware.Name] = index + 1;
            }
        }

        return new HardwareSensorOption(sensor.Identifier.ToString(), $"{source} — {sensor.Name}");
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- logging

    private void DumpDiscoveredHardwareLocked()
    {
        var builder = new StringBuilder();
        foreach (var root in _computer.Hardware)
        {
            UpdateTree(root);
        }

        foreach (var hardware in EnumerateHardware(_computer.Hardware))
        {
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
        builder.AppendLine($"Selected CPU power: {Describe(_cache.CpuPower)}");
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
        builder.AppendLine($"Selected GPU hot spot: {Describe(_cache.GpuHotSpotTemperature)}");
        builder.AppendLine($"Selected GPU power: {Describe(_cache.GpuPower)}");
        builder.AppendLine($"Selected motherboard temperature: {Describe(_cache.MotherboardTemperature)}");
        builder.AppendLine($"Selected RAM temperatures: {DescribeMany(_cache.MemoryTemperatures)}");
        builder.AppendLine($"Selected CPU fan: {Describe(_cache.CpuFan)}");
        builder.AppendLine($"Selected drive temperatures: {DescribeMany(_cache.StorageTemperatures)}");
        AppLog.Block("Sensor selection", builder.ToString());
    }

    private static string Describe(IHardware? hardware) => hardware?.Name ?? "(none)";

    private static string Describe(ISensor? sensor) => sensor?.Name ?? "(none)";

    private static string DescribeMany(IEnumerable<ISensor> sensors)
    {
        var descriptions = sensors.Select(sensor => $"{sensor.Hardware.Name}: {sensor.Name}").ToList();
        return descriptions.Count == 0 ? "(none)" : string.Join(", ", descriptions);
    }

    private sealed class SensorCache
    {
        public static SensorCache Empty { get; } = new();

        public IHardware? Cpu { get; init; }
        public IHardware? Memory { get; init; }
        public IHardware? Gpu { get; init; }

        public ISensor? CpuTemperature { get; init; }
        public ISensor? CpuLoad { get; init; }
        public ISensor? CpuPower { get; init; }

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
        public ISensor? GpuHotSpotTemperature { get; init; }
        public ISensor? GpuPower { get; init; }

        public ISensor? MotherboardTemperature { get; init; }
        public IReadOnlyList<ISensor> MemoryTemperatures { get; init; } = [];
        public ISensor? CpuFan { get; init; }
        public IReadOnlyList<ISensor> StorageTemperatures { get; init; } = [];
        public IReadOnlyList<ISensor> AllStorageTemperatures { get; init; } = [];
        public IReadOnlyList<ISensor> CpuFans { get; init; } = [];

        public string? CpuDeviceId => Cpu?.Identifier.ToString();

        public string? GpuDeviceId => Gpu?.Identifier.ToString();

        public bool IsEmpty => Cpu is null && Memory is null && Gpu is null
            && MotherboardTemperature is null && CpuFan is null && StorageTemperatures.Count == 0;
    }
}
