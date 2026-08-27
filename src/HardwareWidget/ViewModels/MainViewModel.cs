using System.Collections.ObjectModel;
using System.Windows.Threading;
using HardwareWidget.Models;
using HardwareWidget.Services;
using HardwareWidget.Settings;

namespace HardwareWidget.ViewModels;

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

    public MainViewModel(SettingsService settings, Dispatcher dispatcher, Action openSettings, Action exit)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _current = settings.Current;
        _palette = new MetricStagePalette(_current);

        OpenSettingsCommand = new RelayCommand(openSettings);
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

        // Display order matches the spec's readout.
        Metrics = new ObservableCollection<MetricTileViewModel>(
            HardwareMetricsExtensions.Individual.Select(metric => _tiles[metric]));

        RefreshAllTiles();

        _settings.SettingsChanged += (_, updated) => RunOnUi(() => ApplySettings(updated));
    }

    public ObservableCollection<MetricTileViewModel> Metrics { get; }

    public RelayCommand OpenSettingsCommand { get; }

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

        if (sampled.Includes(HardwareMetrics.CpuTemperature))
        {
            _cpuTemperature = snapshot.CpuTemperature;
            OnPropertyChanged(nameof(CpuTemperature));
            RefreshTile(HardwareMetrics.CpuTemperature);
        }

        if (sampled.Includes(HardwareMetrics.CpuUsage))
        {
            _cpuUsagePercent = snapshot.CpuUsagePercent;
            OnPropertyChanged(nameof(CpuUsagePercent));
            RefreshTile(HardwareMetrics.CpuUsage);
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
        }

        if (sampled.Includes(HardwareMetrics.GpuTemperature))
        {
            _gpuTemperature = snapshot.GpuTemperature;
            OnPropertyChanged(nameof(GpuTemperature));
            RefreshTile(HardwareMetrics.GpuTemperature);
        }

        if (sampled.Includes(HardwareMetrics.GpuComputeUsage))
        {
            _gpuComputeUsagePercent = snapshot.GpuComputeUsagePercent;
            OnPropertyChanged(nameof(GpuComputeUsagePercent));
            RefreshTile(HardwareMetrics.GpuComputeUsage);
        }

        if (sampled.Includes(HardwareMetrics.GpuMemoryUsage))
        {
            _gpuMemoryUsagePercent = snapshot.GpuMemoryUsagePercent;
            OnPropertyChanged(nameof(GpuMemoryUsagePercent));
            RefreshTile(HardwareMetrics.GpuMemoryUsage);
        }

        if (sampled.Includes(HardwareMetrics.GpuMemoryTemperature))
        {
            _gpuMemoryTemperature = snapshot.GpuMemoryTemperature;
            OnPropertyChanged(nameof(GpuMemoryTemperature));
            RefreshTile(HardwareMetrics.GpuMemoryTemperature);
        }

        if (sampled.Includes(HardwareMetrics.GpuFan))
        {
            _gpuFanRpm = snapshot.GpuFanRpm;
            OnPropertyChanged(nameof(GpuFanRpm));
            RefreshTile(HardwareMetrics.GpuFan);
        }
    });

    private void ApplySettings(AppSettings updated)
    {
        _current = updated;
        _palette = new MetricStagePalette(updated);
        OnPropertyChanged(nameof(WidgetAppearance));
        RefreshAllTiles();
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
