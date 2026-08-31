using AngesnHardwareWidget.Models;
using AngesnHardwareWidget.Services;
using AngesnHardwareWidget.Settings;

namespace AngesnHardwareWidget.ViewModels;

/// <summary>File-free sample data used only by the XAML Designer.</summary>
public sealed class MainWindowDesignViewModel
{
    public MainWindowDesignViewModel()
    {
        var settings = new AppSettings().Normalized();
        var palette = new MetricStagePalette(settings);
        var now = DateTimeOffset.Now;

        Metrics =
        [
            Create(HardwareMetrics.CpuUsage, "CPU USE", MetricFormat.Percent, 24, 11, settings, palette, now, 0.2),
            Create(HardwareMetrics.CpuTemperature, "CPU TEMP", MetricFormat.Temperature, 63, 3, settings, palette, now, 1.1),
            Create(HardwareMetrics.GpuComputeUsage, "GPU CORE", MetricFormat.Percent, 31, 15, settings, palette, now, 2.2),
            Create(HardwareMetrics.GpuTemperature, "GPU TEMP", MetricFormat.Temperature, 53, 4, settings, palette, now, 0.8),
            Create(HardwareMetrics.MemoryUsage, "RAM", MetricFormat.Memory, 48, 2, settings, palette, now, 1.7),
            Create(HardwareMetrics.GpuFan, "GPU FAN", MetricFormat.Rpm, 1019, 180, settings, palette, now, 0.5),
            Create(HardwareMetrics.GpuMemoryUsage, "GPU MEM", MetricFormat.Percent, 19, 5, settings, palette, now, 2.8),
            Create(HardwareMetrics.GpuMemoryTemperature, "VRAM TEMP", MetricFormat.Temperature, 70, 3, settings, palette, now, 1.4),
            Create(HardwareMetrics.MotherboardTemperature, "MB TEMP", MetricFormat.Temperature, 42, 2, settings, palette, now, 0.4),
            Create(HardwareMetrics.MemoryTemperature, "RAM TEMP", MetricFormat.Temperature, 44, 2, settings, palette, now, 0.9),
            Create(HardwareMetrics.CpuFan, "CPU FAN", MetricFormat.Rpm, 980, 120, settings, palette, now, 1.8),
            Create(HardwareMetrics.StorageTemperature, "DRIVE TEMP", MetricFormat.Temperature, 48, 2, settings, palette, now, 2.4),
            Create(HardwareMetrics.Power, "POWER", MetricFormat.Watts, 160, 35, settings, palette, now, 1.2),
            Create(HardwareMetrics.GpuHotSpotTemperature, "GPU HOTSPOT", MetricFormat.Temperature, 64, 4, settings, palette, now, 2.1),
        ];
    }

    public IReadOnlyList<MetricTileViewModel> Metrics { get; }

    public string AlertText => "⚠ CPU THROTTLING   ⚠ WHEA HARDWARE ERROR";

    private static MetricTileViewModel Create(
        HardwareMetrics metric,
        string label,
        MetricFormat format,
        double currentValue,
        double variation,
        AppSettings settings,
        MetricStagePalette palette,
        DateTimeOffset now,
        double phase)
    {
        var tile = new MetricTileViewModel(metric, label, format);
        for (var index = 0; index <= 30; index++)
        {
            var value = currentValue
                + (Math.Sin((index * 0.72) + phase) * variation)
                + (Math.Sin((index * 0.19) + phase) * variation * 0.35);
            tile.RecordSample(Math.Max(0, value), now.AddSeconds(-900 + (index * 30)));
        }

        tile.Update(
            currentValue,
            settings.ResolveStages(metric),
            palette,
            memoryUsedGb: metric == HardwareMetrics.MemoryUsage ? 31.1 : null,
            memoryTotalGb: metric == HardwareMetrics.MemoryUsage ? 63.9 : null,
            showMemoryUsedAndTotal: false);
        return tile;
    }
}
