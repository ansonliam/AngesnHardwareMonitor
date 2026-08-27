using System.Data;
using System.IO;
using HardwareWidget.Models;
using Microsoft.Data.Sqlite;

namespace HardwareWidget.Services;

/// <summary>
/// Append-only SQLite history under %LOCALAPPDATA%\HardwareWidget\Data.
///
/// Storage shape: one row per (metric, timestamp). The plan sketched both a wide
/// HardwareReadings table and a narrow MetricType/DeviceId table; the narrow one is used because
/// it is the only one that satisfies the MetricType/DeviceId/index requirements, and because it
/// expresses the individual-polling rule for free -- a metric that was not due simply has no row,
/// so no future chart can mistake a stale repeat for a new sample. A metric that *was* due but
/// whose sensor was unavailable is written with a NULL Value, which is a different and equally
/// real observation.
///
/// Nothing here is allowed to break monitoring: every call swallows and logs its failures.
/// </summary>
public sealed class HardwareHistoryRepository : IDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string _connectionString;
    private readonly string _databasePath;

    private bool _initialized;
    private bool _disposed;
    private bool _degraded;

    public HardwareHistoryRepository(string? databasePath = null)
    {
        _databasePath = databasePath ?? AppPaths.DatabasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        }.ToString();
    }

    /// <summary>True once a database operation has failed. Monitoring continues regardless; this
    /// only exists so the UI could surface "history unavailable" later.</summary>
    public bool IsDegraded => _degraded;

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Persists every metric that was sampled this cycle. Both timestamp columns are derived from
    /// the record's single captured DateTimeOffset, so they can never disagree.
    /// </summary>
    public async Task AppendAsync(HardwareHistoryRecord record, CancellationToken cancellationToken = default)
    {
        if (_disposed || record.SampledMetrics == HardwareMetrics.None)
        {
            return;
        }

        var readings = BuildReadings(record);
        if (readings.Count == 0)
        {
            return;
        }

        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!_initialized)
            {
                return;
            }

            var timestampUtc = record.TimestampUtc.ToUniversalTime().ToString("O");
            var timestampUnixMs = record.TimestampUtc.ToUnixTimeMilliseconds();

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO MetricReadings (TimestampUtc, TimestampUnixMs, MetricType, DeviceId, Value)
                VALUES ($timestampUtc, $timestampUnixMs, $metricType, $deviceId, $value);
                """;

            command.Parameters.AddWithValue("$timestampUtc", timestampUtc);
            command.Parameters.AddWithValue("$timestampUnixMs", timestampUnixMs);
            var metricTypeParameter = command.Parameters.Add("$metricType", SqliteType.Text);
            var deviceIdParameter = command.Parameters.Add("$deviceId", SqliteType.Text);
            var valueParameter = command.Parameters.Add("$value", SqliteType.Real);

            foreach (var reading in readings)
            {
                metricTypeParameter.Value = reading.MetricType;
                deviceIdParameter.Value = (object?)reading.DeviceId ?? DBNull.Value;
                valueParameter.Value = (object?)reading.Value ?? DBNull.Value;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            // A history write failure is logged and dropped. Live monitoring must not depend on
            // the database being available.
            _degraded = true;
            AppLog.Warn($"History insert failed; continuing to monitor: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Retention is deliberately isolated here rather than inlined into the write path, so a
    /// "keep 30 days" policy plus its UI can be added later without touching anything else. The
    /// MVP policy is Unlimited, which prunes nothing.
    /// </summary>
    public async Task ApplyRetentionAsync(HistoryRetentionPolicy policy, CancellationToken cancellationToken = default)
    {
        if (_disposed || policy.MaxAge is not { } maxAge)
        {
            return;
        }

        var cutoffUnixMs = DateTimeOffset.UtcNow.Subtract(maxAge).ToUnixTimeMilliseconds();

        try
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!_initialized)
            {
                return;
            }

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM MetricReadings WHERE TimestampUnixMs < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", cutoffUnixMs);

            var deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (deleted > 0)
            {
                AppLog.Info($"Retention removed {deleted} history rows older than {maxAge}.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _degraded = true;
            AppLog.Warn($"Retention pass failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeGate.Dispose();
        SqliteConnection.ClearAllPools();
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();

            // VARCHAR(255) documents the intended maximum length; SQLite's type affinity does not
            // enforce it, so BuildReadings validates length in code before insertion.
            command.CommandText =
                """
                PRAGMA journal_mode = WAL;

                CREATE TABLE IF NOT EXISTS MetricReadings
                (
                    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    TimestampUtc    TEXT NOT NULL,
                    TimestampUnixMs INTEGER NOT NULL,
                    MetricType      VARCHAR(255) NOT NULL,
                    DeviceId        VARCHAR(255) NULL,
                    Value           REAL NULL
                );

                CREATE INDEX IF NOT EXISTS IX_MetricReadings_MetricTimestamp
                ON MetricReadings (MetricType, TimestampUnixMs);

                CREATE INDEX IF NOT EXISTS IX_MetricReadings_MetricDeviceTimestamp
                ON MetricReadings (MetricType, DeviceId, TimestampUnixMs);

                CREATE INDEX IF NOT EXISTS IX_MetricReadings_Timestamp
                ON MetricReadings (TimestampUnixMs);
                """;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _initialized = true;
            _degraded = false;
            AppLog.Info($"History database ready at {_databasePath}.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _degraded = true;
            AppLog.Warn($"History database unavailable; monitoring continues without history: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// Fans a snapshot out into individual metric rows. A metric outside SampledMetrics produces no
    /// row at all; a sampled metric with no value produces a NULL-valued row.
    /// </summary>
    private static List<MetricReading> BuildReadings(HardwareHistoryRecord record)
    {
        var readings = new List<MetricReading>(12);
        var sampled = record.SampledMetrics;

        void Add(string metricType, double? value, string? deviceId)
        {
            if (metricType.Length > MetricTypes.MaxKeyLength)
            {
                AppLog.Warn($"Skipping over-long metric key '{metricType}'.");
                return;
            }

            if (deviceId?.Length > MetricTypes.MaxKeyLength)
            {
                AppLog.Warn($"Truncating over-long device id for '{metricType}'.");
                deviceId = deviceId[..MetricTypes.MaxKeyLength];
            }

            readings.Add(new MetricReading(metricType, deviceId, value));
        }

        if (sampled.Includes(HardwareMetrics.CpuTemperature))
        {
            Add(MetricTypes.CpuTemperature, record.CpuTemperature, record.CpuDeviceId);
        }

        if (sampled.Includes(HardwareMetrics.CpuUsage))
        {
            Add(MetricTypes.CpuUsage, record.CpuUsagePercent, record.CpuDeviceId);
        }

        if (sampled.Includes(HardwareMetrics.MemoryUsage))
        {
            // All three RAM values are always persisted, regardless of what the compact UI shows.
            // DeviceId stays null: system RAM is not a per-device metric.
            Add(MetricTypes.MemoryUsedGb, record.MemoryUsedGb, null);
            Add(MetricTypes.MemoryTotalGb, record.MemoryTotalGb, null);
            Add(MetricTypes.MemoryUsagePercent, record.MemoryUsagePercent, null);
        }

        if (sampled.Includes(HardwareMetrics.GpuTemperature))
        {
            Add(MetricTypes.GpuTemperature, record.GpuTemperature, record.GpuDeviceId);
        }

        if (sampled.Includes(HardwareMetrics.GpuComputeUsage))
        {
            Add(MetricTypes.GpuComputeUsage, record.GpuComputeUsagePercent, record.GpuDeviceId);
        }

        if (sampled.Includes(HardwareMetrics.GpuMemoryUsage))
        {
            Add(MetricTypes.GpuMemoryUsage, record.GpuMemoryUsagePercent, record.GpuDeviceId);

            // Raw VRAM capacity is retained only when the GPU actually reports it, so absent
            // sensors do not accumulate meaningless NULL rows.
            if (record.GpuMemoryUsedMb is not null)
            {
                Add(MetricTypes.GpuMemoryUsedMb, record.GpuMemoryUsedMb, record.GpuDeviceId);
            }

            if (record.GpuMemoryTotalMb is not null)
            {
                Add(MetricTypes.GpuMemoryTotalMb, record.GpuMemoryTotalMb, record.GpuDeviceId);
            }
        }

        if (sampled.Includes(HardwareMetrics.GpuMemoryTemperature))
        {
            Add(MetricTypes.GpuMemoryTemperature, record.GpuMemoryTemperature, record.GpuDeviceId);
        }

        if (sampled.Includes(HardwareMetrics.GpuFan))
        {
            // 0 RPM is a valid reading (zero-fan idle mode), not a missing sensor, and is stored
            // as 0 rather than NULL.
            Add(MetricTypes.GpuFanRpm, record.GpuFanRpm, record.GpuDeviceId);
        }

        return readings;
    }

    private readonly record struct MetricReading(string MetricType, string? DeviceId, double? Value);
}

/// <summary>Retention window for history rows. MVP uses Unlimited.</summary>
public sealed record HistoryRetentionPolicy(TimeSpan? MaxAge)
{
    public static HistoryRetentionPolicy Unlimited { get; } = new((TimeSpan?)null);

    public static HistoryRetentionPolicy Days(int days) => new(TimeSpan.FromDays(days));
}
