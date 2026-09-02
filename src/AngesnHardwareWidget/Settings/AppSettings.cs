using System.Globalization;
using AngesnHardwareWidget.Models;

namespace AngesnHardwareWidget.Settings;

/// <summary>
/// Persisted user settings, serialised as JSON under %LOCALAPPDATA%\AngesnHardwareWidget\settings.json.
/// Defaults here are the shipped defaults: unified polling on, 30 seconds, RAM used/total hidden.
/// </summary>
public sealed class AppSettings
{
    public const int MinimumPollingSeconds = 1;
    public const int DefaultPollingSeconds = 30;
    public const int MaximumPollingSeconds = 300;

    // Idle polling gets a wider ceiling than active polling: the whole point is to back right off
    // while nobody is looking, and an hour between samples is a reasonable thing to want.
    public const int MinimumIdlePollingSeconds = 5;
    public const int DefaultIdlePollingSeconds = 300;
    public const int MaximumIdlePollingSeconds = 3600;

    public const int MinimumIdleAfterSeconds = 30;
    public const int DefaultIdleAfterSeconds = 300;
    public const int MaximumIdleAfterSeconds = 3600;

    public const string RetroAppearance = "Retro";
    public const string DefaultAppearance = "Default";

    // Each column width is either a fixed pixel size or "*", exactly like a Grid's own
    // ColumnDefinition.Width syntax -- "*" tells that column to take up whatever space the other
    // two do not need, the same way the value column always has. A fixed size keeps every row's
    // label, graph and value starting at the same x down the card; Auto would size each column to
    // its own longest content instead of a shared width.
    public const string StarColumnWidth = "*";

    public const string DefaultLabelColumnWidth = "78";
    public const double MinimumLabelColumnWidth = 1;
    public const double MaximumLabelColumnWidth = 140;

    public const string DefaultGraphColumnWidth = "58";
    public const double MinimumGraphColumnWidth = 1;
    public const double MaximumGraphColumnWidth = 120;

    public const string DefaultValueColumnWidth = StarColumnWidth;
    public const double MinimumValueColumnWidth = 1;
    public const double MaximumValueColumnWidth = 400;

    // "*" here would look identical to the normal value column, defeating the point of an
    // override, so the RAM-expanded default is a fixed width wide enough for "23.2/63.9 GB (36%)".
    public const string DefaultValueColumnWidthWithRam = "150";

    // How narrow a metric column may get, per MetricColumnsPanel, before the widget folds its
    // columns back down (eventually to one). Below this, another column of rows would not fit
    // without the row itself becoming too cramped to read.
    public const double DefaultMinimumColumnWidth = 130;
    public const double MinimumMinimumColumnWidth = 1;
    public const double MaximumMinimumColumnWidth = 1000;

    // The RAM-expanded value ("23.2/63.9 GB (36%)") needs a wider column before it is worth
    // splitting into another one, same reasoning as the value-column override above.
    public const double DefaultMinimumColumnWidthWithRam = 180;

    // The graph has no fixed height of its own: it stretches to whatever height its row ends up
    // (driven by the label and value text next to it), bounded by these two. The floor and ceiling
    // below are what the min/max settings are themselves clamped to, so a bad value in either
    // cannot squeeze the graph out of existence or blow the widget up to something absurd.
    public const double AbsoluteMinimumGraphHeight = 1;
    public const double AbsoluteMaximumGraphHeight = 1000;

    public const double DefaultGraphHeightMinimum = 1;
    public const double DefaultGraphHeightMaximum = 200;

    public const string SystemFont = "Segoe UI Variable Text";

    /// <summary>
    /// Font choices in dropdown order, identical to the AI Usage Monitor's list. The first is the
    /// system font; every other name is an embedded family under Assets/fonts.
    /// </summary>
    public static IReadOnlyList<string> FontChoices { get; } =
    [
        SystemFont,
        "VT323",
        "Pixelify Sans",
        "Silkscreen",
        "Tiny5",
        "Space Mono",
        "Chakra Petch",
        "IBM Plex Mono",
        "DotGothic16",
        "Handjet",
        "Rajdhani",
        "Oxanium",
        "Kode Mono",
    ];

    public static IReadOnlyList<string> TextWeightChoices { get; } = ["Normal", "SemiBold", "Bold"];

    /// <summary>
    /// Active polling intervals offered in the UI, in dropdown order. Shared by the settings dialog
    /// and the widget's right-click menu.
    /// </summary>
    public static IReadOnlyList<int> OfferedIntervalSeconds { get; } = [5, 10, 30, 60, 120, 300];

    public bool UseUnifiedPollingInterval { get; set; } = true;

    /// <summary>Show CPU and GPU power as one combined widget row instead of two rows.</summary>
    public bool ConsolidatePower { get; set; }

    public int UnifiedPollingSeconds { get; set; } = DefaultPollingSeconds;

    /// <summary>Presentation only. Never affects what is collected or stored in history.</summary>
    public bool ShowRamUsedAndTotal { get; set; }

