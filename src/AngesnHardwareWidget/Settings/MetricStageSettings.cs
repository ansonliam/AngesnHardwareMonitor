using AngesnHardwareWidget.Models;

namespace AngesnHardwareWidget.Settings;

/// <summary>
/// Five colour stages for one metric, expressed as a scale (<see cref="Minimum"/> to
/// <see cref="Maximum"/>) plus the four cut points that divide it.
///
/// The scale is per metric rather than a blanket 0-100, because 0-100 is meaningless for most of
/// these readings: a CPU is never near 0 C and rarely past 95 C, so grading it over 0-100 would
/// leave every realistic temperature crammed into the first two stages. Each metric instead gets a
/// base and a ceiling that bracket its real operating range, and the defaults simply split that
/// range into five equal parts.
///
/// Stage 4 owns its own boundary (value &lt;= Stage4Maximum), so stage 5 is everything past it and
/// needs no cut point of its own -- five stages, four numbers.
/// </summary>
public sealed class MetricStageSettings
{
    public double Minimum { get; set; }

    public double Maximum { get; set; }

    public double Stage1Maximum { get; set; }

    public double Stage2Maximum { get; set; }

    public double Stage3Maximum { get; set; }

    public double Stage4Maximum { get; set; }

    /// <summary>Builds the default stages for a metric by splitting its range into equal fifths.</summary>
    public static MetricStageSettings FromRange(double minimum, double maximum)
    {
        var step = (maximum - minimum) / 5d;
        return new MetricStageSettings
        {
            Minimum = minimum,
            Maximum = maximum,
            Stage1Maximum = minimum + step,
            Stage2Maximum = minimum + (step * 2),
            Stage3Maximum = minimum + (step * 3),
            Stage4Maximum = minimum + (step * 4),
        };
    }

    /// <summary>
    /// Default operating ranges. These are the "reasonable base" for each metric -- deliberately
    /// not 0-100 for the temperatures, and not 0-100 for fan RPM, which is not a percentage at all.
    /// </summary>
    public static MetricStageSettings Default(string metricType) => metricType switch
    {
        // A running CPU idles in the 30s and throttles in the 90s.
        MetricTypes.CpuTemperature => FromRange(35, 95),

        // GPUs idle cooler than CPUs and are usually happy to ~85 C.
        MetricTypes.GpuTemperature => FromRange(30, 90),

        // VRAM junction runs hotter than the core; GDDR6X is specified to around 105 C.
        MetricTypes.GpuMemoryTemperature => FromRange(40, 100),

        // Utilisation genuinely spans its whole range, and idle should read as healthy.
        MetricTypes.CpuUsage => FromRange(0, 100),
        MetricTypes.GpuComputeUsage => FromRange(0, 100),

        // Memory is never really near empty once an OS is loaded, so the scale starts at 30%.
        MetricTypes.MemoryUsagePercent => FromRange(30, 100),
        MetricTypes.GpuMemoryUsage => FromRange(30, 100),

        // RPM, not a percentage. 0 (zero-fan idle) through a typical ~3000 RPM maximum.
        MetricTypes.GpuFanRpm => FromRange(0, 3000),

        _ => FromRange(0, 100),
    };

    /// <summary>Which of the five stages a reading falls in, 1-based. Returns 0 for no reading.</summary>
    public int StageOf(double? value) => value switch
    {
        null => 0,
        var reading when reading <= Stage1Maximum => 1,
        var reading when reading <= Stage2Maximum => 2,
        var reading when reading <= Stage3Maximum => 3,
        var reading when reading <= Stage4Maximum => 4,
        _ => 5,
    };

    /// <summary>The four cut points must be finite, strictly increasing and inside the scale.</summary>
    public bool IsValid() =>
        double.IsFinite(Minimum) && double.IsFinite(Maximum) && Minimum < Maximum
        && double.IsFinite(Stage1Maximum) && double.IsFinite(Stage2Maximum)
        && double.IsFinite(Stage3Maximum) && double.IsFinite(Stage4Maximum)
        && Minimum <= Stage1Maximum
        && Stage1Maximum < Stage2Maximum
        && Stage2Maximum < Stage3Maximum
        && Stage3Maximum < Stage4Maximum
        && Stage4Maximum <= Maximum;

    public MetricStageSettings Clone() => new()
    {
        Minimum = Minimum,
        Maximum = Maximum,
        Stage1Maximum = Stage1Maximum,
        Stage2Maximum = Stage2Maximum,
        Stage3Maximum = Stage3Maximum,
        Stage4Maximum = Stage4Maximum,
    };
}
