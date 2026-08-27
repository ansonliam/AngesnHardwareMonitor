using HardwareWidget.Models;

namespace HardwareWidget.Settings;

/// <summary>
/// Persisted user settings, serialised as JSON under %LOCALAPPDATA%\HardwareWidget\settings.json.
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

    public bool UseUnifiedPollingInterval { get; set; } = true;

    public int UnifiedPollingSeconds { get; set; } = DefaultPollingSeconds;

    /// <summary>Presentation only. Never affects what is collected or stored in history.</summary>
    public bool ShowRamUsedAndTotal { get; set; }

    public int CpuTemperaturePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int CpuUsagePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int MemoryUsagePollingSeconds { get; set; } = DefaultPollingSeconds;

    public int GpuTemperaturePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int GpuComputeUsagePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int GpuMemoryUsagePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int GpuMemoryTemperaturePollingSeconds { get; set; } = DefaultPollingSeconds;
    public int GpuFanPollingSeconds { get; set; } = DefaultPollingSeconds;

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

    /// <summary>History collection is on by default.</summary>
    public bool CollectHistory { get; set; } = true;

    /// <summary>"Retro" or "Default", matching the AI Usage Monitor's widget appearances.</summary>
    public string WidgetAppearance { get; set; } = RetroAppearance;

    /// <summary>One of <see cref="FontChoices"/>.</summary>
    public string WidgetFont { get; set; } = "Pixelify Sans";

    /// <summary>One of <see cref="TextWeightChoices"/>.</summary>
    public string WidgetTextWeight { get; set; } = "Bold";

    /// <summary>Widget opacity, 0.6-1.0, chosen from the widget's right-click menu.</summary>
    public double WidgetOpacity { get; set; } = 1.0;

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
        _ => DefaultIdlePollingSeconds,
    };

    /// <summary>
    /// The metrics the widget should show, in display order. Falls back to the full default order
    /// if every metric has somehow been hidden, because an empty widget is not a useful state to
    /// leave someone stuck in.
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

        return ordered.Count > 0 ? ordered : HardwareMetricsExtensions.Individual;
    }

    /// <summary>Whether one metric is shown in the widget. Unknown metrics default to visible.</summary>
    public bool IsVisible(HardwareMetrics metric)
    {
        var key = MetricTypes.DisplayKeyOf(metric);
        var entry = MetricDisplay.FirstOrDefault(item => item.MetricType == key);
        return entry?.Visible ?? true;
    }

    /// <summary>Stage thresholds for a displayed metric, falling back to that metric's defaults.</summary>
    public MetricStageSettings ResolveStages(HardwareMetrics metric)
    {
        var key = MetricTypes.DisplayKeyOf(metric);
        return MetricStages.TryGetValue(key, out var stages) && stages.IsValid()
            ? stages
            : MetricStageSettings.Default(key);
    }

    public static bool IsValidInterval(int seconds) =>
        seconds is >= MinimumPollingSeconds and <= MaximumPollingSeconds;

    public static bool IsValidIdleInterval(int seconds) =>
        seconds is >= MinimumIdlePollingSeconds and <= MaximumIdlePollingSeconds;

    public static bool IsValidIdleAfter(int seconds) =>
        seconds is >= MinimumIdleAfterSeconds and <= MaximumIdleAfterSeconds;

    /// <summary>
    /// Whether two settings objects produce the same polling schedule. Settings are saved for
    /// reasons that have nothing to do with polling -- dragging or resizing the widget persists its
    /// placement -- and restarting the scheduler for those would reset every metric's due time and
    /// cause a burst of reads on every drag. Only a genuine schedule change should restart it.
    /// </summary>
    public bool HasSamePollingSchedule(AppSettings other) =>
        UseUnifiedPollingInterval == other.UseUnifiedPollingInterval
        && UnifiedPollingSeconds == other.UnifiedPollingSeconds
        && CpuTemperaturePollingSeconds == other.CpuTemperaturePollingSeconds
        && CpuUsagePollingSeconds == other.CpuUsagePollingSeconds
        && MemoryUsagePollingSeconds == other.MemoryUsagePollingSeconds
        && GpuTemperaturePollingSeconds == other.GpuTemperaturePollingSeconds
        && GpuComputeUsagePollingSeconds == other.GpuComputeUsagePollingSeconds
        && GpuMemoryUsagePollingSeconds == other.GpuMemoryUsagePollingSeconds
        && GpuMemoryTemperaturePollingSeconds == other.GpuMemoryTemperaturePollingSeconds
        && GpuFanPollingSeconds == other.GpuFanPollingSeconds
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
        && IdleGpuFanPollingSeconds == other.IdleGpuFanPollingSeconds;

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

        WidgetAppearance = NormalizeAppearance(WidgetAppearance);
        WidgetFont = NormalizeFont(WidgetFont);
        WidgetTextWeight = NormalizeTextWeight(WidgetTextWeight);
        WidgetOpacity = double.IsFinite(WidgetOpacity) ? Math.Clamp(WidgetOpacity, 0.6, 1.0) : 1.0;
        WidgetTextScale = double.IsFinite(WidgetTextScale) ? Math.Clamp(WidgetTextScale, 0.85, 1.5) : 1.0;
        WidgetWidth = double.IsFinite(WidgetWidth) ? Math.Clamp(WidgetWidth, 150, 2400) : 200;
        WidgetHeight = double.IsFinite(WidgetHeight) ? Math.Clamp(WidgetHeight, 90, 1600) : 236;

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
            .Where(entry => knownKeys.Contains(entry.MetricType))
            .GroupBy(entry => entry.MetricType)
            .Select(group => group.First())
            .ToList();

        foreach (var key in knownKeys.Where(key => MetricDisplay.All(entry => entry.MetricType != key)))
        {
            MetricDisplay.Add(new MetricDisplaySettings { MetricType = key, Visible = true });
        }

        return this;
    }

    public AppSettings Clone() => new()
    {
        UseUnifiedPollingInterval = UseUnifiedPollingInterval,
        UnifiedPollingSeconds = UnifiedPollingSeconds,
        ShowRamUsedAndTotal = ShowRamUsedAndTotal,
        CpuTemperaturePollingSeconds = CpuTemperaturePollingSeconds,
        CpuUsagePollingSeconds = CpuUsagePollingSeconds,
        MemoryUsagePollingSeconds = MemoryUsagePollingSeconds,
        GpuTemperaturePollingSeconds = GpuTemperaturePollingSeconds,
        GpuComputeUsagePollingSeconds = GpuComputeUsagePollingSeconds,
        GpuMemoryUsagePollingSeconds = GpuMemoryUsagePollingSeconds,
        GpuMemoryTemperaturePollingSeconds = GpuMemoryTemperaturePollingSeconds,
        GpuFanPollingSeconds = GpuFanPollingSeconds,
        CollectHistory = CollectHistory,
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
        WidgetAppearance = WidgetAppearance,
        WidgetFont = WidgetFont,
        WidgetTextWeight = WidgetTextWeight,
        WidgetOpacity = WidgetOpacity,
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