    /// <summary>Width of the label column: a pixel size before scaling, or "*". Presentation only.</summary>
    public string WidgetLabelColumnWidth { get; set; } = DefaultLabelColumnWidth;

    /// <summary>Width of the history-graph column: a pixel size before scaling, or "*". Presentation only.</summary>
    public string WidgetGraphColumnWidth { get; set; } = DefaultGraphColumnWidth;

    /// <summary>Width of the value column: a pixel size before scaling, or "*". Presentation only.</summary>
    public string WidgetValueColumnWidth { get; set; } = DefaultValueColumnWidth;

    /// <summary>
    /// Overrides <see cref="WidgetValueColumnWidth"/> while <see cref="ShowRamUsedAndTotal"/> is on,
    /// since "23.2/63.9 GB (36%)" needs noticeably more room than every other metric's value ever
    /// does. Same syntax as the other column widths: a pixel size before scaling, or "*".
    /// </summary>
    public string WidgetValueColumnWidthWithRam { get; set; } = DefaultValueColumnWidthWithRam;

    /// <summary>
    /// Narrowest a metric column may get before the widget folds its columns back down --
    /// eventually to a single column of rows. Presentation only.
    /// </summary>
    public double WidgetMinimumColumnWidth { get; set; } = DefaultMinimumColumnWidth;

    /// <summary>Overrides <see cref="WidgetMinimumColumnWidth"/> while <see cref="ShowRamUsedAndTotal"/> is on.</summary>
    public double WidgetMinimumColumnWidthWithRam { get; set; } = DefaultMinimumColumnWidthWithRam;

    /// <summary>
    /// Lower bound on the history graph's height, before scaling: the graph stretches to fill its
    /// row, clamped between this and <see cref="WidgetGraphHeightMaximum"/>. Presentation only.
    /// </summary>
    public double WidgetGraphHeightMinimum { get; set; } = DefaultGraphHeightMinimum;

    /// <summary>Upper bound on the history graph's height, before scaling. Presentation only.</summary>
    public double WidgetGraphHeightMaximum { get; set; } = DefaultGraphHeightMaximum;

    public int CpuTemperaturePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int CpuUsagePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int MemoryUsagePollingSeconds { get; set; } = DefaultPollingSeconds;

    public int GpuTemperaturePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int GpuComputeUsagePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int GpuMemoryUsagePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int GpuMemoryTemperaturePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int GpuFanPollingSeconds { get; set; } = DefaultPollingSeconds;
    public int MotherboardTemperaturePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int MemoryTemperaturePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int CpuFanPollingSeconds { get; set; } = DefaultPollingSeconds;
    public int StorageTemperaturePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int PowerPollingSeconds { get; set; } = DefaultPollingSeconds;
    public int GpuHotSpotTemperaturePollingSeconds { get; set; } = DefaultPollingSeconds;

    /// <summary>Empty means automatic sensor selection.</summary>
    public string StorageTemperatureSensorId { get; set; } = string.Empty;

    /// <summary>Empty means automatic sensor selection.</summary>
    public string CpuFanSensorId { get; set; } = string.Empty;

    /// <summary>
    /// Poll more slowly once the machine has had no keyboard or mouse input for
    /// <see cref="IdleAfterSeconds"/>. Uses the same unified/individual mode as active polling, so
    /// choosing individual intervals gives an idle interval per metric too.
    /// </summary>
    public bool UseIdlePolling { get; set; } = true;

    public int IdleAfterSeconds { get; set; } = DefaultIdleAfterSeconds;

    public int IdleUnifiedPollingSeconds { get; set; } = DefaultIdlePollingSeconds;

    public int IdleCpuTemperaturePollingSeconds { get; set; } = DefaultIdlePollingSeconds;
    public int IdleCpuUsagePollingSeconds { get; set; } = DefaultIdlePollingSeconds;
    public int IdleMemoryUsagePollingSeconds { get; set; } = DefaultIdlePollingSeconds;

    public int IdleGpuTemperaturePollingSeconds { get; set; } = DefaultIdlePollingSeconds;
    public int IdleGpuComputeUsagePollingSeconds { get; set; } = DefaultIdlePollingSeconds;
    public int IdleGpuMemoryUsagePollingSeconds { get; set; } = DefaultIdlePollingSeconds;
    public int IdleGpuMemoryTemperaturePollingSeconds { get; set; } = DefaultIdlePollingSeconds;
    public int IdleGpuFanPollingSeconds { get; set; } = DefaultIdlePollingSeconds;
    public int IdleMotherboardTemperaturePollingSeconds { get; set; } = DefaultIdlePollingSeconds;
    public int IdleMemoryTemperaturePollingSeconds { get; set; } = DefaultIdlePollingSeconds;
    public int IdleCpuFanPollingSeconds { get; set; } = DefaultIdlePollingSeconds;
    public int IdleStorageTemperaturePollingSeconds { get; set; } = DefaultIdlePollingSeconds;
    public int IdlePowerPollingSeconds { get; set; } = DefaultIdlePollingSeconds;
    public int IdleGpuHotSpotTemperaturePollingSeconds { get; set; } = DefaultIdlePollingSeconds;

