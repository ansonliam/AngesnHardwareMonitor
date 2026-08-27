using System.Diagnostics;
using HardwareWidget.Models;
using HardwareWidget.Settings;

namespace HardwareWidget.Services;

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

    private readonly IHardwareMonitorService _monitor;
    private readonly HardwareHistoryRepository _history;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _restartGate = new(1, 1);

    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private bool _disposed;

    /// <summary>Set when a resume from sleep is observed, so the next cycle rediscovers sensors
    /// before reading and then reads immediately.</summary>
    private volatile bool _rediscoveryRequested;

    public HardwareMonitorScheduler(
        IHardwareMonitorService monitor,
        HardwareHistoryRepository history,
        SettingsService settings)
    {
        _monitor = monitor;
        _history = history;
        _settings = settings;
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

            AppLog.Info(settings.UseUnifiedPollingInterval
                ? $"Scheduler started in unified mode at {settings.UnifiedPollingSeconds}s."
                : "Scheduler started in individual mode: " + string.Join(
                    ", ",
                    HardwareMetricsExtensions.Individual.Select(
                        metric => $"{metric}={settings.ResolveIntervalSeconds(metric)}s")));
        }
        finally
        {
            _restartGate.Release();
        }
    }

    /// <summary>Asks the next cycle to rediscover sensors first. Used on resume from sleep.</summary>
    public void RequestRediscovery() => _rediscoveryRequested = true;

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
        var intervals = HardwareMetricsExtensions.Individual.ToDictionary(
            metric => metric,
            metric => (long)settings.ResolveIntervalSeconds(metric) * 1000L);

        var clock = Stopwatch.StartNew();

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
                        dueAt[metric] = completedAt + intervals[metric];
                    }
                }
            }

            var nextDueAt = dueAt.Values.Min();
            var delay = nextDueAt - clock.ElapsedMilliseconds;
            if (delay > 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken).ConfigureAwait(false);
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

        // Capture the timestamp once; both persisted timestamp columns derive from it.
        var timestamp = DateTimeOffset.UtcNow;

        SnapshotAvailable?.Invoke(this, snapshot);

        if (!_settings.Current.CollectHistory)
        {
            return;
        }

        await _history
            .AppendAsync(HardwareHistoryRecord.FromSnapshot(snapshot, timestamp), cancellationToken)
            .ConfigureAwait(false);
    }
}
