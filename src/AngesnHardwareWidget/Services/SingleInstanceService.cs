using System.IO;
using System.IO.Pipes;
using System.Text;

namespace AngesnHardwareWidget.Services;

/// <summary>
/// Keeps one widget per session, and lets a second launch bring the running one to the front.
///
/// This matters more than a tidy taskbar. Two instances would each run their own polling loop and
/// each write to the same history database, so every metric would get duplicate rows at slightly
/// different timestamps -- quietly corrupting the data being collected for future charts. The
/// Scheduled Task's MultipleInstancesPolicy only stops the *task* double-starting; it does nothing
/// about the task's instance coexisting with a manual launch, which is exactly what "Start with
/// Windows" makes likely.
///
/// A second instance signals the first over a named pipe and exits. Without that, double-clicking
/// the executable while the widget is hidden in the tray would appear to do nothing at all.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\AngesnHardwareWidget.PrimaryInstance.v1";
    private const string PipeName = "AngesnHardwareWidget.Activate.v1";
    private const string ActivateMessage = "show";

    private readonly CancellationTokenSource _listenerCancellation = new();

    private Mutex? _mutex;
    private bool _ownsMutex;
    private Task? _listener;

    /// <summary>
    /// True if this process is the primary instance. The mutex is session-local, which is the right
    /// scope: every instance of this app is elevated and runs in the user's own session.
    /// </summary>
    public bool TryAcquirePrimaryInstance()
    {
        try
        {
            // If a previous instance was killed, its handle closed and the kernel object went with
            // it, so this correctly reports a new mutex rather than staying wedged forever.
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            _ownsMutex = createdNew;
            return createdNew;
        }
        catch (Exception exception)
        {
            // If the mutex cannot be created at all, running is better than refusing to start.
            AppLog.Warn($"Single-instance check failed, continuing as primary: {exception.Message}");
            _ownsMutex = false;
            return true;
        }
    }

    /// <summary>
    /// Listens for later launches and invokes <paramref name="onActivate"/> for each. Only the
    /// primary instance should call this.
    /// </summary>
    public void StartListening(Action onActivate)
    {
        _listener = Task.Run(() => ListenAsync(onActivate, _listenerCancellation.Token));
    }

    /// <summary>
    /// Tells the already-running instance to show itself. Best effort: if the pipe cannot be
    /// reached the second instance still exits, because the alternative is two widgets.
    /// </summary>
    public void SignalExistingInstance()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);

            // Short timeout: the primary is already running, so this either connects promptly or
            // something is wrong and there is nothing useful to wait for.
            pipe.Connect(2000);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(ActivateMessage);

            AppLog.Info("Another instance is already running; asked it to show the widget and exiting.");
        }
        catch (Exception exception)
        {
            AppLog.Warn($"Could not signal the running instance: {exception.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            _listenerCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        // Not awaited: the listener is parked in a blocking accept, and cancelling the token plus
        // process exit is enough to tear it down. Waiting here would risk hanging shutdown.
        _listener = null;

        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex?.Dispose();
        _mutex = null;
        _ownsMutex = false;

        _listenerCancellation.Dispose();
    }

    private static async Task ListenAsync(Action onActivate, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // One server instance per connection, recreated each time round: simplest thing
                // that correctly handles repeated launches.
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                if (string.Equals(message, ActivateMessage, StringComparison.OrdinalIgnoreCase))
                {
                    onActivate();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                AppLog.Warn($"Instance listener error: {exception.Message}");

                // Back off briefly so a persistent fault cannot spin this loop.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
