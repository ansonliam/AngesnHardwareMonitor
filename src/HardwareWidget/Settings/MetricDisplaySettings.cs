namespace HardwareWidget.Settings;

/// <summary>
/// Whether one metric appears in the widget. Order is carried by the position of these entries in
/// <see cref="AppSettings.MetricDisplay"/> rather than by an index field, so reordering is a list
/// move and there is no way for two metrics to claim the same position.
/// </summary>
public sealed class MetricDisplaySettings
{
    /// <summary>The stable MetricType key, e.g. "gpu.compute_usage".</summary>
    public string MetricType { get; set; } = string.Empty;

    public bool Visible { get; set; } = true;

    public MetricDisplaySettings Clone() => new()
    {
        MetricType = MetricType,
        Visible = Visible,
    };
}
