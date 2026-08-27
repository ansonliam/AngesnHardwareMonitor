using System.Globalization;
using System.Windows.Media;
using HardwareWidget.Models;
using HardwareWidget.Services;
using HardwareWidget.Settings;

namespace HardwareWidget.ViewModels;

/// <summary>How a metric's raw number becomes display text.</summary>
public enum MetricFormat
{
    /// <summary>Rounded integer with a degree sign.</summary>
    Temperature,

    /// <summary>Rounded integer with a percent sign.</summary>
    Percent,

    /// <summary>Rounded integer, no unit. 0 is a real value here.</summary>
    Rpm,

    /// <summary>RAM, which switches between "36%" and "23.2/63.9 GB (36%)".</summary>
    Memory,
}

/// <summary>
/// One row of the widget: its label, its formatted value and the stage colour that value falls in.
/// </summary>
public sealed class MetricTileViewModel : ObservableObject
{
    private const string Unavailable = "--";

    private string _display = Unavailable;
    private Brush? _valueBrush;
    private int _stage;

    public MetricTileViewModel(HardwareMetrics metric, string label, MetricFormat format)
    {
        Metric = metric;
        Label = label;
        Format = format;
    }

    public HardwareMetrics Metric { get; }

    public string Label { get; }

    public MetricFormat Format { get; }

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

            MetricFormat.Memory => FormatMemory(gradedValue, memoryUsedGb, memoryTotalGb, showMemoryUsedAndTotal),
            _ => Unavailable,
        };

        Stage = stages.StageOf(gradedValue);
        ValueBrush = palette.BrushForStage(Stage);
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