    /// <summary>"Retro" or "Default", matching the AI Usage Monitor's widget appearances.</summary>
    public string WidgetAppearance { get; set; } = RetroAppearance;

    /// <summary>One of <see cref="FontChoices"/>.</summary>
    public string WidgetFont { get; set; } = "Pixelify Sans";

    /// <summary>One of <see cref="TextWeightChoices"/>.</summary>
    public string WidgetTextWeight { get; set; } = "Bold";

    /// <summary>Widget opacity, 0.6-1.0, chosen from the widget's right-click menu.</summary>
    public double WidgetOpacity { get; set; } = 1.0;

    /// <summary>
    /// Whether the main widget is open. Kept with the rest of the widget state so the monitoring
    /// runtime can start without constructing a Window when the user previously hid it.
    /// </summary>
    public bool ShowWidget { get; set; } = true;

    /// <summary>Text size multiplier, 0.85-1.5, chosen from the widget's right-click menu.</summary>
    public double WidgetTextScale { get; set; } = 1.0;

    public bool WidgetAlwaysOnTop { get; set; } = true;

    /// <summary>When locked, the widget cannot be dragged or resized by mistake.</summary>
    public bool WidgetLocked { get; set; }

    // Widget placement, so a borderless, draggable, resizable widget comes back where it was
    // left. Null means "not positioned yet" and lets the window pick its own default corner.
    //
    // Nullable rather than NaN on purpose: System.Text.Json refuses to write NaN at all, so a NaN
    // sentinel made the very first save of a fresh settings object throw. Null round-trips as JSON
    // null and expresses the same thing.
    public double? WidgetLeft { get; set; }
    public double? WidgetTop { get; set; }
    public double WidgetWidth { get; set; } = 200;
    public double WidgetHeight { get; set; } = 236;

    /// <summary>
    /// Five-stage colour thresholds per metric, keyed by the stable MetricType string. Missing
    /// entries are filled from <see cref="MetricStageSettings.Default"/>, so an older settings file
    /// picks up defaults for metrics it has never heard of.
    /// </summary>
    public Dictionary<string, MetricStageSettings> MetricStages { get; set; } = [];

    /// <summary>
    /// Which metrics the widget shows, and in what order. List order is display order.
    /// </summary>
    public List<MetricDisplaySettings> MetricDisplay { get; set; } = [];

    public string Stage1Color { get; set; } = "#2ECC71";
    public string Stage2Color { get; set; } = "#9ACD32";
    public string Stage3Color { get; set; } = "#FFD21E";
    public string Stage4Color { get; set; } = "#FF9800";
    public string Stage5Color { get; set; } = "#FF4D4F";
    public string UnavailableColor { get; set; } = "#59616B";

    /// <summary>
    /// The effective interval for one metric: the unified value when unified mode is on, otherwise
    /// that metric's own value. Always returns a clamped, valid number of seconds.
    /// </summary>
    public int ResolveIntervalSeconds(HardwareMetrics metric, bool idle = false)
    {
        if (idle && UseIdlePolling)
        {
            return UseUnifiedPollingInterval
                ? ClampIdle(IdleUnifiedPollingSeconds)
                : ClampIdle(IdleIntervalOf(metric));
        }

        if (UseUnifiedPollingInterval)
        {
            return Clamp(UnifiedPollingSeconds);
        }

        return Clamp(metric switch
        {
            HardwareMetrics.CpuTemperature => CpuTemperaturePollingSeconds,
            HardwareMetrics.CpuUsage => CpuUsagePollingSeconds,
            HardwareMetrics.MemoryUsage => MemoryUsagePollingSeconds,
            HardwareMetrics.GpuTemperature => GpuTemperaturePollingSeconds,
            HardwareMetrics.GpuComputeUsage => GpuComputeUsagePollingSeconds,
            HardwareMetrics.GpuMemoryUsage => GpuMemoryUsagePollingSeconds,
            HardwareMetrics.GpuMemoryTemperature => GpuMemoryTemperaturePollingSeconds,
            HardwareMetrics.GpuFan => GpuFanPollingSeconds,
            HardwareMetrics.MotherboardTemperature => MotherboardTemperaturePollingSeconds,
            HardwareMetrics.MemoryTemperature => MemoryTemperaturePollingSeconds,
            HardwareMetrics.CpuFan => CpuFanPollingSeconds,
            HardwareMetrics.StorageTemperature => StorageTemperaturePollingSeconds,
            HardwareMetrics.Power => PowerPollingSeconds,
            HardwareMetrics.GpuHotSpotTemperature => GpuHotSpotTemperaturePollingSeconds,
            HardwareMetrics.CpuPower => PowerPollingSeconds,
            HardwareMetrics.GpuPower => PowerPollingSeconds,
            _ => DefaultPollingSeconds,
        });
    }

