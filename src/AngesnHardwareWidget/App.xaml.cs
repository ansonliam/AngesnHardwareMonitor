using System.Windows;
using System.Windows.Threading;
using AngesnHardwareWidget.Models;
using AngesnHardwareWidget.Services;
using AngesnHardwareWidget.Settings;
using AngesnHardwareWidget.ViewModels;
using AngesnHardwareWidget.Views;
using Microsoft.Win32;

namespace AngesnHardwareWidget;

/// <summary>
/// Composition root. Builds the long-lived monitor service, settings store, view model and
/// background scheduler, then creates the widget Window only while the user wants it visible.
/// </summary>
public partial class App : Application, IApplicationController
{
    private LibreHardwareMonitorService? _monitor;
    private HardwareMonitorScheduler? _scheduler;
    private SettingsService? _settings;
    private TrayIconService? _tray;
    private MainViewModel? _mainViewModel;
    private MainWindow? _widget;
    private SettingsWindow? _settingsWindow;
    private AppSettings? _activeSchedule;
    private SingleInstanceService? _singleInstance;
    private WheaHardwareErrorMonitor? _wheaMonitor;
    private DispatcherTimer? _hardwareAlertTimer;
    private bool _exiting;

    public bool IsExiting => _exiting;

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        // Before anything opens a file under the data folder, since it moves that folder.
        AppPaths.MigrateLegacyDataIfNeeded();

