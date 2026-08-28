using System.Diagnostics;
using AngesnHardwareWidget.Models;
using AngesnHardwareWidget.Settings;

namespace AngesnHardwareWidget.Services;

/// <summary>
/// The single background polling loop. One loop -- not eight DispatcherTimers -- tracks a next-due
/// time per logical metric, wakes when the earliest one is due, and asks the monitor service for
/// exactly the metrics that are due. Because the service updates hardware objects rather than
/// individual sensors, that one call also coalesces the hardware updates: five GPU metrics due
/// together cost one GPU update, and a fast GPU metric may refresh the GPU often while the slower
/// GPU metrics are still only published and persisted on their own cadence.
///
/// Nothing here touches the WPF UI thread; consumers marshal <see cref="SnapshotAvailable"/>.
/// </summary>
public sealed class HardwareMonitorScheduler : IAsyncDisposable
{
    /// <summary>Metrics whose due times land within this window are treated as due together, so
    /// equal intervals coalesce into one cycle instead of drifting into separate ones.</summary>
    private static readonly TimeSpan CoalesceTolerance = TimeSpan.FromMilliseconds(75);

    /// <summary>
    /// How often the loop re-checks for input while on the idle cadence. Without this cap a long
    /// idle interval would also delay noticing that the user came back.
    /// </summary>
    private static readonly TimeSpan IdleStateCheckInterval = TimeSpan.FromSeconds(5);

    private readonly IHardwareMonitorService _monitor;
    private readonly SettingsService _settings;
    private readonly ISystemIdleTimeProvider _idleTime;
    private readonly SemaphoreSlim _restartGate = new(1, 1);

    /// <summary>
    /// Signalled to wake the loop out of its sleep early. A flag alone is not enough: with a 5
    /// minute interval the loop can be parked in a 5 minute wait, and a manual refresh has to take
    /// effect now rather than whenever that wait happens to expire.
    /// </summary>
    private readonly SemaphoreSlim _wake = new(0);

    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private bool _disposed;

    /// <summary>Set when a resume from sleep is observed, so the next cycle rediscovers sensors
    /// before reading and then reads immediately.</summary>
    private volatile bool _rediscoveryRequested;

    /// <summary>Set by a manual Refresh, so the next cycle samples every metric regardless of when
    /// each was next due.</summary>
    private volatile bool _immediateReadRequested;

    public HardwareMonitorScheduler(
        IHardwareMonitorService monitor,
        SettingsService settings,
        ISystemIdleTimeProvider? idleTime = null)
    {
        _monitor = monitor;
        _settings = settings;
        _idleTime = idleTime ?? new SystemIdleTimeProvider();
    }

    /// <summary>Raised on a background thread after every cycle that sampled at least one metric.</summary>
    public event EventHandler<HardwareSnapshot>? SnapshotAvailable;

    public void Start()
    {
        _ = RestartAsync();
    }

    /// <summary>
    /// Rebuilds the schedule from current settings. The old loop is cancelled and awaited before
    /// the new one starts, so the two never overlap. The Computer instance and the cached sensor
    /// references are untouched -- only the due-time table is rebuilt.
    /// </summary>
    public async Task RestartAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _restartGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopLoopAsync().ConfigureAwait(false);

            if (_disposed)
            {
                return;
            }

