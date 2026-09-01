using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AngesnHardwareWidget.Models;
using AngesnHardwareWidget.Services;
using AngesnHardwareWidget.Settings;

namespace AngesnHardwareWidget.ViewModels;

/// <summary>
/// One entry in any of the interval ComboBoxes. Held as seconds, because seconds are what every
/// interval setting persists, but shown in whichever unit reads naturally: sub-minute values in
/// seconds, whole minutes in minutes.
/// </summary>
public sealed record PollingIntervalOption(int Seconds)
{
    public string Label => Seconds switch
    {
        1 => "1 second",
        < 60 => $"{Seconds} seconds",
        60 => "1 minute",

        // Anything that is not a whole number of minutes stays in seconds; a hand-edited 90 reads
        // better as "90 seconds" than "1.5 minutes".
        _ when Seconds % 60 == 0 => $"{Seconds / 60} minutes",
        _ => $"{Seconds} seconds",
    };

    /// <summary>
    /// The dialog's ComboBox template renders the selected item through a plain ContentPresenter,
    /// which formats via ToString() and does not honour DisplayMemberPath -- without this the box
    /// showed "PollingIntervalOption { Seconds = 30 }". Formatting here rather than in the template
    /// keeps every ComboBox that shows one of these correct by construction.
    /// </summary>
    public override string ToString() => Label;
}

