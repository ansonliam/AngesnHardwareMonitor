using System.Globalization;
using AngesnHardwareWidget.Settings;

namespace AngesnHardwareWidget.ViewModels;

/// <summary>
/// One metric's row in the stage-threshold editor: four editable cut points plus the scale they
/// sit on. Values are held as text so a half-typed number does not blow up binding; each edit
/// raises <see cref="Edited"/> so the dialog can apply the change live, and text that is not yet a
/// valid set of thresholds simply is not committed.
/// </summary>
public sealed class MetricStageRowViewModel : ObservableObject
{
    private readonly string _metricType;

    private bool _isVisible;
    private bool _isGraphVisible;
    private string _stage1Maximum;
    private string _stage2Maximum;
    private string _stage3Maximum;
    private string _stage4Maximum;
    private bool _suppressEdited;

    public MetricStageRowViewModel(
        string metricType,
        string label,
        string unit,
        MetricStageSettings stages,
        bool isVisible,
        bool isGraphVisible)
    {
        _metricType = metricType;
        Label = label;
        Unit = unit;
        _isVisible = isVisible;
        _isGraphVisible = isGraphVisible;
        Minimum = stages.Minimum;
        Maximum = stages.Maximum;

        _stage1Maximum = Format(stages.Stage1Maximum);
        _stage2Maximum = Format(stages.Stage2Maximum);
        _stage3Maximum = Format(stages.Stage3Maximum);
        _stage4Maximum = Format(stages.Stage4Maximum);
    }

    /// <summary>Raised whenever one of the four cut points is edited.</summary>
    public event EventHandler? Edited;

    public string MetricType => _metricType;

    public string Label { get; }

    /// <summary>Whether this metric appears in the widget. Applies live, like the thresholds.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                Edited?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Whether this metric's history sparkline is shown. Applies live, like the thresholds.</summary>
    public bool IsGraphVisible
    {
        get => _isGraphVisible;
        set
        {
            if (SetProperty(ref _isGraphVisible, value))
            {
                Edited?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>"°C", "%" or "RPM", shown so the numbers are unambiguous.</summary>
    public string Unit { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    /// <summary>The scale each row is graded over, e.g. "35-95 °C".</summary>
    public string ScaleText => $"{Format(Minimum)}-{Format(Maximum)} {Unit}";

    public string Stage1Maximum
    {
        get => _stage1Maximum;
        set => SetStage(ref _stage1Maximum, value);
    }

    public string Stage2Maximum
    {
        get => _stage2Maximum;
        set => SetStage(ref _stage2Maximum, value);
    }

    public string Stage3Maximum
    {
        get => _stage3Maximum;
        set => SetStage(ref _stage3Maximum, value);
    }

    public string Stage4Maximum
    {
        get => _stage4Maximum;
        set => SetStage(ref _stage4Maximum, value);
    }

    /// <summary>Parses the four cut points back into settings, or fails if they are not valid.</summary>
    public bool TryBuild(out MetricStageSettings stages)
    {
        stages = new MetricStageSettings { Minimum = Minimum, Maximum = Maximum };

        if (!TryParse(Stage1Maximum, out var stage1)
            || !TryParse(Stage2Maximum, out var stage2)
            || !TryParse(Stage3Maximum, out var stage3)
            || !TryParse(Stage4Maximum, out var stage4))
        {
            return false;
        }

        stages.Stage1Maximum = stage1;
        stages.Stage2Maximum = stage2;
        stages.Stage3Maximum = stage3;
        stages.Stage4Maximum = stage4;
        return stages.IsValid();
    }

    /// <summary>
    /// Rewrites all four boxes from the given thresholds and raises <see cref="Edited"/> once, not
    /// four times, so resetting to defaults is a single live update.
    /// </summary>
    public void Reset(MetricStageSettings stages)
    {
        _suppressEdited = true;
        try
        {
            Stage1Maximum = Format(stages.Stage1Maximum);
            Stage2Maximum = Format(stages.Stage2Maximum);
            Stage3Maximum = Format(stages.Stage3Maximum);
            Stage4Maximum = Format(stages.Stage4Maximum);
        }
        finally
        {
            _suppressEdited = false;
        }

        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void SetStage(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName) || _suppressEdited)
        {
            return;
        }

        Edited?.Invoke(this, EventArgs.Empty);
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);
}
