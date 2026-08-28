using System.Collections.ObjectModel;
using System.Windows.Threading;
using AngesnHardwareWidget.Models;
using AngesnHardwareWidget.Services;
using AngesnHardwareWidget.Settings;

namespace AngesnHardwareWidget.ViewModels;

/// <summary>
/// The widget's state. Holds the raw nullable numbers and exposes the eight display rows; all
/// formatting and colouring happens here in the presentation layer, never in the hardware service.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly Dispatcher _dispatcher;
    private readonly SettingsService _settings;
    private readonly Dictionary<HardwareMetrics, MetricTileViewModel> _tiles;

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

    public MainViewModel(
        SettingsService settings,
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
            [HardwareMetrics.CpuTemperature] = new(HardwareMetrics.CpuTemperature, "CPU TEMP", MetricFormat.Temperature),
            [HardwareMetrics.CpuUsage] = new(HardwareMetrics.CpuUsage, "CPU USE", MetricFormat.Percent),
            [HardwareMetrics.MemoryUsage] = new(HardwareMetrics.MemoryUsage, "RAM", MetricFormat.Memory),
            [HardwareMetrics.GpuTemperature] = new(HardwareMetrics.GpuTemperature, "GPU TEMP", MetricFormat.Temperature),
            [HardwareMetrics.GpuComputeUsage] = new(HardwareMetrics.GpuComputeUsage, "GPU CORE", MetricFormat.Percent),
            [HardwareMetrics.GpuMemoryUsage] = new(HardwareMetrics.GpuMemoryUsage, "GPU MEM", MetricFormat.Percent),
            [HardwareMetrics.GpuMemoryTemperature] = new(HardwareMetrics.GpuMemoryTemperature, "VRAM TEMP", MetricFormat.Temperature),
            [HardwareMetrics.GpuFan] = new(HardwareMetrics.GpuFan, "GPU FAN", MetricFormat.Rpm),
        };

        Metrics = [];
        ApplyPerTilePresentation();
        RebuildMetrics();
        RefreshAllTiles();

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
    });

    private void ApplySettings(AppSettings updated)
    {
        _current = updated;
        _palette = new MetricStagePalette(updated);
        OnPropertyChanged(nameof(WidgetAppearance));
        ApplyPerTilePresentation();
        RebuildMetrics();
        RefreshAllTiles();
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
            tile.ShowGraph = _current.IsGraphVisible(metric);
            tile.ValueColumnWidth = metric == HardwareMetrics.MemoryUsage ? ramValueColumnWidth : valueColumnWidth;
        }
    }

    /// <summary>
    /// Rebuilds the displayed rows from the persisted order and visibility. The tile objects
    /// themselves are reused from the cache, so hiding a metric and showing it again does not lose
    /// its last reading or make the row flash as unavailable.
    /// </summary>
    private void RebuildMetrics()
    {
        var desired = _current.ResolveDisplayOrder();

        if (Metrics.Count == desired.Count
            && !Metrics.Where((tile, index) => tile.Metric != desired[index]).Any())
        {
            return;
        }

        Metrics.Clear();
        foreach (var metric in desired)
        {
            Metrics.Add(_tiles[metric]);
        }
    }

    private void RefreshAllTiles()
    {
        foreach (var metric in HardwareMetricsExtensions.Individual)
        {
            RefreshTile(metric);
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
        _ => null,
    };

    private void RecordSample(HardwareMetrics metric, double? value, DateTimeOffset recordedAt) =>
        _tiles[metric].RecordSample(value, recordedAt);

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
