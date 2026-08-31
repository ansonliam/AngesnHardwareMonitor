using System.Diagnostics.Eventing.Reader;

namespace AngesnHardwareWidget.Services;

/// <summary>
/// Watches the Windows System log for new warning/error/critical WHEA-Logger events. Existing
/// historical events establish the baseline and do not light the widget on every startup; once a
/// new hardware error occurs during this app session, the warning stays visible for that session.
/// </summary>
public sealed class WheaHardwareErrorMonitor
{
    private const string WheaQuery =
        "*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and (Level=1 or Level=2 or Level=3)]]";

    private long? _lastRecordId;
    private bool _baselineEstablished;
    private bool _warnedAboutReadFailure;

    public WheaHardwareErrorMonitor() => EstablishBaseline();

    public bool HasHardwareError { get; private set; }

    public bool Poll()
    {
        if (HasHardwareError)
        {
            return true;
        }

        try
        {
            var latestRecordId = ReadLatestRecordId();
            if (!_baselineEstablished)
            {
                _lastRecordId = latestRecordId;
                _baselineEstablished = true;
                return false;
            }

            if (latestRecordId is not { } current || current <= (_lastRecordId ?? 0))
            {
                return false;
            }

            _lastRecordId = current;
            HasHardwareError = true;
            AppLog.Warn($"New WHEA hardware error detected in the Windows System log (record {current}).");
            return true;
        }
        catch (Exception exception)
        {
            if (!_warnedAboutReadFailure)
            {
                _warnedAboutReadFailure = true;
                AppLog.Warn($"Could not monitor WHEA hardware errors: {exception.GetType().Name}: {exception.Message}");
            }

            return false;
        }
    }

    private void EstablishBaseline()
    {
        try
        {
            _lastRecordId = ReadLatestRecordId();
            _baselineEstablished = true;
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Could not establish the WHEA event baseline: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static long? ReadLatestRecordId()
    {
        var query = new EventLogQuery("System", PathType.LogName, WheaQuery)
        {
            ReverseDirection = true,
            TolerateQueryErrors = true,
        };

        using var reader = new EventLogReader(query);
        using var record = reader.ReadEvent();
        return record?.RecordId;
    }
}
