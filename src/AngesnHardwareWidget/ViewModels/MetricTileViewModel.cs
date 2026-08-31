using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AngesnHardwareWidget.Models;
using AngesnHardwareWidget.Services;
using AngesnHardwareWidget.Settings;

namespace AngesnHardwareWidget.ViewModels;

/// <summary>How a metric's raw number becomes display text.</summary>
public enum MetricFormat
{
    /// <summary>Rounded integer with a degree sign.</summary>
    Temperature,

    /// <summary>Rounded integer with a percent sign.</summary>
    Percent,

    /// <summary>Rounded integer, no unit. 0 is a real value here.</summary>
    Rpm,

    /// <summary>Rounded integer watts.</summary>
    Watts,

    /// <summary>RAM, which switches between "36%" and "23.2/63.9 GB (36%)".</summary>
    Memory,
}

/// <summary>
/// One row of the widget: its label, its formatted value and the stage colour that value falls in.
/// </summary>
public sealed class MetricTileViewModel : ObservableObject
{
    private const string Unavailable = "--";
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromMinutes(15);
    private const int MaximumHistoryPoints = 2048;

    private string _display = Unavailable;
    private string _label;
    private Brush? _valueBrush;
    private int _stage;
    private double _graphMaximum;
    private bool _showGraph = true;
    private GridLength _valueColumnWidth = new(1, GridUnitType.Star);

    public MetricTileViewModel(HardwareMetrics metric, string label, MetricFormat format)
    {
        Metric = metric;
        _label = label;
        Format = format;
        _graphMaximum = format == MetricFormat.Rpm ? 1000 : 100;
    }

    public HardwareMetrics Metric { get; }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public MetricFormat Format { get; }

    public ObservableCollection<HardwareHistoryPoint> History { get; } = [];

    public double GraphMaximum
    {
        get => _graphMaximum;
        private set => SetProperty(ref _graphMaximum, value);
    }

    /// <summary>Whether this metric's history sparkline is shown. Set from settings, not by the user directly.</summary>
    public bool ShowGraph
    {
        get => _showGraph;
        set => SetProperty(ref _showGraph, value);
    }

    /// <summary>
    /// This row's own value-column width. Ordinarily every row shares the same width, but the RAM
    /// override only widens the RAM row, so each tile carries its own rather than the widget
    /// binding all rows to one shared resource. Set from settings, not by the user directly.
    /// </summary>
    public GridLength ValueColumnWidth
    {
        get => _valueColumnWidth;
        set => SetProperty(ref _valueColumnWidth, value);
    }

    public string Display
    {
        get => _display;
        private set => SetProperty(ref _display, value);
    }

    /// <summary>1-5 for a graded reading, 0 when there is no reading.</summary>
    public int Stage
    {
        get => _stage;
        private set => SetProperty(ref _stage, value);
    }

    public Brush? ValueBrush
    {
        get => _valueBrush;
        private set => SetProperty(ref _valueBrush, value);
    }

    /// <summary>
    /// Recomputes text and colour. <paramref name="gradedValue"/> is the number the stages apply
    /// to, which for RAM is the percentage even when the text shows used/total GB as well.
    /// </summary>
    public void Update(
        double? gradedValue,
        MetricStageSettings stages,
        MetricStagePalette palette,
        double? memoryUsedGb = null,
        double? memoryTotalGb = null,
        bool showMemoryUsedAndTotal = false)
    {
        Display = Format switch
        {
            MetricFormat.Temperature => FormatNumber(gradedValue, "0", "°"),
            MetricFormat.Percent => FormatNumber(gradedValue, "0", "%"),

            // 0 RPM is valid (zero-fan idle mode) and must not read as a missing sensor.
            MetricFormat.Rpm => FormatNumber(gradedValue, "0", string.Empty),

            MetricFormat.Watts => FormatNumber(gradedValue, "0", " W"),

            MetricFormat.Memory => FormatMemory(gradedValue, memoryUsedGb, memoryTotalGb, showMemoryUsedAndTotal),
            _ => Unavailable,
        };

        Stage = stages.StageOf(gradedValue);
        ValueBrush = palette.BrushForStage(Stage);
    }

    public void RecordSample(double? value, DateTimeOffset recordedAt)
    {
        if (value is not { } number || !double.IsFinite(number))
        {
            return;
        }

        var point = new HardwareHistoryPoint(recordedAt, number);
        if (History.Count > 0)
        {
            var last = History[^1];
            if (recordedAt < last.RecordedAt)
            {
                return;
            }

            if (recordedAt == last.RecordedAt)
            {
                History[^1] = point;
                UpdateGraphMaximum();
                return;
            }
        }

        History.Add(point);
        var cutoff = recordedAt - HistoryWindow;
        while (History.Count > 1 && History[1].RecordedAt < cutoff)
        {
            History.RemoveAt(0);
        }

        while (History.Count > MaximumHistoryPoints)
        {
            History.RemoveAt(0);
        }

        UpdateGraphMaximum();
    }

    private void UpdateGraphMaximum()
    {
        if (Format != MetricFormat.Rpm)
        {
            return;
        }

        var peak = History.Count == 0 ? 0 : History.Max(point => point.Value);
        GraphMaximum = Math.Max(1000, Math.Ceiling(peak / 500) * 500);
    }

    private static string FormatNumber(double? value, string format, string suffix) =>
        value is { } number
            ? Math.Round(number).ToString(format, CultureInfo.CurrentCulture) + suffix
            : Unavailable;

    private static string FormatMemory(double? percent, double? usedGb, double? totalGb, bool expanded)
    {
        if (!expanded)
        {
            return FormatNumber(percent, "0", "%");
        }

        if (usedGb is not { } used || totalGb is not { } total)
        {
            return $"{Unavailable}/{Unavailable} GB ({Unavailable})";
        }

        var percentText = percent is { } value
            ? value.ToString("0", CultureInfo.CurrentCulture) + "%"
            : Unavailable;

        return string.Format(CultureInfo.CurrentCulture, "{0:0.0}/{1:0.0} GB ({2})", used, total, percentText);
    }
}

public sealed record HardwareHistoryPoint(DateTimeOffset RecordedAt, double Value);