    /// <summary>The configured idle interval for one metric, before clamping.</summary>
    public int IdleIntervalOf(HardwareMetrics metric) => metric switch
    {
        HardwareMetrics.CpuTemperature => IdleCpuTemperaturePollingSeconds,
        HardwareMetrics.CpuUsage => IdleCpuUsagePollingSeconds,
        HardwareMetrics.MemoryUsage => IdleMemoryUsagePollingSeconds,
        HardwareMetrics.GpuTemperature => IdleGpuTemperaturePollingSeconds,
        HardwareMetrics.GpuComputeUsage => IdleGpuComputeUsagePollingSeconds,
        HardwareMetrics.GpuMemoryUsage => IdleGpuMemoryUsagePollingSeconds,
        HardwareMetrics.GpuMemoryTemperature => IdleGpuMemoryTemperaturePollingSeconds,
        HardwareMetrics.GpuFan => IdleGpuFanPollingSeconds,
        HardwareMetrics.MotherboardTemperature => IdleMotherboardTemperaturePollingSeconds,
        HardwareMetrics.MemoryTemperature => IdleMemoryTemperaturePollingSeconds,
        HardwareMetrics.CpuFan => IdleCpuFanPollingSeconds,
        HardwareMetrics.StorageTemperature => IdleStorageTemperaturePollingSeconds,
        HardwareMetrics.Power => IdlePowerPollingSeconds,
        HardwareMetrics.GpuHotSpotTemperature => IdleGpuHotSpotTemperaturePollingSeconds,
        HardwareMetrics.CpuPower => IdlePowerPollingSeconds,
        HardwareMetrics.GpuPower => IdlePowerPollingSeconds,
        _ => DefaultIdlePollingSeconds,
    };

    /// <summary>
    /// The metrics the widget should show and collect, in display order. An empty list is valid:
    /// unticking Show deliberately stops polling and history collection for that metric.
    /// </summary>
    public IReadOnlyList<HardwareMetrics> ResolveDisplayOrder()
    {
        var byKey = HardwareMetricsExtensions.Individual
            .ToDictionary(MetricTypes.DisplayKeyOf, metric => metric);

        var ordered = new List<HardwareMetrics>(byKey.Count);
        foreach (var entry in MetricDisplay)
        {
            if (entry.Visible && byKey.TryGetValue(entry.MetricType, out var metric))
            {
                ordered.Add(metric);
            }
        }

        return ordered;
    }

    /// <summary>The physical metric groups that need polling for the currently visible rows.</summary>
    public HardwareMetrics ResolveCollectedMetrics()
    {
        var known = HardwareMetricsExtensions.Individual.ToDictionary(MetricTypes.DisplayKeyOf, metric => metric);
        var collected = HardwareMetrics.None;
        foreach (var entry in MetricDisplay.Where(entry => entry.Visible))
        {
            if (known.TryGetValue(entry.MetricType, out var metric))
            {
                if ((metric == HardwareMetrics.Power && !ConsolidatePower)
                    || ((metric == HardwareMetrics.CpuPower || metric == HardwareMetrics.GpuPower) && ConsolidatePower))
                {
                    continue;
                }

                collected |= metric;
            }
            else if (SensorMetricKeys.IsDrive(entry.MetricType))
            {
                collected |= HardwareMetrics.StorageTemperature;
            }
            else if (SensorMetricKeys.IsCpuFan(entry.MetricType))
            {
                collected |= HardwareMetrics.CpuFan;
            }
        }

        return collected;
    }

    /// <summary>Whether one metric is shown in the widget. Unknown metrics default to visible.</summary>
    public bool IsVisible(HardwareMetrics metric)
    {
        var key = MetricTypes.DisplayKeyOf(metric);
        var entry = MetricDisplay.FirstOrDefault(item => item.MetricType == key);
        return entry?.Visible ?? true;
    }

    /// <summary>Whether one metric's history sparkline is shown. Unknown metrics default to visible.</summary>
    public bool IsGraphVisible(HardwareMetrics metric)
    {
        var key = MetricTypes.DisplayKeyOf(metric);
        var entry = MetricDisplay.FirstOrDefault(item => item.MetricType == key);
        return entry?.ShowGraph ?? true;
    }

    public bool IsGraphVisible(string metricType) =>
        MetricDisplay.FirstOrDefault(item => item.MetricType == metricType)?.ShowGraph ?? true;

    public string ResolveDisplayName(HardwareMetrics metric)
    {
        var key = MetricTypes.DisplayKeyOf(metric);
        var configured = MetricDisplay.FirstOrDefault(item => item.MetricType == key)?.DisplayName?.Trim();
        return string.IsNullOrWhiteSpace(configured)
            ? MetricTypes.DefaultDisplayNameOf(metric)
            : configured;
    }

    public string ResolveDisplayName(string metricType, string fallback)
    {
        var configured = MetricDisplay.FirstOrDefault(item => item.MetricType == metricType)?.DisplayName?.Trim();
        return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    }

