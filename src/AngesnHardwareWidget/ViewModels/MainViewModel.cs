using System.Collections.ObjectModel;
using System.Windows.Threading;
using AngesnHardwareWidget.Models;
using AngesnHardwareWidget.Services;
using AngesnHardwareWidget.Settings;

namespace AngesnHardwareWidget.ViewModels;

/// <summary>
/// The widget's state. Holds the raw nullable numbers and exposes the display rows; all
/// formatting and colouring happens here in the presentation layer, never in the hardware service.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private const double CpuThrottleTemperature = 90d;
    private const double GpuCoreThrottleTemperature = 95d;
    private const double GpuHotSpotThrottleTemperature = 110d;

    private readonly Dispatcher _dispatcher;
    private readonly SettingsService _settings;
    private readonly Dictionary<HardwareMetrics, MetricTileViewModel> _tiles;
    private readonly Dictionary<string, MetricTileViewModel> _sensorTiles = [];
    private readonly Dictionary<string, string> _sensorIds = [];
    private readonly Dictionary<string, double?> _sensorValues = [];

    private AppSettings _current;
    private MetricStagePalette _palette;

    private double? _cpuTemperature;
    private double? _cpuUsagePercent;
    private double? _memoryUsedGb;
    private double? _memoryTotalGb;
    private double? _memoryUsagePercent;
    private double? _gpuTemperature;
    private double? _gpuComputeUsagePercent;
    private double? _gpuMemoryUsagePercent;
    private double? _gpuMemoryTemperature;
    private double? _gpuFanRpm;
    private double? _motherboardTemperature;
    private double? _memoryTemperature;
    private double? _cpuFanRpm;
    private double? _storageTemperature;
    private double? _powerWatts;
    private double? _cpuPowerWatts;
    private double? _gpuPowerWatts;
    private double? _gpuHotSpotTemperature;
    private bool _hasWheaHardwareError;
    private string _alertText = string.Empty;

    public MainViewModel(
        SettingsService settings,
        HardwareSensorCatalog sensorCatalog,
        Dispatcher dispatcher,
        Action openSettings,
        Action refresh,
        Action exit)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _current = settings.Current;
        _palette = new MetricStagePalette(_current);

        OpenSettingsCommand = new RelayCommand(openSettings);
        RefreshCommand = new RelayCommand(refresh);
        ExitCommand = new RelayCommand(exit);

        _tiles = new Dictionary<HardwareMetrics, MetricTileViewModel>
        {
            [HardwareMetrics.CpuTemperature] = new(HardwareMetrics.CpuTemperature, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.CpuTemperature), MetricFormat.Temperature),
            [HardwareMetrics.CpuUsage] = new(HardwareMetrics.CpuUsage, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.CpuUsage), MetricFormat.Percent),
            [HardwareMetrics.MemoryUsage] = new(HardwareMetrics.MemoryUsage, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.MemoryUsage), MetricFormat.Memory),
            [HardwareMetrics.GpuTemperature] = new(HardwareMetrics.GpuTemperature, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.GpuTemperature), MetricFormat.Temperature),
            [HardwareMetrics.GpuComputeUsage] = new(HardwareMetrics.GpuComputeUsage, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.GpuComputeUsage), MetricFormat.Percent),
            [HardwareMetrics.GpuMemoryUsage] = new(HardwareMetrics.GpuMemoryUsage, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.GpuMemoryUsage), MetricFormat.Percent),
            [HardwareMetrics.GpuMemoryTemperature] = new(HardwareMetrics.GpuMemoryTemperature, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.GpuMemoryTemperature), MetricFormat.Temperature),
            [HardwareMetrics.GpuFan] = new(HardwareMetrics.GpuFan, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.GpuFan), MetricFormat.Rpm),
            [HardwareMetrics.MotherboardTemperature] = new(HardwareMetrics.MotherboardTemperature, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.MotherboardTemperature), MetricFormat.Temperature),
            [HardwareMetrics.MemoryTemperature] = new(HardwareMetrics.MemoryTemperature, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.MemoryTemperature), MetricFormat.Temperature),
            [HardwareMetrics.CpuFan] = new(HardwareMetrics.CpuFan, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.CpuFan), MetricFormat.Rpm),
            [HardwareMetrics.StorageTemperature] = new(HardwareMetrics.StorageTemperature, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.StorageTemperature), MetricFormat.Temperature),
            [HardwareMetrics.Power] = new(HardwareMetrics.Power, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.Power), MetricFormat.Watts),
            [HardwareMetrics.CpuPower] = new(HardwareMetrics.CpuPower, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.CpuPower), MetricFormat.Watts),
            [HardwareMetrics.GpuPower] = new(HardwareMetrics.GpuPower, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.GpuPower), MetricFormat.Watts),
            [HardwareMetrics.GpuHotSpotTemperature] = new(HardwareMetrics.GpuHotSpotTemperature, MetricTypes.DefaultDisplayNameOf(HardwareMetrics.GpuHotSpotTemperature), MetricFormat.Temperature),
        };

        AddSensorTiles(sensorCatalog.DriveTemperatureSensors, SensorMetricKeys.Drive, HardwareMetrics.StorageTemperature, MetricFormat.Temperature);
        AddSensorTiles(sensorCatalog.CpuFanSensors, SensorMetricKeys.CpuFan, HardwareMetrics.CpuFan, MetricFormat.Rpm);

        Metrics = [];
        ApplyPerTilePresentation();
        RebuildMetrics();
        RefreshAllTiles();
        RefreshAlertText();

        _settings.SettingsChanged += (_, updated) => RunOnUi(() => ApplySettings(updated));
    }

    public ObservableCollection<MetricTileViewModel> Metrics { get; }

    public RelayCommand OpenSettingsCommand { get; }

    /// <summary>Forces an immediate sample of every metric.</summary>
    public RelayCommand RefreshCommand { get; }

    public RelayCommand ExitCommand { get; }

    /// <summary>"Retro" or "Default". The window turns this into fonts, corners and text rendering.</summary>
    public string WidgetAppearance => _current.WidgetAppearance;

    // ------------------------------------------------------------ raw values

    public double? CpuTemperature => _cpuTemperature;

    public double? CpuUsagePercent => _cpuUsagePercent;

    public double? MemoryUsedGb => _memoryUsedGb;

    public double? MemoryTotalGb => _memoryTotalGb;

    public double? MemoryUsagePercent => _memoryUsagePercent;

    public double? GpuTemperature => _gpuTemperature;

    public double? GpuComputeUsagePercent => _gpuComputeUsagePercent;

    public double? GpuMemoryUsagePercent => _gpuMemoryUsagePercent;

    public double? GpuMemoryTemperature => _gpuMemoryTemperature;

    public double? GpuFanRpm => _gpuFanRpm;

    public double? MotherboardTemperature => _motherboardTemperature;

    public double? MemoryTemperature => _memoryTemperature;

    public double? CpuFanRpm => _cpuFanRpm;

    public double? StorageTemperature => _storageTemperature;

    public double? PowerWatts => _powerWatts;

    public double? CpuPowerWatts => _cpuPowerWatts;

    public double? GpuPowerWatts => _gpuPowerWatts;

    public double? GpuHotSpotTemperature => _gpuHotSpotTemperature;

    /// <summary>One compact, reserved status line at the bottom of the widget.</summary>
    public string AlertText
    {
        get => _alertText;
        private set => SetProperty(ref _alertText, value);
    }

    // ----------------------------------------------------------------- update

    /// <summary>
    /// Applies a cycle's results. Only the metrics the cycle actually sampled are written, so in
    /// individual-polling mode a fast metric refreshing does not blank out the slower ones.
    /// </summary>
    public void Apply(HardwareSnapshot snapshot) => RunOnUi(() =>
    {
        var sampled = snapshot.SampledMetrics;
        var recordedAt = DateTimeOffset.Now;

        if (sampled.Includes(HardwareMetrics.CpuTemperature))
        {
            _cpuTemperature = snapshot.CpuTemperature;
            OnPropertyChanged(nameof(CpuTemperature));
            RefreshTile(HardwareMetrics.CpuTemperature);
            RecordSample(HardwareMetrics.CpuTemperature, _cpuTemperature, recordedAt);
        }

        if (sampled.Includes(HardwareMetrics.CpuUsage))
        {
            _cpuUsagePercent = snapshot.CpuUsagePercent;
            OnPropertyChanged(nameof(CpuUsagePercent));
            RefreshTile(HardwareMetrics.CpuUsage);
            RecordSample(HardwareMetrics.CpuUsage, _cpuUsagePercent, recordedAt);
        }

        if (sampled.Includes(HardwareMetrics.MemoryUsage))
        {
            _memoryUsedGb = snapshot.MemoryUsedGb;
            _memoryTotalGb = snapshot.MemoryTotalGb;
            _memoryUsagePercent = snapshot.MemoryUsagePercent;
            OnPropertyChanged(nameof(MemoryUsedGb));
            OnPropertyChanged(nameof(MemoryTotalGb));
            OnPropertyChanged(nameof(MemoryUsagePercent));
            RefreshTile(HardwareMetrics.MemoryUsage);
            RecordSample(HardwareMetrics.MemoryUsage, _memoryUsagePercent, recordedAt);
        }

        if (sampled.Includes(HardwareMetrics.GpuTemperature))
        {
            _gpuTemperature = snapshot.GpuTemperature;
            OnPropertyChanged(nameof(GpuTemperature));
            RefreshTile(HardwareMetrics.GpuTemperature);
            RecordSample(HardwareMetrics.GpuTemperature, _gpuTemperature, recordedAt);
        }

        if (sampled.Includes(HardwareMetrics.GpuComputeUsage))
        {
            _gpuComputeUsagePercent = snapshot.GpuComputeUsagePercent;
            OnPropertyChanged(nameof(GpuComputeUsagePercent));
            RefreshTile(HardwareMetrics.GpuComputeUsage);
            RecordSample(HardwareMetrics.GpuComputeUsage, _gpuComputeUsagePercent, recordedAt);
        }

        if (sampled.Includes(HardwareMetrics.GpuMemoryUsage))
        {
            _gpuMemoryUsagePercent = snapshot.GpuMemoryUsagePercent;
            OnPropertyChanged(nameof(GpuMemoryUsagePercent));
            RefreshTile(HardwareMetrics.GpuMemoryUsage);
            RecordSample(HardwareMetrics.GpuMemoryUsage, _gpuMemoryUsagePercent, recordedAt);
        }

        if (sampled.Includes(HardwareMetrics.GpuMemoryTemperature))
        {
            _gpuMemoryTemperature = snapshot.GpuMemoryTemperature;
            OnPropertyChanged(nameof(GpuMemoryTemperature));
            RefreshTile(HardwareMetrics.GpuMemoryTemperature);
            RecordSample(HardwareMetrics.GpuMemoryTemperature, _gpuMemoryTemperature, recordedAt);
        }

        if (sampled.Includes(HardwareMetrics.GpuFan))
        {
            _gpuFanRpm = snapshot.GpuFanRpm;
            OnPropertyChanged(nameof(GpuFanRpm));
            RefreshTile(HardwareMetrics.GpuFan);
            RecordSample(HardwareMetrics.GpuFan, _gpuFanRpm, recordedAt);
        }

        ApplyMetric(HardwareMetrics.MotherboardTemperature, snapshot.MotherboardTemperature, ref _motherboardTemperature, nameof(MotherboardTemperature), sampled, recordedAt);
        ApplyMetric(HardwareMetrics.MemoryTemperature, snapshot.MemoryTemperature, ref _memoryTemperature, nameof(MemoryTemperature), sampled, recordedAt);
        ApplyMetric(HardwareMetrics.CpuFan, snapshot.CpuFanRpm, ref _cpuFanRpm, nameof(CpuFanRpm), sampled, recordedAt);
        ApplyMetric(HardwareMetrics.StorageTemperature, snapshot.StorageTemperature, ref _storageTemperature, nameof(StorageTemperature), sampled, recordedAt);
        ApplyMetric(HardwareMetrics.Power, snapshot.PowerWatts, ref _powerWatts, nameof(PowerWatts), sampled, recordedAt);
        ApplyMetric(HardwareMetrics.CpuPower, snapshot.CpuPowerWatts, ref _cpuPowerWatts, nameof(CpuPowerWatts), sampled, recordedAt);
        ApplyMetric(HardwareMetrics.GpuPower, snapshot.GpuPowerWatts, ref _gpuPowerWatts, nameof(GpuPowerWatts), sampled, recordedAt);
        ApplyMetric(HardwareMetrics.GpuHotSpotTemperature, snapshot.GpuHotSpotTemperature, ref _gpuHotSpotTemperature, nameof(GpuHotSpotTemperature), sampled, recordedAt);
        ApplySensorMetrics(snapshot, sampled, recordedAt);

        RefreshAlertText();
    });

    /// <summary>WHEA errors are sticky for this app session once Windows reports one.</summary>
    public void ApplyWheaHardwareError(bool detected) => RunOnUi(() =>
    {
        if (!detected || _hasWheaHardwareError)
        {
            return;
        }

        _hasWheaHardwareError = true;
        RefreshAlertText();
    });

    private void ApplyMetric(
        HardwareMetrics metric,
        double? value,
        ref double? field,
        string propertyName,
        HardwareMetrics sampled,
        DateTimeOffset recordedAt)
    {
        if (!sampled.Includes(metric))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        RefreshTile(metric);
        RecordSample(metric, value, recordedAt);
    }

    private void ApplySettings(AppSettings updated)
    {
        _current = updated;
        _palette = new MetricStagePalette(updated);
        OnPropertyChanged(nameof(WidgetAppearance));
        ApplyPerTilePresentation();
        RebuildMetrics();
        RefreshAllTiles();
        RefreshAlertText();
    }

    /// <summary>
    /// Applies each metric's show-graph setting to its cached tile. Separate from
    /// <see cref="RebuildMetrics"/>, which short-circuits when order and visibility have not
    /// changed and would otherwise miss a graph-only toggle or a value-width change.
    /// </summary>
    private void ApplyPerTilePresentation()
    {
        var scale = _current.WidgetTextScale;

        // Every metric shares one value-column width except RAM: while RAM used/total is shown,
        // "23.2/63.9 GB (36%)" needs a wider column than any other metric's value ever does, and
        // only that row should widen for it.
        var valueColumnWidth = ColumnWidths.Parse(_current.WidgetValueColumnWidth, scale);
        var ramValueColumnWidth = _current.ShowRamUsedAndTotal
            ? ColumnWidths.Parse(_current.WidgetValueColumnWidthWithRam, scale)
            : valueColumnWidth;

        foreach (var (metric, tile) in _tiles)
        {
            tile.Label = _current.ResolveDisplayName(metric);
            tile.ShowGraph = _current.IsGraphVisible(metric);
            tile.ValueColumnWidth = metric == HardwareMetrics.MemoryUsage ? ramValueColumnWidth : valueColumnWidth;
        }

        foreach (var (key, tile) in _sensorTiles)
        {
            tile.Label = _current.ResolveDisplayName(key, tile.Label);
            tile.ShowGraph = _current.IsGraphVisible(key);
            tile.ValueColumnWidth = valueColumnWidth;
        }
    }

    /// <summary>
    /// Rebuilds the displayed rows from the persisted order and visibility. The tile objects
    /// themselves are reused from the cache, so hiding a metric and showing it again does not lose
    /// its last reading or make the row flash as unavailable.
    /// </summary>
    private void RebuildMetrics()
    {
        var standardKeys = _current.ResolveDisplayOrder()
            .Where(metric => metric is not HardwareMetrics.CpuFan and not HardwareMetrics.StorageTemperature)
            .Where(metric => _current.ConsolidatePower
                ? metric is not HardwareMetrics.CpuPower and not HardwareMetrics.GpuPower
                : metric != HardwareMetrics.Power)
            .Select(MetricTypes.DisplayKeyOf);
        var desired = _current.MetricDisplay
            .Where(entry => entry.Visible)
            .Select(entry => entry.MetricType)
            .Where(key => standardKeys.Contains(key) || _sensorTiles.ContainsKey(key))
            .Select(key => _sensorTiles.TryGetValue(key, out var sensorTile)
                ? sensorTile
                : _tiles[HardwareMetricsExtensions.Individual.Single(metric => MetricTypes.DisplayKeyOf(metric) == key)])
            .ToList();

        if (Metrics.Count == desired.Count
            && !Metrics.Where((tile, index) => !ReferenceEquals(tile, desired[index])).Any())
        {
            return;
        }

        Metrics.Clear();
        foreach (var tile in desired)
        {
            Metrics.Add(tile);
        }
    }

    private void RefreshAllTiles()
    {
        foreach (var metric in HardwareMetricsExtensions.Individual)
        {
            RefreshTile(metric);
        }

        foreach (var (key, tile) in _sensorTiles)
        {
            RefreshSensorTile(key, tile, _sensorValues.GetValueOrDefault(key));
        }
    }

    private void RefreshTile(HardwareMetrics metric)
    {
        var stages = _current.ResolveStages(metric);

        if (metric == HardwareMetrics.MemoryUsage)
        {
            // RAM is graded on its percentage even when the text also shows used/total GB.
            _tiles[metric].Update(
                _memoryUsagePercent,
                stages,
                _palette,
                _memoryUsedGb,
                _memoryTotalGb,
                _current.ShowRamUsedAndTotal);
            return;
        }

        _tiles[metric].Update(ValueOf(metric), stages, _palette);
    }

    private double? ValueOf(HardwareMetrics metric) => metric switch
    {
        HardwareMetrics.CpuTemperature => _cpuTemperature,
        HardwareMetrics.CpuUsage => _cpuUsagePercent,
        HardwareMetrics.MemoryUsage => _memoryUsagePercent,
        HardwareMetrics.GpuTemperature => _gpuTemperature,
        HardwareMetrics.GpuComputeUsage => _gpuComputeUsagePercent,
        HardwareMetrics.GpuMemoryUsage => _gpuMemoryUsagePercent,
        HardwareMetrics.GpuMemoryTemperature => _gpuMemoryTemperature,
        HardwareMetrics.GpuFan => _gpuFanRpm,
        HardwareMetrics.MotherboardTemperature => _motherboardTemperature,
        HardwareMetrics.MemoryTemperature => _memoryTemperature,
        HardwareMetrics.CpuFan => _cpuFanRpm,
        HardwareMetrics.StorageTemperature => _storageTemperature,
        HardwareMetrics.Power => _powerWatts,
        HardwareMetrics.CpuPower => _cpuPowerWatts,
        HardwareMetrics.GpuPower => _gpuPowerWatts,
        HardwareMetrics.GpuHotSpotTemperature => _gpuHotSpotTemperature,
        _ => null,
    };

    private void RefreshAlertText()
    {
        var alerts = new List<string>(3);
        if (_current.IsVisible(HardwareMetrics.CpuTemperature)
            && _cpuTemperature is >= CpuThrottleTemperature)
        {
            alerts.Add("⚠ CPU THROTTLING");
        }

        if ((_current.IsVisible(HardwareMetrics.GpuHotSpotTemperature)
                && _gpuHotSpotTemperature is >= GpuHotSpotThrottleTemperature)
            || (_current.IsVisible(HardwareMetrics.GpuTemperature)
                && _gpuTemperature is >= GpuCoreThrottleTemperature))
        {
            alerts.Add("⚠ GPU THROTTLING");
        }

        if (_hasWheaHardwareError)
        {
            alerts.Add("⚠ WHEA HARDWARE ERROR");
        }

        AlertText = string.Join("   ", alerts);
    }

    private void RecordSample(HardwareMetrics metric, double? value, DateTimeOffset recordedAt) =>
        _tiles[metric].RecordSample(value, recordedAt);

    private void AddSensorTiles(
        IEnumerable<HardwareSensorOption> sensors,
        Func<string, string> keyOf,
        HardwareMetrics metric,
        MetricFormat format)
    {
        foreach (var sensor in sensors)
        {
            var key = keyOf(sensor.Id);
            _sensorIds[key] = sensor.Id;
            _sensorTiles[key] = new MetricTileViewModel(metric, sensor.Label, format);
        }
    }

    private void ApplySensorMetrics(HardwareSnapshot snapshot, HardwareMetrics sampled, DateTimeOffset recordedAt)
    {
        foreach (var (key, sensorId) in _sensorIds)
        {
            var applicable = SensorMetricKeys.IsDrive(key)
                ? sampled.Includes(HardwareMetrics.StorageTemperature)
                : sampled.Includes(HardwareMetrics.CpuFan);
            if (!applicable)
            {
                continue;
            }

            var value = snapshot.SensorValues.GetValueOrDefault(sensorId);
            _sensorValues[key] = value;
            RefreshSensorTile(key, _sensorTiles[key], value);
            _sensorTiles[key].RecordSample(value, recordedAt);
        }
    }

    private void RefreshSensorTile(string key, MetricTileViewModel tile, double? value)
    {
        var defaultMetricType = SensorMetricKeys.IsDrive(key)
            ? MetricTypes.StorageTemperature
            : MetricTypes.CpuFanRpm;
        tile.Update(value, _current.ResolveStages(key, defaultMetricType), _palette);
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action);
    }
}
