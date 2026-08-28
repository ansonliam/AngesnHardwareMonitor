using System.Windows;
using System.Windows.Threading;
using AngesnHardwareWidget.Services;
using AngesnHardwareWidget.Settings;
using AngesnHardwareWidget.ViewModels;
using AngesnHardwareWidget.Views;
using Microsoft.Win32;

namespace AngesnHardwareWidget;

/// <summary>
/// Composition root. Builds the one long-lived monitor service, settings store and background
/// scheduler, then wires them to the widget and tray icon.
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
    private bool _exiting;

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
        _monitor = new LibreHardwareMonitorService();

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

        _scheduler = new HardwareMonitorScheduler(_monitor, _settings);

        _widget = new MainWindow(_settings);
        _mainViewModel = new MainViewModel(_settings, Dispatcher, ShowSettings, RefreshNow, ExitApplication);
        _widget.DataContext = _mainViewModel;
        MainWindow = _widget;
        _widget.Show();

        _scheduler.SnapshotAvailable += (_, snapshot) => _mainViewModel.Apply(snapshot);
        _settings.SettingsChanged += OnSettingsChanged;

        _tray = new TrayIconService(this);
        _tray.Initialize();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // If startup is enabled but the task points at an older location (rebuilt or moved app),
        // repoint it. Cheap, idempotent, and it stops "start with Windows" silently rotting.
        new WindowsStartupService().EnsurePathCurrent();

        _scheduler.Start();
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        AppLog.Info("Angesn Hardware Widget shutting down.");

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

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

    public bool IsWidgetVisible() => _widget?.IsVisible ?? false;

    public void ShowWidget() => Dispatcher.BeginInvoke(() =>
    {
        if (_widget is null)
        {
            return;
        }

        _widget.Show();
        _widget.Activate();
    });

    public void HideWidget() => Dispatcher.BeginInvoke(() => _widget?.Hide());

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

        _settingsWindow = new SettingsWindow(_settings);

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

        if (_activeSchedule is not null && _activeSchedule.HasSamePollingSchedule(updated))
        {
            _activeSchedule = updated;
            return;
        }

        _activeSchedule = updated;
        _ = _scheduler.RestartAsync();
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
