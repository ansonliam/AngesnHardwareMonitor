using System.Runtime.InteropServices;

namespace HardwareWidget.Services;

public interface ISystemIdleTimeProvider
{
    /// <summary>How long since the last keyboard or mouse input, machine-wide.</summary>
    TimeSpan GetIdleTime();
}

/// <summary>
/// Machine-wide input idle time, same approach as the AI Usage Monitor's provider.
/// </summary>
public sealed class SystemIdleTimeProvider : ISystemIdleTimeProvider
{
    public TimeSpan GetIdleTime()
    {
        var lastInputInfo = new LastInputInfo
        {
            cbSize = (uint)Marshal.SizeOf<LastInputInfo>(),
        };

        if (!GetLastInputInfo(ref lastInputInfo))
        {
            // Treat an unreadable value as "active", so a failure here can never leave the widget
            // stuck on the slow idle cadence.
            return TimeSpan.Zero;
        }

        // Both values use the same 32-bit millisecond clock. Unsigned subtraction also handles the
        // clock wrapping roughly every 49.7 days.
        var elapsedMilliseconds = unchecked((uint)Environment.TickCount - lastInputInfo.dwTime);
        return TimeSpan.FromMilliseconds(elapsedMilliseconds);
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }
}
