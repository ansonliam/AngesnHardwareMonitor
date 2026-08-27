using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using HardwareWidget.Models;
using HardwareWidget.Services;
using HardwareWidget.Settings;

namespace HardwareWidget.ViewModels;

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
    private static readonly int[] OfferedSeconds = [5, 10, 30, 60, 120, 300];

    // Idle cadences are allowed to be much longer than active ones, so they get their own lists.
    private static readonly int[] OfferedIdleSeconds = [10, 30, 60, 120, 300, 600, 900, 1800, 3600];
    private static readonly int[] OfferedIdleAfterSeconds = [60, 120, 300, 600, 900, 1800, 3600];

    private readonly SettingsService _settings;

    // Live section.
    private string _widgetAppearance;
    private string _widgetFont;
    private string _widgetTextWeight;
    private bool _showRamUsedAndTotal;

    // Pending section, committed by Save.
    private bool _collectHistory;
    private bool _useUnifiedPollingInterval;
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
    private PollingIntervalOption _unifiedInterval;
    private PollingIntervalOption _cpuTemperatureInterval;
    private PollingIntervalOption _cpuUsageInterval;
    private PollingIntervalOption _memoryUsageInterval;
    private PollingIntervalOption _gpuTemperatureInterval;
    private PollingIntervalOption _gpuComputeUsageInterval;
    private PollingIntervalOption _gpuMemoryUsageInterval;
    private PollingIntervalOption _gpuMemoryTemperatureInterval;
    private PollingIntervalOption _gpuFanInterval;

    private string? _validationMessage;
    private string? _savedMessage;
    private bool _hasUnsavedMonitoringChanges;

    public SettingsViewModel(SettingsService settings, Action closeWindow)
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

        _collectHistory = current.CollectHistory;
        _useUnifiedPollingInterval = current.UseUnifiedPollingInterval;
        _unifiedInterval = Option(current.UnifiedPollingSeconds);
        _cpuTemperatureInterval = Option(current.CpuTemperaturePollingSeconds);
        _cpuUsageInterval = Option(current.CpuUsagePollingSeconds);
        _memoryUsageInterval = Option(current.MemoryUsagePollingSeconds);
        _gpuTemperatureInterval = Option(current.GpuTemperaturePollingSeconds);
        _gpuComputeUsageInterval = Option(current.GpuComputeUsagePollingSeconds);
        _gpuMemoryUsageInterval = Option(current.GpuMemoryUsagePollingSeconds);
        _gpuMemoryTemperatureInterval = Option(current.GpuMemoryTemperaturePollingSeconds);
        _gpuFanInterval = Option(current.GpuFanPollingSeconds);

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
        };

        var byKey = HardwareMetricsExtensions.Individual.ToDictionary(MetricTypes.DisplayKeyOf, m => m);
        var orderedMetrics = current.MetricDisplay
            .Where(entry => byKey.ContainsKey(entry.MetricType))
            .Select(entry => byKey[entry.MetricType])
            .ToList();

        StageRows = new ObservableCollection<MetricStageRowViewModel>(
            orderedMetrics.Select(metric =>
            {
                var (label, unit) = labels[metric];
                return Row(current, metric, label, unit);
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

    // --------------------------------------------------- pending (needs Save)

    public bool CollectHistory
    {
        get => _collectHistory;
        set => SetPending(ref _collectHistory, value);
    }

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
        updated.MetricDisplay = StageRows
            .Select(row => new MetricDisplaySettings { MetricType = row.MetricType, Visible = row.IsVisible })
            .ToList();
        updated.WidgetAppearance = AppSettings.NormalizeAppearance(WidgetAppearance);
        updated.WidgetFont = AppSettings.NormalizeFont(WidgetFont);
        updated.WidgetTextWeight = AppSettings.NormalizeTextWeight(WidgetTextWeight);
        updated.ShowRamUsedAndTotal = ShowRamUsedAndTotal;
        updated.MetricStages = stages;

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
        updated.CollectHistory = CollectHistory;
        updated.UseUnifiedPollingInterval = UseUnifiedPollingInterval;
        updated.UnifiedPollingSeconds = UnifiedInterval.Seconds;
        updated.CpuTemperaturePollingSeconds = CpuTemperatureInterval.Seconds;
        updated.CpuUsagePollingSeconds = CpuUsageInterval.Seconds;
        updated.MemoryUsagePollingSeconds = MemoryUsageInterval.Seconds;
        updated.GpuTemperaturePollingSeconds = GpuTemperatureInterval.Seconds;
        updated.GpuComputeUsagePollingSeconds = GpuComputeUsageInterval.Seconds;
        updated.GpuMemoryUsagePollingSeconds = GpuMemoryUsageInterval.Seconds;
        updated.GpuMemoryTemperaturePollingSeconds = GpuMemoryTemperatureInterval.Seconds;
        updated.GpuFanPollingSeconds = GpuFanInterval.Seconds;
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

        _settings.Save(updated);

        HasUnsavedMonitoringChanges = false;
        SavedMessage = $"Monitoring settings applied at {DateTime.Now:HH:mm:ss}.";
    }

    /// <summary>
    /// Moves a row, which is what reorders the widget. Applied live so the widget reflows as the
    /// row is dropped.
    /// </summary>
    public void MoveRow(MetricStageRowViewModel row, int targetIndex)
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
        ApplyLive();
    }

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
            && AppSettings.IsValidInterval(GpuFanInterval.Seconds);
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
            && AppSettings.IsValidIdleInterval(IdleGpuFanInterval.Seconds);
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
            settings.IsVisible(metric));

    private void SetLive<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            ApplyLive();
        }
    }

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