/// <summary>
/// The settings dialog, split into two kinds of setting.
///
/// Appearance and colour stages apply live: they are cosmetic, the widget is visible next to the
/// dialog, and tuning a threshold is a "watch it change" job, so making the user press Save would
/// only get in the way. Monitoring settings -- history collection and the polling intervals -- are
/// held pending until Save, because committing them tears down and rebuilds the polling schedule
/// and should happen once the user has finished choosing, not on every dropdown flick.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private static readonly IReadOnlyList<int> OfferedSeconds = AppSettings.OfferedIntervalSeconds;

    // Idle cadences are allowed to be much longer than active ones, so they get their own lists.
    private static readonly int[] OfferedIdleSeconds = [10, 30, 60, 120, 300, 600, 900, 1800, 3600];
    private static readonly int[] OfferedIdleAfterSeconds = [60, 120, 300, 600, 900, 1800, 3600];

    private readonly SettingsService _settings;
    private readonly WindowsStartupService _startup = new();

    // Live section.
    private string _widgetAppearance;
    private string _widgetFont;
    private string _widgetTextWeight;
    private bool _showRamUsedAndTotal;
    private string _widgetLabelColumnWidth;
    private string _widgetGraphColumnWidth;
    private string _widgetValueColumnWidth;
    private string _widgetValueColumnWidthWithRam;
    private string _widgetLabelColumnWidthText;
    private string _widgetGraphColumnWidthText;
    private string _widgetValueColumnWidthText;
    private string _widgetValueColumnWidthWithRamText;
    private double _widgetGraphHeightMinimum;
    private double _widgetGraphHeightMaximum;
    private string _widgetGraphHeightMinimumText;
    private string _widgetGraphHeightMaximumText;
    private double _widgetMinimumColumnWidth;
    private double _widgetMinimumColumnWidthWithRam;
    private string _widgetMinimumColumnWidthText;
    private string _widgetMinimumColumnWidthWithRamText;
    private bool _startWithWindows;

    // Pending section, committed by Save.
    private bool _useUnifiedPollingInterval;
    private bool _consolidatePower;
    private bool _useIdlePolling;
    private PollingIntervalOption _idleAfter;
    private PollingIntervalOption _idleUnifiedInterval;
    private PollingIntervalOption _idleCpuTemperatureInterval;
    private PollingIntervalOption _idleCpuUsageInterval;
    private PollingIntervalOption _idleMemoryUsageInterval;
    private PollingIntervalOption _idleGpuTemperatureInterval;
    private PollingIntervalOption _idleGpuComputeUsageInterval;
    private PollingIntervalOption _idleGpuMemoryUsageInterval;
    private PollingIntervalOption _idleGpuMemoryTemperatureInterval;
    private PollingIntervalOption _idleGpuFanInterval;
    private PollingIntervalOption _idleMotherboardTemperatureInterval;
    private PollingIntervalOption _idleMemoryTemperatureInterval;
    private PollingIntervalOption _idleCpuFanInterval;
    private PollingIntervalOption _idleStorageTemperatureInterval;
    private PollingIntervalOption _idlePowerInterval;
    private PollingIntervalOption _idleGpuHotSpotTemperatureInterval;
    private PollingIntervalOption _unifiedInterval;
    private PollingIntervalOption _cpuTemperatureInterval;
    private PollingIntervalOption _cpuUsageInterval;
    private PollingIntervalOption _memoryUsageInterval;
    private PollingIntervalOption _gpuTemperatureInterval;
    private PollingIntervalOption _gpuComputeUsageInterval;
    private PollingIntervalOption _gpuMemoryUsageInterval;
    private PollingIntervalOption _gpuMemoryTemperatureInterval;
    private PollingIntervalOption _gpuFanInterval;
    private PollingIntervalOption _motherboardTemperatureInterval;
    private PollingIntervalOption _memoryTemperatureInterval;
    private PollingIntervalOption _cpuFanInterval;
    private PollingIntervalOption _storageTemperatureInterval;
    private PollingIntervalOption _powerInterval;
    private PollingIntervalOption _gpuHotSpotTemperatureInterval;

    private string? _validationMessage;
    private string? _savedMessage;
    private bool _hasUnsavedMonitoringChanges;

    public SettingsViewModel(SettingsService settings, HardwareSensorCatalog sensorCatalog, Action closeWindow)
    {
        _settings = settings;

        var current = settings.Current;

        // A hand-edited settings file can hold a valid interval that is not one of the offered
        // values; keep it selectable rather than silently rewriting the user's choice.
        var seconds = OfferedSeconds
            .Concat(HardwareMetricsExtensions.Individual.Select(metric => current.ResolveIntervalSeconds(metric)))
            .Append(current.UnifiedPollingSeconds)
            .Where(AppSettings.IsValidInterval)
            .Distinct()
            .OrderBy(value => value);

        IntervalOptions = new ObservableCollection<PollingIntervalOption>(
            seconds.Select(value => new PollingIntervalOption(value)));

        IdleIntervalOptions = new ObservableCollection<PollingIntervalOption>(
            OfferedIdleSeconds
                .Append(current.IdleUnifiedPollingSeconds)
                .Concat(HardwareMetricsExtensions.Individual.Select(current.IdleIntervalOf))
                .Where(AppSettings.IsValidIdleInterval)
                .Distinct()
                .OrderBy(value => value)
                .Select(value => new PollingIntervalOption(value)));

        IdleAfterOptions = new ObservableCollection<PollingIntervalOption>(
            OfferedIdleAfterSeconds
                .Append(current.IdleAfterSeconds)
                .Where(AppSettings.IsValidIdleAfter)
                .Distinct()
                .OrderBy(value => value)
                .Select(value => new PollingIntervalOption(value)));


        _widgetAppearance = current.WidgetAppearance;
        _widgetFont = current.WidgetFont;
        _widgetTextWeight = current.WidgetTextWeight;
        _showRamUsedAndTotal = current.ShowRamUsedAndTotal;
        _widgetLabelColumnWidth = current.WidgetLabelColumnWidth;
        _widgetGraphColumnWidth = current.WidgetGraphColumnWidth;
        _widgetValueColumnWidth = current.WidgetValueColumnWidth;
        _widgetValueColumnWidthWithRam = current.WidgetValueColumnWidthWithRam;
        _widgetLabelColumnWidthText = current.WidgetLabelColumnWidth;
        _widgetGraphColumnWidthText = current.WidgetGraphColumnWidth;
        _widgetValueColumnWidthText = current.WidgetValueColumnWidth;
        _widgetValueColumnWidthWithRamText = current.WidgetValueColumnWidthWithRam;
        _widgetGraphHeightMinimum = current.WidgetGraphHeightMinimum;
        _widgetGraphHeightMaximum = current.WidgetGraphHeightMaximum;
        _widgetGraphHeightMinimumText = FormatNumber(current.WidgetGraphHeightMinimum);
        _widgetGraphHeightMaximumText = FormatNumber(current.WidgetGraphHeightMaximum);
        _widgetMinimumColumnWidth = current.WidgetMinimumColumnWidth;
        _widgetMinimumColumnWidthWithRam = current.WidgetMinimumColumnWidthWithRam;
        _widgetMinimumColumnWidthText = FormatNumber(current.WidgetMinimumColumnWidth);
        _widgetMinimumColumnWidthWithRamText = FormatNumber(current.WidgetMinimumColumnWidthWithRam);

        // Read from the scheduled task rather than from settings.json: the task is the actual
        // state, so if it was removed outside the app the checkbox still tells the truth.
        _startWithWindows = _startup.IsEnabled();

        _useUnifiedPollingInterval = current.UseUnifiedPollingInterval;
        _consolidatePower = current.ConsolidatePower;
        _unifiedInterval = Option(current.UnifiedPollingSeconds);
        _cpuTemperatureInterval = Option(current.CpuTemperaturePollingSeconds);
        _cpuUsageInterval = Option(current.CpuUsagePollingSeconds);
        _memoryUsageInterval = Option(current.MemoryUsagePollingSeconds);
        _gpuTemperatureInterval = Option(current.GpuTemperaturePollingSeconds);
        _gpuComputeUsageInterval = Option(current.GpuComputeUsagePollingSeconds);
        _gpuMemoryUsageInterval = Option(current.GpuMemoryUsagePollingSeconds);
        _gpuMemoryTemperatureInterval = Option(current.GpuMemoryTemperaturePollingSeconds);
        _gpuFanInterval = Option(current.GpuFanPollingSeconds);
        _motherboardTemperatureInterval = Option(current.MotherboardTemperaturePollingSeconds);
        _memoryTemperatureInterval = Option(current.MemoryTemperaturePollingSeconds);
        _cpuFanInterval = Option(current.CpuFanPollingSeconds);
        _storageTemperatureInterval = Option(current.StorageTemperaturePollingSeconds);
        _powerInterval = Option(current.PowerPollingSeconds);
        _gpuHotSpotTemperatureInterval = Option(current.GpuHotSpotTemperaturePollingSeconds);

        _useIdlePolling = current.UseIdlePolling;
        _idleAfter = ResolveIdleAfter(current.IdleAfterSeconds);
        _idleUnifiedInterval = IdleOption(current.IdleUnifiedPollingSeconds);
        _idleCpuTemperatureInterval = IdleOption(current.IdleCpuTemperaturePollingSeconds);
        _idleCpuUsageInterval = IdleOption(current.IdleCpuUsagePollingSeconds);
        _idleMemoryUsageInterval = IdleOption(current.IdleMemoryUsagePollingSeconds);
        _idleGpuTemperatureInterval = IdleOption(current.IdleGpuTemperaturePollingSeconds);
        _idleGpuComputeUsageInterval = IdleOption(current.IdleGpuComputeUsagePollingSeconds);
        _idleGpuMemoryUsageInterval = IdleOption(current.IdleGpuMemoryUsagePollingSeconds);
        _idleGpuMemoryTemperatureInterval = IdleOption(current.IdleGpuMemoryTemperaturePollingSeconds);
        _idleGpuFanInterval = IdleOption(current.IdleGpuFanPollingSeconds);
        _idleMotherboardTemperatureInterval = IdleOption(current.IdleMotherboardTemperaturePollingSeconds);
        _idleMemoryTemperatureInterval = IdleOption(current.IdleMemoryTemperaturePollingSeconds);
        _idleCpuFanInterval = IdleOption(current.IdleCpuFanPollingSeconds);
        _idleStorageTemperatureInterval = IdleOption(current.IdleStorageTemperaturePollingSeconds);
        _idlePowerInterval = IdleOption(current.IdlePowerPollingSeconds);
        _idleGpuHotSpotTemperatureInterval = IdleOption(current.IdleGpuHotSpotTemperaturePollingSeconds);

        // Rows are listed in the widget's own display order, so the editor and the widget always
        // agree about what "first" means.
        var labels = new Dictionary<HardwareMetrics, (string Label, string Unit)>
        {
            [HardwareMetrics.CpuTemperature] = ("CPU temperature", "°C"),
            [HardwareMetrics.CpuUsage] = ("CPU usage", "%"),
            [HardwareMetrics.MemoryUsage] = ("RAM usage", "%"),
            [HardwareMetrics.GpuTemperature] = ("GPU temperature", "°C"),
            [HardwareMetrics.GpuComputeUsage] = ("GPU core usage", "%"),
            [HardwareMetrics.GpuMemoryUsage] = ("GPU memory usage", "%"),
            [HardwareMetrics.GpuMemoryTemperature] = ("VRAM temperature", "°C"),
            [HardwareMetrics.GpuFan] = ("GPU fan", "RPM"),
            [HardwareMetrics.MotherboardTemperature] = ("Motherboard temperature", "°C"),
            [HardwareMetrics.MemoryTemperature] = ("RAM temperature", "°C"),
            [HardwareMetrics.CpuFan] = ("CPU fan", "RPM"),
            [HardwareMetrics.StorageTemperature] = ("Drive temperature", "°C"),
            [HardwareMetrics.Power] = ("CPU + GPU power", "W"),
            [HardwareMetrics.CpuPower] = ("CPU power", "W"),
            [HardwareMetrics.GpuPower] = ("GPU power", "W"),
            [HardwareMetrics.GpuHotSpotTemperature] = ("GPU hotspot temperature", "°C"),
        };

        var sensorLabels = sensorCatalog.DriveTemperatureSensors
            .ToDictionary(
                sensor => SensorMetricKeys.Drive(sensor.Id),
                sensor => (sensor.Label, "°C"));
        foreach (var sensor in sensorCatalog.CpuFanSensors)
        {
            sensorLabels[SensorMetricKeys.CpuFan(sensor.Id)] = (sensor.Label, "RPM");
        }

        var byKey = HardwareMetricsExtensions.Individual.ToDictionary(MetricTypes.DisplayKeyOf, m => m);
        StageRows = new ObservableCollection<MetricStageRowViewModel>(
            current.MetricDisplay
                .Where(entry => byKey.ContainsKey(entry.MetricType) || sensorLabels.ContainsKey(entry.MetricType))
                .Where(entry => entry.MetricType is not MetricTypes.CpuFanRpm and not MetricTypes.StorageTemperature)
                .Select(entry =>
            {
                if (byKey.TryGetValue(entry.MetricType, out var metric))
                {
                    var (staticLabel, staticUnit) = labels[metric];
                    return Row(current, metric, staticLabel, staticUnit);
                }

                var (sensorLabel, sensorUnit) = sensorLabels[entry.MetricType];
                return SensorRow(current, entry.MetricType, sensorLabel, sensorUnit);
            }));

        foreach (var row in StageRows)
        {
            row.Edited += (_, _) => ApplyLive();
        }

        SaveMonitoringCommand = new RelayCommand(SaveMonitoring, () => HasUnsavedMonitoringChanges);
        CloseCommand = new RelayCommand(closeWindow);
        ResetStagesCommand = new RelayCommand(ResetStagesToDefaults);
    }

    public ObservableCollection<PollingIntervalOption> IntervalOptions { get; }

    public ObservableCollection<PollingIntervalOption> IdleIntervalOptions { get; }

    public ObservableCollection<PollingIntervalOption> IdleAfterOptions { get; }

    /// <summary>Five-stage thresholds, one row per displayed metric.</summary>
    public ObservableCollection<MetricStageRowViewModel> StageRows { get; }

    public IReadOnlyList<string> WidgetAppearances { get; } =
        [AppSettings.RetroAppearance, AppSettings.DefaultAppearance];

    public IReadOnlyList<string> WidgetFonts => AppSettings.FontChoices;

    public IReadOnlyList<string> WidgetTextWeights => AppSettings.TextWeightChoices;


    public RelayCommand SaveMonitoringCommand { get; }

    public RelayCommand CloseCommand { get; }

    public RelayCommand ResetStagesCommand { get; }

    /// <summary>Shown when a value is rejected. Null when there is nothing wrong.</summary>
    public string? ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    /// <summary>Confirmation shown after the monitoring section is saved.</summary>
    public string? SavedMessage
    {
        get => _savedMessage;
        private set => SetProperty(ref _savedMessage, value);
    }

    /// <summary>Enables Save and warns that the pending monitoring edits are not in effect yet.</summary>
    public bool HasUnsavedMonitoringChanges
    {
        get => _hasUnsavedMonitoringChanges;
        private set
        {
            if (SetProperty(ref _hasUnsavedMonitoringChanges, value))
            {
                SaveMonitoringCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // ------------------------------------------------------- live (no Save)

    public string WidgetAppearance
    {
        get => _widgetAppearance;
        set => SetLive(ref _widgetAppearance, value);
    }

    public string WidgetFont
    {
        get => _widgetFont;
        set => SetLive(ref _widgetFont, value);
    }

    public string WidgetTextWeight
    {
        get => _widgetTextWeight;
        set => SetLive(ref _widgetTextWeight, value);
    }

    /// <summary>Presentation only, so it applies live like the rest of the appearance section.</summary>
    public bool ShowRamUsedAndTotal
    {
        get => _showRamUsedAndTotal;
        set => SetLive(ref _showRamUsedAndTotal, value);
    }

    /// <summary>
    /// Width of the label column, before scaling, as typed: a number of pixels, or "*" to take up
    /// whatever the other two columns leave over. Held as text so a half-typed value does not blow
    /// up binding; out-of-range or unparseable text is reported but not applied, leaving the last
    /// valid width in effect.
    /// </summary>
    public string WidgetLabelColumnWidthText
    {
        get => _widgetLabelColumnWidthText;
        set => SetColumnWidth(
            ref _widgetLabelColumnWidthText,
            value,
            ref _widgetLabelColumnWidth,
            AppSettings.MinimumLabelColumnWidth,
            AppSettings.MaximumLabelColumnWidth,
            "Label column width");
    }

    /// <summary>Width of the history-graph column, before scaling, as typed. See <see cref="WidgetLabelColumnWidthText"/>.</summary>
    public string WidgetGraphColumnWidthText
    {
        get => _widgetGraphColumnWidthText;
        set => SetColumnWidth(
            ref _widgetGraphColumnWidthText,
            value,
            ref _widgetGraphColumnWidth,
            AppSettings.MinimumGraphColumnWidth,
            AppSettings.MaximumGraphColumnWidth,
            "Graph column width");
    }

    /// <summary>Width of the value column, before scaling, as typed. See <see cref="WidgetLabelColumnWidthText"/>.</summary>
    public string WidgetValueColumnWidthText
    {
        get => _widgetValueColumnWidthText;
        set => SetColumnWidth(
            ref _widgetValueColumnWidthText,
            value,
            ref _widgetValueColumnWidth,
            AppSettings.MinimumValueColumnWidth,
            AppSettings.MaximumValueColumnWidth,
            "Value column width");
    }

    /// <summary>Overrides <see cref="WidgetValueColumnWidthText"/> while RAM used/total is shown. See <see cref="WidgetLabelColumnWidthText"/>.</summary>
    public string WidgetValueColumnWidthWithRamText
    {
        get => _widgetValueColumnWidthWithRamText;
        set => SetColumnWidth(
            ref _widgetValueColumnWidthWithRamText,
            value,
            ref _widgetValueColumnWidthWithRam,
            AppSettings.MinimumValueColumnWidth,
            AppSettings.MaximumValueColumnWidth,
            "Value column width (RAM shown)");
    }

    /// <summary>
    /// Narrowest a metric column may get before the widget folds columns back down, as typed. No
    /// "*" option -- this is a plain minimum, not a column width.
    /// </summary>
    public string WidgetMinimumColumnWidthText
    {
        get => _widgetMinimumColumnWidthText;
        set => SetPixelValue(
            ref _widgetMinimumColumnWidthText,
            value,
            ref _widgetMinimumColumnWidth,
            AppSettings.MinimumMinimumColumnWidth,
            AppSettings.MaximumMinimumColumnWidth,
            "Minimum column width");
    }

    /// <summary>Overrides <see cref="WidgetMinimumColumnWidthText"/> while RAM used/total is shown.</summary>
    public string WidgetMinimumColumnWidthWithRamText
    {
        get => _widgetMinimumColumnWidthWithRamText;
        set => SetPixelValue(
            ref _widgetMinimumColumnWidthWithRamText,
            value,
            ref _widgetMinimumColumnWidthWithRam,
            AppSettings.MinimumMinimumColumnWidth,
            AppSettings.MaximumMinimumColumnWidth,
            "Minimum column width (RAM shown)");
    }

    /// <summary>
    /// Lower bound on the graph's height, before scaling, as typed. The graph has no fixed height
    /// of its own -- it stretches to fill its row -- so this and the maximum below are what bound
    /// it. Held as text so a half-typed value does not blow up binding; out-of-range or
    /// unparseable text is reported but not applied, leaving the last valid bound in effect.
    /// </summary>
    public string WidgetGraphHeightMinimumText
    {
        get => _widgetGraphHeightMinimumText;
        set => SetPixelValue(
            ref _widgetGraphHeightMinimumText,
            value,
            ref _widgetGraphHeightMinimum,
            AppSettings.AbsoluteMinimumGraphHeight,
            AppSettings.AbsoluteMaximumGraphHeight,
            "Graph min height");
    }

    /// <summary>Upper bound on the graph's height, before scaling, as typed. See <see cref="WidgetGraphHeightMinimumText"/>.</summary>
    public string WidgetGraphHeightMaximumText
    {
        get => _widgetGraphHeightMaximumText;
        set => SetPixelValue(
            ref _widgetGraphHeightMaximumText,
            value,
            ref _widgetGraphHeightMaximum,
            AppSettings.AbsoluteMinimumGraphHeight,
            AppSettings.AbsoluteMaximumGraphHeight,
            "Graph max height");
    }

    /// <summary>
    /// Registers or removes the logon task. Applied immediately, and reverted if Windows refuses,
    /// so the checkbox never claims a state the system does not actually have.
    /// </summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value)
            {
                return;
            }

            if (!_startup.TrySetEnabled(value, out var error))
            {
                ValidationMessage = $"Could not change the startup setting: {error}";
                OnPropertyChanged();
                return;
            }

            _startWithWindows = value;
            ValidationMessage = null;
            OnPropertyChanged();
        }
    }

    // --------------------------------------------------- pending (needs Save)

    public bool UseUnifiedPollingInterval
    {
        get => _useUnifiedPollingInterval;
        set
        {
            if (SetPending(ref _useUnifiedPollingInterval, value))
            {
                OnPropertyChanged(nameof(IndividualIntervalsEnabled));
                OnPropertyChanged(nameof(IdleUnifiedVisible));
                OnPropertyChanged(nameof(IdleIndividualVisible));
            }
        }
    }

    public bool ConsolidatePower
    {
        get => _consolidatePower;
        set => SetPending(ref _consolidatePower, value);
    }

    /// <summary>The individual interval controls are shown only in individual mode.</summary>
    public bool IndividualIntervalsEnabled => !UseUnifiedPollingInterval;

    public bool UseIdlePolling
    {
        get => _useIdlePolling;
        set
        {
            if (SetPending(ref _useIdlePolling, value))
            {
                OnPropertyChanged(nameof(IdleUnifiedVisible));
                OnPropertyChanged(nameof(IdleIndividualVisible));
            }
        }
    }

    /// <summary>Idle polling uses the same unified/individual mode as active polling.</summary>
    public bool IdleUnifiedVisible => UseIdlePolling && UseUnifiedPollingInterval;

    public bool IdleIndividualVisible => UseIdlePolling && !UseUnifiedPollingInterval;

    public PollingIntervalOption IdleAfter
    {
        get => _idleAfter;
        set => SetPending(ref _idleAfter, value);
    }

    public PollingIntervalOption IdleUnifiedInterval
    {
        get => _idleUnifiedInterval;
        set => SetPending(ref _idleUnifiedInterval, value);
    }

    public PollingIntervalOption IdleCpuTemperatureInterval
    {
        get => _idleCpuTemperatureInterval;
        set => SetPending(ref _idleCpuTemperatureInterval, value);
    }

    public PollingIntervalOption IdleCpuUsageInterval
    {
        get => _idleCpuUsageInterval;
        set => SetPending(ref _idleCpuUsageInterval, value);
    }

    public PollingIntervalOption IdleMemoryUsageInterval
    {
        get => _idleMemoryUsageInterval;
        set => SetPending(ref _idleMemoryUsageInterval, value);
    }

    public PollingIntervalOption IdleGpuTemperatureInterval
    {
        get => _idleGpuTemperatureInterval;
        set => SetPending(ref _idleGpuTemperatureInterval, value);
    }

    public PollingIntervalOption IdleGpuComputeUsageInterval
    {
        get => _idleGpuComputeUsageInterval;
        set => SetPending(ref _idleGpuComputeUsageInterval, value);
    }

    public PollingIntervalOption IdleGpuMemoryUsageInterval
    {
        get => _idleGpuMemoryUsageInterval;
        set => SetPending(ref _idleGpuMemoryUsageInterval, value);
    }

    public PollingIntervalOption IdleGpuMemoryTemperatureInterval
    {
        get => _idleGpuMemoryTemperatureInterval;
        set => SetPending(ref _idleGpuMemoryTemperatureInterval, value);
    }

    public PollingIntervalOption IdleGpuFanInterval
    {
        get => _idleGpuFanInterval;
        set => SetPending(ref _idleGpuFanInterval, value);
    }

    public PollingIntervalOption IdleMotherboardTemperatureInterval
    {
        get => _idleMotherboardTemperatureInterval;
        set => SetPending(ref _idleMotherboardTemperatureInterval, value);
    }

    public PollingIntervalOption IdleMemoryTemperatureInterval
    {
        get => _idleMemoryTemperatureInterval;
        set => SetPending(ref _idleMemoryTemperatureInterval, value);
    }

    public PollingIntervalOption IdleCpuFanInterval
    {
        get => _idleCpuFanInterval;
        set => SetPending(ref _idleCpuFanInterval, value);
    }

    public PollingIntervalOption IdleStorageTemperatureInterval
    {
        get => _idleStorageTemperatureInterval;
        set => SetPending(ref _idleStorageTemperatureInterval, value);
    }

    public PollingIntervalOption IdlePowerInterval
    {
        get => _idlePowerInterval;
        set => SetPending(ref _idlePowerInterval, value);
    }

    public PollingIntervalOption IdleGpuHotSpotTemperatureInterval
    {
        get => _idleGpuHotSpotTemperatureInterval;
        set => SetPending(ref _idleGpuHotSpotTemperatureInterval, value);
    }

    public PollingIntervalOption UnifiedInterval
    {
        get => _unifiedInterval;
        set => SetPending(ref _unifiedInterval, value);
    }

    public PollingIntervalOption CpuTemperatureInterval
    {
        get => _cpuTemperatureInterval;
        set => SetPending(ref _cpuTemperatureInterval, value);
    }

    public PollingIntervalOption CpuUsageInterval
    {
        get => _cpuUsageInterval;
        set => SetPending(ref _cpuUsageInterval, value);
    }

    public PollingIntervalOption MemoryUsageInterval
    {
        get => _memoryUsageInterval;
        set => SetPending(ref _memoryUsageInterval, value);
    }

    public PollingIntervalOption GpuTemperatureInterval
    {
        get => _gpuTemperatureInterval;
        set => SetPending(ref _gpuTemperatureInterval, value);
    }

    public PollingIntervalOption GpuComputeUsageInterval
    {
        get => _gpuComputeUsageInterval;
        set => SetPending(ref _gpuComputeUsageInterval, value);
    }

    public PollingIntervalOption GpuMemoryUsageInterval
    {
        get => _gpuMemoryUsageInterval;
        set => SetPending(ref _gpuMemoryUsageInterval, value);
    }

    public PollingIntervalOption GpuMemoryTemperatureInterval
    {
        get => _gpuMemoryTemperatureInterval;
        set => SetPending(ref _gpuMemoryTemperatureInterval, value);
    }

    public PollingIntervalOption GpuFanInterval
    {
        get => _gpuFanInterval;
        set => SetPending(ref _gpuFanInterval, value);
    }

    public PollingIntervalOption MotherboardTemperatureInterval
    {
        get => _motherboardTemperatureInterval;
        set => SetPending(ref _motherboardTemperatureInterval, value);
    }

    public PollingIntervalOption MemoryTemperatureInterval
    {
        get => _memoryTemperatureInterval;
        set => SetPending(ref _memoryTemperatureInterval, value);
    }

    public PollingIntervalOption CpuFanInterval
    {
        get => _cpuFanInterval;
        set => SetPending(ref _cpuFanInterval, value);
    }

    public PollingIntervalOption StorageTemperatureInterval
    {
        get => _storageTemperatureInterval;
        set => SetPending(ref _storageTemperatureInterval, value);
    }

    public PollingIntervalOption PowerInterval
    {
        get => _powerInterval;
        set => SetPending(ref _powerInterval, value);
    }

    public PollingIntervalOption GpuHotSpotTemperatureInterval
    {
        get => _gpuHotSpotTemperatureInterval;
        set => SetPending(ref _gpuHotSpotTemperatureInterval, value);
    }

    // -------------------------------------------------------------- behaviour

    /// <summary>
    /// Persists the appearance and stage settings straight away. Reads the currently persisted
    /// object first and only overwrites the live fields, so monitoring edits still waiting for Save
    /// are not committed by a side door.
    /// </summary>
    private void ApplyLive()
    {
        var stages = new Dictionary<string, MetricStageSettings>();
        foreach (var row in StageRows)
        {
            if (!row.TryBuild(out var built))
            {
                // A half-typed or out-of-order threshold is simply not committed; the rest of the
                // live settings are not held hostage to it either, so nothing is applied this pass.
                ValidationMessage = $"{row.Label}: enter four increasing numbers within {row.ScaleText}.";
                return;
            }

            stages[row.MetricType] = built;
        }

        ValidationMessage = null;

        var updated = _settings.Current;

        // Merged into the persisted list, never assigned over it. The editor only holds rows whose
        // sensor the catalog knew about when the window opened, so replacing the list outright
        // deletes the settings of any drive or fan that was not enumerated at that moment -- along
        // with the two retired aggregate rows -- and they come back as new default rows on the next
        // launch. Edited rows are taken in editor order; everything else keeps its entry.
        var edited = StageRows
            .Select(row => new MetricDisplaySettings
            {
                MetricType = row.MetricType,
                Visible = row.IsVisible,
                // An untouched name is stored as empty so the row keeps tracking its default label
                // instead of freezing the label it was shown with.
                DisplayName = string.Equals(row.DisplayName.Trim(), row.DefaultDisplayName, StringComparison.Ordinal)
                    ? string.Empty
                    : row.DisplayName.Trim(),
                ShowGraph = row.IsGraphVisible,
            })
            .ToList();

        var editedKeys = edited.Select(entry => entry.MetricType).ToHashSet(StringComparer.Ordinal);
        updated.MetricDisplay = edited
            .Concat(updated.MetricDisplay.Where(entry => !editedKeys.Contains(entry.MetricType)))
            .ToList();
        updated.WidgetAppearance = AppSettings.NormalizeAppearance(WidgetAppearance);
        updated.WidgetFont = AppSettings.NormalizeFont(WidgetFont);
        updated.WidgetTextWeight = AppSettings.NormalizeTextWeight(WidgetTextWeight);
        updated.ShowRamUsedAndTotal = ShowRamUsedAndTotal;
        updated.WidgetLabelColumnWidth = _widgetLabelColumnWidth;
        updated.WidgetGraphColumnWidth = _widgetGraphColumnWidth;
        updated.WidgetValueColumnWidth = _widgetValueColumnWidth;
        updated.WidgetValueColumnWidthWithRam = _widgetValueColumnWidthWithRam;
        updated.WidgetMinimumColumnWidth = _widgetMinimumColumnWidth;
        updated.WidgetMinimumColumnWidthWithRam = _widgetMinimumColumnWidthWithRam;
        updated.WidgetGraphHeightMinimum = _widgetGraphHeightMinimum;
        updated.WidgetGraphHeightMaximum = _widgetGraphHeightMaximum;
        // Merged for the same reason as MetricDisplay: thresholds belonging to a sensor the editor
        // could not show must survive an edit made to some other row.
        foreach (var (key, value) in stages)
        {
            updated.MetricStages[key] = value;
        }

        _settings.Save(updated);
    }

    /// <summary>
    /// Commits the monitoring section. This is the change that rebuilds the polling schedule, which
    /// is why it waits for an explicit Save.
    /// </summary>
    private void SaveMonitoring()
    {
        if (!AreIntervalsValid())
        {
            SavedMessage = null;
            ValidationMessage = "Polling intervals must be between 1 and 300 seconds.";
            AppLog.Warn("Monitoring save rejected: an interval was out of range.");
            return;
        }

        if (!AreIdleIntervalsValid())
        {
            SavedMessage = null;
            ValidationMessage = "Idle intervals must be between 5 and 3600 seconds.";
            AppLog.Warn("Monitoring save rejected: an idle interval was out of range.");
            return;
        }

        ValidationMessage = null;

        var updated = _settings.Current;
        updated.UseUnifiedPollingInterval = UseUnifiedPollingInterval;
        updated.ConsolidatePower = ConsolidatePower;
        updated.UnifiedPollingSeconds = UnifiedInterval.Seconds;
        updated.CpuTemperaturePollingSeconds = CpuTemperatureInterval.Seconds;
        updated.CpuUsagePollingSeconds = CpuUsageInterval.Seconds;
        updated.MemoryUsagePollingSeconds = MemoryUsageInterval.Seconds;
        updated.GpuTemperaturePollingSeconds = GpuTemperatureInterval.Seconds;
        updated.GpuComputeUsagePollingSeconds = GpuComputeUsageInterval.Seconds;
        updated.GpuMemoryUsagePollingSeconds = GpuMemoryUsageInterval.Seconds;
        updated.GpuMemoryTemperaturePollingSeconds = GpuMemoryTemperatureInterval.Seconds;
        updated.GpuFanPollingSeconds = GpuFanInterval.Seconds;
        updated.MotherboardTemperaturePollingSeconds = MotherboardTemperatureInterval.Seconds;
        updated.MemoryTemperaturePollingSeconds = MemoryTemperatureInterval.Seconds;
        updated.CpuFanPollingSeconds = CpuFanInterval.Seconds;
        updated.StorageTemperaturePollingSeconds = StorageTemperatureInterval.Seconds;
        updated.PowerPollingSeconds = PowerInterval.Seconds;
        updated.GpuHotSpotTemperaturePollingSeconds = GpuHotSpotTemperatureInterval.Seconds;
        updated.UseIdlePolling = UseIdlePolling;
        updated.IdleAfterSeconds = IdleAfter.Seconds;
        updated.IdleUnifiedPollingSeconds = IdleUnifiedInterval.Seconds;
        updated.IdleCpuTemperaturePollingSeconds = IdleCpuTemperatureInterval.Seconds;
        updated.IdleCpuUsagePollingSeconds = IdleCpuUsageInterval.Seconds;
        updated.IdleMemoryUsagePollingSeconds = IdleMemoryUsageInterval.Seconds;
        updated.IdleGpuTemperaturePollingSeconds = IdleGpuTemperatureInterval.Seconds;
        updated.IdleGpuComputeUsagePollingSeconds = IdleGpuComputeUsageInterval.Seconds;
        updated.IdleGpuMemoryUsagePollingSeconds = IdleGpuMemoryUsageInterval.Seconds;
        updated.IdleGpuMemoryTemperaturePollingSeconds = IdleGpuMemoryTemperatureInterval.Seconds;
        updated.IdleGpuFanPollingSeconds = IdleGpuFanInterval.Seconds;
        updated.IdleMotherboardTemperaturePollingSeconds = IdleMotherboardTemperatureInterval.Seconds;
        updated.IdleMemoryTemperaturePollingSeconds = IdleMemoryTemperatureInterval.Seconds;
        updated.IdleCpuFanPollingSeconds = IdleCpuFanInterval.Seconds;
        updated.IdleStorageTemperaturePollingSeconds = IdleStorageTemperatureInterval.Seconds;
        updated.IdlePowerPollingSeconds = IdlePowerInterval.Seconds;
        updated.IdleGpuHotSpotTemperaturePollingSeconds = IdleGpuHotSpotTemperatureInterval.Seconds;

        _settings.Save(updated);

        HasUnsavedMonitoringChanges = false;
        SavedMessage = $"Monitoring settings applied at {DateTime.Now:HH:mm:ss}.";
    }

    /// <summary>
    /// Moves a row without persisting anything. Called repeatedly while a drag is in progress, so
    /// the list visibly reorders under the cursor; saving on every hover step would mean rewriting
    /// settings.json dozens of times per drag.
    /// </summary>
    public void MoveRowPreview(MetricStageRowViewModel row, int targetIndex)
    {
        var currentIndex = StageRows.IndexOf(row);
        if (currentIndex < 0)
        {
            return;
        }

        targetIndex = Math.Clamp(targetIndex, 0, StageRows.Count - 1);
        if (targetIndex == currentIndex)
        {
            return;
        }

        StageRows.Move(currentIndex, targetIndex);
    }

    /// <summary>Persists the current row order. Called once, when a drag is dropped.</summary>
    public void CommitRowOrder() => ApplyLive();

    private void ResetStagesToDefaults()
    {
        foreach (var row in StageRows)
        {
            row.Reset(MetricStageSettings.Default(row.MetricType));
        }
    }

    private bool AreIntervalsValid()
    {
        if (UseUnifiedPollingInterval)
        {
            return AppSettings.IsValidInterval(UnifiedInterval.Seconds);
        }

        return AppSettings.IsValidInterval(CpuTemperatureInterval.Seconds)
            && AppSettings.IsValidInterval(CpuUsageInterval.Seconds)
            && AppSettings.IsValidInterval(MemoryUsageInterval.Seconds)
            && AppSettings.IsValidInterval(GpuTemperatureInterval.Seconds)
            && AppSettings.IsValidInterval(GpuComputeUsageInterval.Seconds)
            && AppSettings.IsValidInterval(GpuMemoryUsageInterval.Seconds)
            && AppSettings.IsValidInterval(GpuMemoryTemperatureInterval.Seconds)
            && AppSettings.IsValidInterval(GpuFanInterval.Seconds)
            && AppSettings.IsValidInterval(MotherboardTemperatureInterval.Seconds)
            && AppSettings.IsValidInterval(MemoryTemperatureInterval.Seconds)
            && AppSettings.IsValidInterval(CpuFanInterval.Seconds)
            && AppSettings.IsValidInterval(StorageTemperatureInterval.Seconds)
            && AppSettings.IsValidInterval(PowerInterval.Seconds)
            && AppSettings.IsValidInterval(GpuHotSpotTemperatureInterval.Seconds);
    }

    private bool AreIdleIntervalsValid()
    {
        if (!UseIdlePolling)
        {
            return true;
        }

        if (!AppSettings.IsValidIdleAfter(IdleAfter.Seconds))
        {
            return false;
        }

        if (UseUnifiedPollingInterval)
        {
            return AppSettings.IsValidIdleInterval(IdleUnifiedInterval.Seconds);
        }

        return AppSettings.IsValidIdleInterval(IdleCpuTemperatureInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdleCpuUsageInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdleMemoryUsageInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdleGpuTemperatureInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdleGpuComputeUsageInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdleGpuMemoryUsageInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdleGpuMemoryTemperatureInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdleGpuFanInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdleMotherboardTemperatureInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdleMemoryTemperatureInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdleCpuFanInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdleStorageTemperatureInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdlePowerInterval.Seconds)
            && AppSettings.IsValidIdleInterval(IdleGpuHotSpotTemperatureInterval.Seconds);
    }

    private static MetricStageRowViewModel Row(
        AppSettings settings,
        HardwareMetrics metric,
        string label,
        string unit) =>
        new(
            MetricTypes.DisplayKeyOf(metric),
            label,
            unit,
            settings.ResolveStages(metric),
            settings.IsVisible(metric),
            settings.IsGraphVisible(metric),
            settings.ResolveDisplayName(metric),
            MetricTypes.DefaultDisplayNameOf(metric));

    private static MetricStageRowViewModel SensorRow(
        AppSettings settings,
        string metricType,
        string label,
        string unit) =>
        new(
            metricType,
            label,
            unit,
            settings.ResolveStages(
                metricType,
                SensorMetricKeys.IsDrive(metricType) ? MetricTypes.StorageTemperature : MetricTypes.CpuFanRpm),
            settings.MetricDisplay.FirstOrDefault(entry => entry.MetricType == metricType)?.Visible ?? true,
            settings.IsGraphVisible(metricType),
            settings.ResolveDisplayName(metricType, label),
            label);

    private void SetLive<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            ApplyLive();
        }
    }

    /// <summary>
    /// Parses one column-width textbox: "*" or a number of pixels within range. A valid value
    /// updates the canonical width that Save persists and applies live; anything else is left as
    /// typed and reported, without touching that canonical value, so a mid-edit textbox never
    /// reverts under the user's cursor.
    /// </summary>
    private void SetColumnWidth(
        ref string textField,
        string value,
        ref string widthField,
        double minimum,
        double maximum,
        string label,
        [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref textField, value, propertyName))
        {
            return;
        }

        if (!AppSettings.IsValidColumnWidth(value, minimum, maximum))
        {
            ValidationMessage = $"{label} must be \"*\" or a number between {minimum:0} and {maximum:0}.";
            return;
        }

        ValidationMessage = null;
        widthField = value.Trim();
        ApplyLive();
    }

    /// <summary>
    /// Parses one plain numeric textbox, no "*" option. Same last-valid-on-failure behaviour as
    /// <see cref="SetColumnWidth"/>.
    /// </summary>
    private void SetPixelValue(
        ref string textField,
        string value,
        ref double valueField,
        double minimum,
        double maximum,
        string label,
        [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref textField, value, propertyName))
        {
            return;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || parsed < minimum || parsed > maximum)
        {
            ValidationMessage = $"{label} must be a number between {minimum:0} and {maximum:0}.";
            return;
        }

        ValidationMessage = null;
        valueField = parsed;
        ApplyLive();
    }

    private static string FormatNumber(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private bool SetPending<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        SavedMessage = null;
        HasUnsavedMonitoringChanges = true;
        return true;
    }

    private PollingIntervalOption Option(int seconds) =>
        IntervalOptions.FirstOrDefault(option => option.Seconds == seconds)
        ?? IntervalOptions.First(option => option.Seconds == AppSettings.DefaultPollingSeconds);

    private PollingIntervalOption IdleOption(int seconds) =>
        IdleIntervalOptions.FirstOrDefault(option => option.Seconds == seconds)
        ?? IdleIntervalOptions.First(option => option.Seconds == AppSettings.DefaultIdlePollingSeconds);

    private PollingIntervalOption ResolveIdleAfter(int seconds) =>
        IdleAfterOptions.FirstOrDefault(option => option.Seconds == seconds)
        ?? IdleAfterOptions.First(option => option.Seconds == AppSettings.DefaultIdleAfterSeconds);

}