            var settings = _settings.Current;
            _cancellation = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(settings, _cancellation.Token));

            var idleSuffix = settings.UseIdlePolling
                ? $" Idle after {settings.IdleAfterSeconds}s."
                : " Idle polling off.";

            AppLog.Info((settings.UseUnifiedPollingInterval
                ? $"Scheduler started in unified mode at {settings.UnifiedPollingSeconds}s."
                : "Scheduler started in individual mode: " + string.Join(
                    ", ",
                    HardwareMetricsExtensions.Individual.Select(
                        metric => $"{metric}={settings.ResolveIntervalSeconds(metric)}s")))
                + idleSuffix);
        }
        finally
        {
            _restartGate.Release();
        }
    }

    /// <summary>Asks the next cycle to rediscover sensors first. Used on resume from sleep.</summary>
    public void RequestRediscovery()
    {
        _rediscoveryRequested = true;
        Wake();
    }

    /// <summary>
    /// Samples every metric as soon as possible, without waiting for anything to fall due. Backs
    /// the Refresh action on the widget's menu and the tray icon.
    /// </summary>
    public void RequestImmediateRead()
    {
        _immediateReadRequested = true;
        Wake();
    }

    private void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _restartGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopLoopAsync().ConfigureAwait(false);
        }
        finally
        {
            _restartGate.Release();
            _restartGate.Dispose();
            _wake.Dispose();
        }
    }

    private async Task StopLoopAsync()
    {
        var cancellation = _cancellation;
        var loop = _loop;
        _cancellation = null;
        _loop = null;

        if (cancellation is null)
        {
            return;
        }

        await cancellation.CancelAsync().ConfigureAwait(false);

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                AppLog.Error("Polling loop ended with an error", exception);
            }
        }

        cancellation.Dispose();
    }

    private async Task RunAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        // Every metric starts due, which gives the required immediate first read.
        var dueAt = HardwareMetricsExtensions.Individual.ToDictionary(metric => metric, _ => 0L);

        var clock = Stopwatch.StartNew();
        var idleThreshold = TimeSpan.FromSeconds(settings.IdleAfterSeconds);
        var wasIdle = false;

        // The wake signal outlives individual loops, so drop any counts left over from the previous
        // one. A stale count would only cost one wasted wake-up, but starting clean is clearer.
        while (_wake.Wait(0))
        {
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_rediscoveryRequested)
            {
                _rediscoveryRequested = false;
                _monitor.Rediscover();

                // Read everything straight after a rediscovery so the UI recovers immediately.
                foreach (var metric in HardwareMetricsExtensions.Individual)
                {
                    dueAt[metric] = 0L;
                }
            }

            if (_immediateReadRequested)
            {
                _immediateReadRequested = false;

                // A one-off read that deliberately leaves dueAt alone. Marking everything due
                // instead would restart every metric's countdown from the moment of the refresh,
                // so repeatedly pressing Refresh would drag the whole cadence along with it. The
                // schedule is the user's setting; a manual refresh is not a reschedule.
                await RunCycleAsync(HardwareMetrics.All, cancellationToken).ConfigureAwait(false);
            }

            // Intervals are resolved per cycle rather than once up front, because the idle
            // interval can take over at any moment without the schedule being rebuilt.
            var isIdle = settings.UseIdlePolling && _idleTime.GetIdleTime() >= idleThreshold;

            if (isIdle != wasIdle)
            {
                wasIdle = isIdle;
                AppLog.Info(isIdle
                    ? $"Machine idle for {idleThreshold.TotalSeconds:0}s; switching to the idle polling cadence."
                    : "Input resumed; returning to the active polling cadence.");

                // Coming back from idle, refresh everything at once: the readings on screen are as
                // stale as the idle interval, and the user is looking at them again.
                if (!isIdle)
                {
                    foreach (var metric in HardwareMetricsExtensions.Individual)
                    {
                        dueAt[metric] = 0L;
                    }
                }
            }

            var now = clock.ElapsedMilliseconds;
            var threshold = now + (long)CoalesceTolerance.TotalMilliseconds;

            var due = HardwareMetrics.None;
            foreach (var (metric, metricDueAt) in dueAt)
            {
                if (metricDueAt <= threshold)
                {
                    due |= metric;
                }
            }

            if (due != HardwareMetrics.None)
            {
                await RunCycleAsync(due, cancellationToken).ConfigureAwait(false);

                // Schedule from the completion of the read rather than from its start, so a slow
                // read cannot make the loop spin with a permanently overdue metric.
                var completedAt = clock.ElapsedMilliseconds;
                foreach (var metric in HardwareMetricsExtensions.Individual)
                {
                    if (due.Includes(metric))
                    {
                        dueAt[metric] = completedAt
                            + ((long)settings.ResolveIntervalSeconds(metric, isIdle) * 1000L);
                    }
                }
            }

            var nextDueAt = dueAt.Values.Min();
            var delay = nextDueAt - clock.ElapsedMilliseconds;

            // While idle, never sleep past the point where returning input should be noticed, so
            // waking the machine does not wait out a long idle interval before refreshing.
            if (isIdle && delay > IdleStateCheckInterval.TotalMilliseconds)
            {
                delay = (long)IdleStateCheckInterval.TotalMilliseconds;
            }

            if (delay > 0)
            {
                try
                {
                    // Waiting on the signal rather than Task.Delay is what lets Refresh, a resume
                    // from sleep, or a settings change cut the sleep short.
                    await _wake.WaitAsync(TimeSpan.FromMilliseconds(delay), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task RunCycleAsync(HardwareMetrics due, CancellationToken cancellationToken)
    {
        HardwareSnapshot snapshot;
        try
        {
            snapshot = _monitor.Read(due);
        }
        catch (Exception exception)
        {
            // The service already degrades internally; this is the last line of defence so a
            // hardware fault can never terminate the loop.
            AppLog.Error("Cycle read failed", exception);
            return;
        }

        SnapshotAvailable?.Invoke(this, snapshot);
    }
}