        AppLog.Info($"Angesn Hardware Widget starting (data: {AppPaths.DataDirectory}).");
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Two instances would each poll and each write history, giving every metric duplicate rows
        // at slightly different timestamps. Hand over to the running one and quit instead.
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquirePrimaryInstance())
        {
            _singleInstance.SignalExistingInstance();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _singleInstance.StartListening(ShowWidget);

        _settings = new SettingsService();
        _activeSchedule = _settings.Current;
        _monitor = new LibreHardwareMonitorService(_settings);

        // Initialization can fail on a locked-down machine or without the kernel driver; the widget
        // must still come up and show "--" rather than refusing to start.
        try
        {
            _monitor.Initialize();
        }
        catch (Exception exception)
        {
            AppLog.Error("LibreHardwareMonitor initialization failed; the widget will run degraded", exception);
        }

        EnsureSensorMetricRows(_settings, _monitor.GetSensorCatalog());
        _activeSchedule = _settings.Current;

        _scheduler = new HardwareMonitorScheduler(_monitor, _settings);

        _mainViewModel = new MainViewModel(
            _settings,
            _monitor.GetSensorCatalog(),
            Dispatcher,
            ShowSettings,
            RefreshNow,
            ExitApplication);

        if (_settings.Current.ShowWidget)
        {
            ShowWidgetCore(persistVisibility: false);
        }

        _wheaMonitor = new WheaHardwareErrorMonitor();
        _hardwareAlertTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _hardwareAlertTimer.Tick += (_, _) =>
            _mainViewModel.ApplyWheaHardwareError(_wheaMonitor.Poll());
        _hardwareAlertTimer.Start();

        _scheduler.SnapshotAvailable += (_, snapshot) => _mainViewModel.Apply(snapshot);
        _settings.SettingsChanged += OnSettingsChanged;

        _tray = new TrayIconService(this);
        _tray.Initialize();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // If startup is enabled but the task points at an older location (rebuilt or moved app),
        // repoint it. Cheap, idempotent, and it stops "start with Windows" silently rotting.
        new WindowsStartupService().EnsurePathCurrent();

        // Subscribed before the scheduler starts so the very first snapshot is caught. Waiting for
        // a snapshot rather than reclaiming here is deliberate: the first sampling cycle is still
        // running on a background thread at this point, so its allocations are not garbage yet and
        // collecting now would just be paid for twice.
        _scheduler.SnapshotAvailable += OnFirstSnapshotAvailable;
        MemoryReclaimer.StartPeriodicReclaim(Dispatcher);

        _scheduler.Start();
    }

    private void OnFirstSnapshotAvailable(object? sender, HardwareSnapshot snapshot)
    {
        if (sender is HardwareMonitorScheduler scheduler)
        {
            scheduler.SnapshotAvailable -= OnFirstSnapshotAvailable;
        }

        MemoryReclaimer.ReclaimAfterStartup(Dispatcher);
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        AppLog.Info("Angesn Hardware Widget shutting down.");

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _hardwareAlertTimer?.Stop();
        if (_settings is not null)
        {
            _settings.SettingsChanged -= OnSettingsChanged;
        }

        if (_scheduler is not null)
        {
            // Bounded wait: shutdown must not hang on a cycle that is mid-read.
            _scheduler.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        }

        _tray?.Dispose();
        _singleInstance?.Dispose();
        _monitor?.Dispose();

        base.OnExit(eventArgs);
    }

    // ------------------------------------------------- IApplicationController

    public bool IsWidgetVisible() => Dispatcher.Invoke(() => _widget?.IsVisible == true);

    public void ShowWidget() => Dispatcher.BeginInvoke(() =>
    {
        ShowWidgetCore(persistVisibility: true);
    });

    public void HideWidget() => Dispatcher.BeginInvoke(HideWidgetCore);

    public void RefreshNow() => _scheduler?.RequestImmediateRead();

    public void ShowSettings() => Dispatcher.BeginInvoke(() =>
    {
        if (_settings is null)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var sensorCatalog = _monitor?.GetSensorCatalog()
            ?? new HardwareSensorCatalog([], []);
        _settingsWindow = new SettingsWindow(_settings, sensorCatalog, this);

        // Only own the settings window while the widget is actually on screen: an owner that is
        // hidden would take the dialog with it.
        if (_widget?.IsVisible == true)
        {
            _settingsWindow.Owner = _widget;
        }

        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    });

    public void ExitApplication() => Dispatcher.BeginInvoke(() =>
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        Shutdown();
    });

    private void ShowWidgetCore(bool persistVisibility)
    {
        if (_settings is null || _mainViewModel is null)
        {
            return;
        }

        if (persistVisibility)
        {
            SetWidgetVisibility(true);
        }

        if (_widget is null)
        {
            _widget = new MainWindow(_settings, this)
            {
                DataContext = _mainViewModel,
            };
            _widget.Closed += OnWidgetClosed;
            MainWindow = _widget;
        }

        _widget.Show();
        _widget.Activate();
    }

    private void HideWidgetCore()
    {
        if (_settings is null)
        {
            return;
        }

        if (_widget is not null)
        {
            // WPF closes owned windows with their owner. Settings must stay usable after its
            // checkbox releases the widget, so remove that ownership first.
            foreach (Window window in Windows)
            {
                if (ReferenceEquals(window.Owner, _widget))
                {
                    window.Owner = null;
                }
            }

            _widget.CloseForHide();
        }

        SetWidgetVisibility(false);
    }

    private void OnWidgetClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        window.Closed -= OnWidgetClosed;
        if (!ReferenceEquals(_widget, window))
        {
            return;
        }

        _widget = null;
        if (ReferenceEquals(MainWindow, window))
        {
            MainWindow = null;
        }

        if (!IsExiting)
        {
            MemoryReclaimer.ReclaimAfterWindowClose(Dispatcher);
        }
    }

    private void SetWidgetVisibility(bool isVisible)
    {
        if (_settings is null)
        {
            return;
        }

        var updated = _settings.Current;
        if (updated.ShowWidget == isVisible)
        {
            return;
        }

        updated.ShowWidget = isVisible;
        _settings.Save(updated);
    }

    // ------------------------------------------------------------------ wiring

    /// <summary>
    /// Rebuilds the polling schedule, but only when the schedule actually changed. Settings are
    /// also saved when the widget is dragged or resized, and restarting the scheduler for that
    /// would reset every due time and fire a burst of reads on each drag.
    /// </summary>
    private void OnSettingsChanged(object? sender, AppSettings updated)
    {
        if (_scheduler is null)
        {
            return;
        }

        var sensorSelectionChanged = _activeSchedule is not null
            && !_activeSchedule.HasSameSensorSelection(updated);
        if (sensorSelectionChanged)
        {
            _scheduler.RequestRediscovery();
        }

        if (_activeSchedule is not null && _activeSchedule.HasSamePollingSchedule(updated))
        {
            _activeSchedule = updated;
            return;
        }

        _activeSchedule = updated;
        _ = _scheduler.RestartAsync();
    }

    private static void EnsureSensorMetricRows(SettingsService settings, HardwareSensorCatalog catalog)
    {
        var updated = settings.Current;
        var changed = false;

        // The former aggregate rows have been superseded by one row per detected source.
        foreach (var metric in new[] { HardwareMetrics.CpuFan, HardwareMetrics.StorageTemperature })
        {
            var entry = updated.MetricDisplay.FirstOrDefault(item => item.MetricType == MetricTypes.DisplayKeyOf(metric));
            if (entry is not null && entry.Visible)
            {
                entry.Visible = false;
                changed = true;
            }
        }

        changed |= SyncSensorRows(updated, catalog.DriveTemperatureSensors, SensorMetricKeys.Drive);
        changed |= SyncSensorRows(updated, catalog.CpuFanSensors, SensorMetricKeys.CpuFan);

        if (changed)
        {
            settings.Save(updated);
        }
    }

    private static bool SyncSensorRows(
        AppSettings settings,
        IEnumerable<HardwareSensorOption> sensors,
        Func<string, string> keyOf)
    {
        var changed = false;
        foreach (var sensor in sensors)
        {
            var key = keyOf(sensor.Id);
            var existing = settings.MetricDisplay.FirstOrDefault(entry => entry.MetricType == key);
            if (existing is not null)
            {
                // An empty display name means "use whatever the catalog calls this sensor now", so
                // a name the app generated is cleared rather than left frozen: a drive first seen
                // before Windows would give up its volume letter must not be stuck on its model
                // number forever. A name the user typed is never touched.
                if (existing.DisplayName.Length > 0 && sensor.IsGeneratedLabel(existing.DisplayName))
                {
                    existing.DisplayName = string.Empty;
                    changed = true;
                }

                continue;
            }

            settings.MetricDisplay.Add(new Settings.MetricDisplaySettings
            {
                MetricType = key,
                // Deliberately empty: the label is resolved from the live catalog at render time.
                DisplayName = string.Empty,
                Visible = true,
                ShowGraph = true,
            });
            settings.MetricStages[key] = MetricStageSettings.Default(
                SensorMetricKeys.IsDrive(key) ? MetricTypes.StorageTemperature : MetricTypes.CpuFanRpm);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// After a resume the GPU may have been reset and the cached sensor references can be stale, so
    /// the next cycle rediscovers and then reads immediately. No manual restart is needed.
    /// </summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode != PowerModes.Resume)
        {
            return;
        }

        AppLog.Info("Resumed from sleep; requesting sensor rediscovery.");
        _scheduler?.RequestRediscovery();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        // The widget stays alive: a UI-thread fault is logged and swallowed rather than closing it.
        AppLog.Error("Unhandled UI exception", eventArgs.Exception);
        eventArgs.Handled = true;
    }
}