    /// <summary>Stage thresholds for a displayed metric, falling back to that metric's defaults.</summary>
    public MetricStageSettings ResolveStages(HardwareMetrics metric)
    {
        var key = MetricTypes.DisplayKeyOf(metric);
        return MetricStages.TryGetValue(key, out var stages) && stages.IsValid()
            ? stages
            : MetricStageSettings.Default(key);
    }

    public MetricStageSettings ResolveStages(string metricType, string defaultMetricType) =>
        MetricStages.TryGetValue(metricType, out var stages) && stages.IsValid()
            ? stages
            : MetricStageSettings.Default(defaultMetricType);

    public static bool IsValidInterval(int seconds) =>
        seconds is >= MinimumPollingSeconds and <= MaximumPollingSeconds;

    public static bool IsValidIdleInterval(int seconds) =>
        seconds is >= MinimumIdlePollingSeconds and <= MaximumIdlePollingSeconds;

    public static bool IsValidIdleAfter(int seconds) =>
        seconds is >= MinimumIdleAfterSeconds and <= MaximumIdleAfterSeconds;

    /// <summary>
    /// True for "*" (case-insensitive, ignoring surrounding whitespace -- the Grid syntax it
    /// mirrors is not case sensitive either) or a finite number of pixels within range.
    /// </summary>
    public static bool IsValidColumnWidth(string? text, double minimum, double maximum)
    {
        var trimmed = text?.Trim();
        if (string.Equals(trimmed, StarColumnWidth, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels)
            && pixels >= minimum
            && pixels <= maximum;
    }

    /// <summary>
    /// Whether two settings objects produce the same polling schedule. Settings are saved for
    /// reasons that have nothing to do with polling -- dragging or resizing the widget persists its
    /// placement -- and restarting the scheduler for those would reset every metric's due time and
    /// cause a burst of reads on every drag. Only a genuine schedule change should restart it.
    /// </summary>
    public bool HasSamePollingSchedule(AppSettings other) =>
        UseUnifiedPollingInterval == other.UseUnifiedPollingInterval
        && ConsolidatePower == other.ConsolidatePower
        && UnifiedPollingSeconds == other.UnifiedPollingSeconds
        && CpuTemperaturePollingSeconds == other.CpuTemperaturePollingSeconds
        && CpuUsagePollingSeconds == other.CpuUsagePollingSeconds
        && MemoryUsagePollingSeconds == other.MemoryUsagePollingSeconds
        && GpuTemperaturePollingSeconds == other.GpuTemperaturePollingSeconds
        && GpuComputeUsagePollingSeconds == other.GpuComputeUsagePollingSeconds
        && GpuMemoryUsagePollingSeconds == other.GpuMemoryUsagePollingSeconds
        && GpuMemoryTemperaturePollingSeconds == other.GpuMemoryTemperaturePollingSeconds
        && GpuFanPollingSeconds == other.GpuFanPollingSeconds
        && MotherboardTemperaturePollingSeconds == other.MotherboardTemperaturePollingSeconds
        && MemoryTemperaturePollingSeconds == other.MemoryTemperaturePollingSeconds
        && CpuFanPollingSeconds == other.CpuFanPollingSeconds
        && StorageTemperaturePollingSeconds == other.StorageTemperaturePollingSeconds
        && PowerPollingSeconds == other.PowerPollingSeconds
        && GpuHotSpotTemperaturePollingSeconds == other.GpuHotSpotTemperaturePollingSeconds
        && UseIdlePolling == other.UseIdlePolling
        && IdleAfterSeconds == other.IdleAfterSeconds
        && IdleUnifiedPollingSeconds == other.IdleUnifiedPollingSeconds
        && IdleCpuTemperaturePollingSeconds == other.IdleCpuTemperaturePollingSeconds
        && IdleCpuUsagePollingSeconds == other.IdleCpuUsagePollingSeconds
        && IdleMemoryUsagePollingSeconds == other.IdleMemoryUsagePollingSeconds
        && IdleGpuTemperaturePollingSeconds == other.IdleGpuTemperaturePollingSeconds
        && IdleGpuComputeUsagePollingSeconds == other.IdleGpuComputeUsagePollingSeconds
        && IdleGpuMemoryUsagePollingSeconds == other.IdleGpuMemoryUsagePollingSeconds
        && IdleGpuMemoryTemperaturePollingSeconds == other.IdleGpuMemoryTemperaturePollingSeconds
        && IdleGpuFanPollingSeconds == other.IdleGpuFanPollingSeconds
        && IdleMotherboardTemperaturePollingSeconds == other.IdleMotherboardTemperaturePollingSeconds
        && IdleMemoryTemperaturePollingSeconds == other.IdleMemoryTemperaturePollingSeconds
        && IdleCpuFanPollingSeconds == other.IdleCpuFanPollingSeconds
        && IdleStorageTemperaturePollingSeconds == other.IdleStorageTemperaturePollingSeconds
        && IdlePowerPollingSeconds == other.IdlePowerPollingSeconds
        && IdleGpuHotSpotTemperaturePollingSeconds == other.IdleGpuHotSpotTemperaturePollingSeconds
        && HardwareMetricsExtensions.Individual.All(metric => IsVisible(metric) == other.IsVisible(metric))
        && MetricDisplay
            .Where(entry => SensorMetricKeys.IsKnown(entry.MetricType))
            .OrderBy(entry => entry.MetricType, StringComparer.Ordinal)
            .Select(entry => (entry.MetricType, entry.Visible))
            .SequenceEqual(other.MetricDisplay
                .Where(entry => SensorMetricKeys.IsKnown(entry.MetricType))
                .OrderBy(entry => entry.MetricType, StringComparer.Ordinal)
                .Select(entry => (entry.MetricType, entry.Visible)));

    public bool HasSameSensorSelection(AppSettings other) =>
        string.Equals(StorageTemperatureSensorId, other.StorageTemperatureSensorId, StringComparison.Ordinal)
        && string.Equals(CpuFanSensorId, other.CpuFanSensorId, StringComparison.Ordinal);

    public static string NormalizeAppearance(string? appearance) =>
        appearance == DefaultAppearance ? DefaultAppearance : RetroAppearance;

    public static string NormalizeFont(string? font) =>
        font is not null && FontChoices.Contains(font) ? font : SystemFont;

    public static string NormalizeTextWeight(string? weight) =>
        weight is not null && TextWeightChoices.Contains(weight) ? weight : "Normal";

    /// <summary>
    /// Repairs out-of-range values that reached us from a hand-edited or older settings file, so a
    /// bad file degrades to defaults instead of producing a runaway polling loop or unreadable
    /// colours.
    /// </summary>
    public AppSettings Normalized()
    {
        UnifiedPollingSeconds = Clamp(UnifiedPollingSeconds);
        CpuTemperaturePollingSeconds = Clamp(CpuTemperaturePollingSeconds);
        CpuUsagePollingSeconds = Clamp(CpuUsagePollingSeconds);
        MemoryUsagePollingSeconds = Clamp(MemoryUsagePollingSeconds);
        GpuTemperaturePollingSeconds = Clamp(GpuTemperaturePollingSeconds);
        GpuComputeUsagePollingSeconds = Clamp(GpuComputeUsagePollingSeconds);
        GpuMemoryUsagePollingSeconds = Clamp(GpuMemoryUsagePollingSeconds);
        GpuMemoryTemperaturePollingSeconds = Clamp(GpuMemoryTemperaturePollingSeconds);
        GpuFanPollingSeconds = Clamp(GpuFanPollingSeconds);
        MotherboardTemperaturePollingSeconds = Clamp(MotherboardTemperaturePollingSeconds);
        MemoryTemperaturePollingSeconds = Clamp(MemoryTemperaturePollingSeconds);
        CpuFanPollingSeconds = Clamp(CpuFanPollingSeconds);
        StorageTemperaturePollingSeconds = Clamp(StorageTemperaturePollingSeconds);
        PowerPollingSeconds = Clamp(PowerPollingSeconds);
        GpuHotSpotTemperaturePollingSeconds = Clamp(GpuHotSpotTemperaturePollingSeconds);
        StorageTemperatureSensorId ??= string.Empty;
        CpuFanSensorId ??= string.Empty;

        IdleAfterSeconds = Math.Clamp(IdleAfterSeconds, MinimumIdleAfterSeconds, MaximumIdleAfterSeconds);
        IdleUnifiedPollingSeconds = ClampIdle(IdleUnifiedPollingSeconds);
        IdleCpuTemperaturePollingSeconds = ClampIdle(IdleCpuTemperaturePollingSeconds);
        IdleCpuUsagePollingSeconds = ClampIdle(IdleCpuUsagePollingSeconds);
        IdleMemoryUsagePollingSeconds = ClampIdle(IdleMemoryUsagePollingSeconds);
        IdleGpuTemperaturePollingSeconds = ClampIdle(IdleGpuTemperaturePollingSeconds);
        IdleGpuComputeUsagePollingSeconds = ClampIdle(IdleGpuComputeUsagePollingSeconds);
        IdleGpuMemoryUsagePollingSeconds = ClampIdle(IdleGpuMemoryUsagePollingSeconds);
        IdleGpuMemoryTemperaturePollingSeconds = ClampIdle(IdleGpuMemoryTemperaturePollingSeconds);
        IdleGpuFanPollingSeconds = ClampIdle(IdleGpuFanPollingSeconds);
        IdleMotherboardTemperaturePollingSeconds = ClampIdle(IdleMotherboardTemperaturePollingSeconds);
        IdleMemoryTemperaturePollingSeconds = ClampIdle(IdleMemoryTemperaturePollingSeconds);
        IdleCpuFanPollingSeconds = ClampIdle(IdleCpuFanPollingSeconds);
        IdleStorageTemperaturePollingSeconds = ClampIdle(IdleStorageTemperaturePollingSeconds);
        IdlePowerPollingSeconds = ClampIdle(IdlePowerPollingSeconds);
        IdleGpuHotSpotTemperaturePollingSeconds = ClampIdle(IdleGpuHotSpotTemperaturePollingSeconds);

        WidgetAppearance = NormalizeAppearance(WidgetAppearance);
        WidgetFont = NormalizeFont(WidgetFont);
        WidgetTextWeight = NormalizeTextWeight(WidgetTextWeight);
        WidgetOpacity = double.IsFinite(WidgetOpacity) ? Math.Clamp(WidgetOpacity, 0.6, 1.0) : 1.0;
        WidgetTextScale = double.IsFinite(WidgetTextScale) ? Math.Clamp(WidgetTextScale, 0.85, 1.5) : 1.0;
        WidgetWidth = double.IsFinite(WidgetWidth) ? Math.Clamp(WidgetWidth, 150, 2400) : 200;
        WidgetHeight = double.IsFinite(WidgetHeight) ? Math.Clamp(WidgetHeight, 90, 1600) : 236;
        WidgetLabelColumnWidth = IsValidColumnWidth(WidgetLabelColumnWidth, MinimumLabelColumnWidth, MaximumLabelColumnWidth)
            ? WidgetLabelColumnWidth.Trim()
            : DefaultLabelColumnWidth;
        WidgetGraphColumnWidth = IsValidColumnWidth(WidgetGraphColumnWidth, MinimumGraphColumnWidth, MaximumGraphColumnWidth)
            ? WidgetGraphColumnWidth.Trim()
            : DefaultGraphColumnWidth;
        WidgetValueColumnWidth = IsValidColumnWidth(WidgetValueColumnWidth, MinimumValueColumnWidth, MaximumValueColumnWidth)
            ? WidgetValueColumnWidth.Trim()
            : DefaultValueColumnWidth;
        WidgetValueColumnWidthWithRam = IsValidColumnWidth(WidgetValueColumnWidthWithRam, MinimumValueColumnWidth, MaximumValueColumnWidth)
            ? WidgetValueColumnWidthWithRam.Trim()
            : DefaultValueColumnWidthWithRam;
        WidgetMinimumColumnWidth = double.IsFinite(WidgetMinimumColumnWidth)
            ? Math.Clamp(WidgetMinimumColumnWidth, MinimumMinimumColumnWidth, MaximumMinimumColumnWidth)
            : DefaultMinimumColumnWidth;
        WidgetMinimumColumnWidthWithRam = double.IsFinite(WidgetMinimumColumnWidthWithRam)
            ? Math.Clamp(WidgetMinimumColumnWidthWithRam, MinimumMinimumColumnWidth, MaximumMinimumColumnWidth)
            : DefaultMinimumColumnWidthWithRam;
        WidgetGraphHeightMinimum = double.IsFinite(WidgetGraphHeightMinimum)
            ? Math.Clamp(WidgetGraphHeightMinimum, AbsoluteMinimumGraphHeight, AbsoluteMaximumGraphHeight)
            : DefaultGraphHeightMinimum;
        WidgetGraphHeightMaximum = double.IsFinite(WidgetGraphHeightMaximum)
            ? Math.Clamp(WidgetGraphHeightMaximum, WidgetGraphHeightMinimum, AbsoluteMaximumGraphHeight)
            : DefaultGraphHeightMaximum;

        MetricStages ??= [];
        MetricDisplay ??= [];

        var knownKeys = HardwareMetricsExtensions.Individual.Select(MetricTypes.DisplayKeyOf).ToList();

        foreach (var key in knownKeys)
        {
            if (!MetricStages.TryGetValue(key, out var stages) || !stages.IsValid())
            {
                MetricStages[key] = MetricStageSettings.Default(key);
            }
        }

        // Drop keys this build no longer knows, then append any metric the file has never seen, so
        // an older settings file picks up new metrics at the end instead of losing them.
        MetricDisplay = MetricDisplay
            .Where(entry => entry is not null)
            .Where(entry => knownKeys.Contains(entry.MetricType) || SensorMetricKeys.IsKnown(entry.MetricType))
            .GroupBy(entry => entry.MetricType)
            .Select(group => group.First())
            .ToList();

        foreach (var key in knownKeys.Where(key => MetricDisplay.All(entry => entry.MetricType != key)))
        {
            MetricDisplay.Add(new MetricDisplaySettings
            {
                MetricType = key,
                // Per-drive and per-CPU-fan rows replace the two former aggregate rows.
                Visible = key is not MetricTypes.CpuFanRpm and not MetricTypes.StorageTemperature,
            });
        }

        foreach (var entry in MetricDisplay)
        {
            entry.DisplayName = entry.DisplayName?.Trim() ?? string.Empty;
        }

        return this;
    }

    public AppSettings Clone() => new()
    {
        UseUnifiedPollingInterval = UseUnifiedPollingInterval,
        UnifiedPollingSeconds = UnifiedPollingSeconds,
        ShowRamUsedAndTotal = ShowRamUsedAndTotal,
        WidgetLabelColumnWidth = WidgetLabelColumnWidth,
        WidgetGraphColumnWidth = WidgetGraphColumnWidth,
        WidgetValueColumnWidth = WidgetValueColumnWidth,
        WidgetValueColumnWidthWithRam = WidgetValueColumnWidthWithRam,
        WidgetMinimumColumnWidth = WidgetMinimumColumnWidth,
        WidgetMinimumColumnWidthWithRam = WidgetMinimumColumnWidthWithRam,
        WidgetGraphHeightMinimum = WidgetGraphHeightMinimum,
        WidgetGraphHeightMaximum = WidgetGraphHeightMaximum,
        CpuTemperaturePollingSeconds = CpuTemperaturePollingSeconds,
        CpuUsagePollingSeconds = CpuUsagePollingSeconds,
        MemoryUsagePollingSeconds = MemoryUsagePollingSeconds,
        GpuTemperaturePollingSeconds = GpuTemperaturePollingSeconds,
        GpuComputeUsagePollingSeconds = GpuComputeUsagePollingSeconds,
        GpuMemoryUsagePollingSeconds = GpuMemoryUsagePollingSeconds,
        GpuMemoryTemperaturePollingSeconds = GpuMemoryTemperaturePollingSeconds,
        GpuFanPollingSeconds = GpuFanPollingSeconds,
        MotherboardTemperaturePollingSeconds = MotherboardTemperaturePollingSeconds,
        MemoryTemperaturePollingSeconds = MemoryTemperaturePollingSeconds,
        CpuFanPollingSeconds = CpuFanPollingSeconds,
        StorageTemperaturePollingSeconds = StorageTemperaturePollingSeconds,
        PowerPollingSeconds = PowerPollingSeconds,
        ConsolidatePower = ConsolidatePower,
        GpuHotSpotTemperaturePollingSeconds = GpuHotSpotTemperaturePollingSeconds,
        StorageTemperatureSensorId = StorageTemperatureSensorId,
        CpuFanSensorId = CpuFanSensorId,
        UseIdlePolling = UseIdlePolling,
        IdleAfterSeconds = IdleAfterSeconds,
        IdleUnifiedPollingSeconds = IdleUnifiedPollingSeconds,
        IdleCpuTemperaturePollingSeconds = IdleCpuTemperaturePollingSeconds,
        IdleCpuUsagePollingSeconds = IdleCpuUsagePollingSeconds,
        IdleMemoryUsagePollingSeconds = IdleMemoryUsagePollingSeconds,
        IdleGpuTemperaturePollingSeconds = IdleGpuTemperaturePollingSeconds,
        IdleGpuComputeUsagePollingSeconds = IdleGpuComputeUsagePollingSeconds,
        IdleGpuMemoryUsagePollingSeconds = IdleGpuMemoryUsagePollingSeconds,
        IdleGpuMemoryTemperaturePollingSeconds = IdleGpuMemoryTemperaturePollingSeconds,
        IdleGpuFanPollingSeconds = IdleGpuFanPollingSeconds,
        IdleMotherboardTemperaturePollingSeconds = IdleMotherboardTemperaturePollingSeconds,
        IdleMemoryTemperaturePollingSeconds = IdleMemoryTemperaturePollingSeconds,
        IdleCpuFanPollingSeconds = IdleCpuFanPollingSeconds,
        IdleStorageTemperaturePollingSeconds = IdleStorageTemperaturePollingSeconds,
        IdlePowerPollingSeconds = IdlePowerPollingSeconds,
        IdleGpuHotSpotTemperaturePollingSeconds = IdleGpuHotSpotTemperaturePollingSeconds,
        WidgetAppearance = WidgetAppearance,
        WidgetFont = WidgetFont,
        WidgetTextWeight = WidgetTextWeight,
        WidgetOpacity = WidgetOpacity,
        ShowWidget = ShowWidget,
        WidgetTextScale = WidgetTextScale,
        WidgetAlwaysOnTop = WidgetAlwaysOnTop,
        WidgetLocked = WidgetLocked,
        WidgetLeft = WidgetLeft,
        WidgetTop = WidgetTop,
        WidgetWidth = WidgetWidth,
        WidgetHeight = WidgetHeight,
        MetricStages = MetricStages.ToDictionary(entry => entry.Key, entry => entry.Value.Clone()),
        MetricDisplay = MetricDisplay.Select(entry => entry.Clone()).ToList(),
        Stage1Color = Stage1Color,
        Stage2Color = Stage2Color,
        Stage3Color = Stage3Color,
        Stage4Color = Stage4Color,
        Stage5Color = Stage5Color,
        UnavailableColor = UnavailableColor,
    };

    private static int Clamp(int seconds) =>
        Math.Clamp(seconds, MinimumPollingSeconds, MaximumPollingSeconds);

    private static int ClampIdle(int seconds) =>
        Math.Clamp(seconds, MinimumIdlePollingSeconds, MaximumIdlePollingSeconds);
}
