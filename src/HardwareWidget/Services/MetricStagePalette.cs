using System.Windows.Media;
using HardwareWidget.Settings;

namespace HardwareWidget.Services;

/// <summary>
/// Turns a reading plus its metric's stage thresholds into a brush. Brushes are parsed once per
/// settings change and frozen, so the polling loop never allocates or crosses a thread boundary to
/// colour a value.
/// </summary>
public sealed class MetricStagePalette
{
    private readonly Brush[] _stages;
    private readonly Brush _unavailable;

    public MetricStagePalette(AppSettings settings)
    {
        _stages =
        [
            Parse(settings.Stage1Color, "#2ECC71"),
            Parse(settings.Stage2Color, "#9ACD32"),
            Parse(settings.Stage3Color, "#FFD21E"),
            Parse(settings.Stage4Color, "#FF9800"),
            Parse(settings.Stage5Color, "#FF4D4F"),
        ];

        _unavailable = Parse(settings.UnavailableColor, "#59616B");
    }

    /// <summary>Stage 1-5 maps to the configured colours; a missing reading uses the muted colour.</summary>
    public Brush BrushForStage(int stage) =>
        stage is >= 1 and <= 5 ? _stages[stage - 1] : _unavailable;

    /// <summary>A bad hex value in a hand-edited settings file falls back rather than throwing.</summary>
    private static Brush Parse(string? value, string fallback)
    {
        var brush = new SolidColorBrush(TryParseColor(value) ?? TryParseColor(fallback)!.Value);
        brush.Freeze();
        return brush;
    }

    private static Color? TryParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return ColorConverter.ConvertFromString(value) as Color?;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
