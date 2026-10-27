namespace AltPane.Core.Layout;

/// <summary>
/// Label for a window relative to the monitor that is drawing the switcher.
/// <see cref="DisplayBounds.Number"/> is the Windows Settings number: the trailing
/// index of <c>\\.\DISPLAYn</c> from <c>EnumDisplayMonitors</c> / <c>GetMonitorInfo</c>
/// when every monitor has a unique index, otherwise 1..n from left to right then top
/// to bottom (<see cref="MonitorLayout.AssignNumbers"/>). The same number is used on
/// every switcher instance.
/// </summary>
public static class MonitorBadge
{
    public static string Label(int windowMonitor, int hostMonitor, string thisScreen)
    {
        if (windowMonitor <= 0) return "";
        if (hostMonitor > 0 && windowMonitor == hostMonitor)
            return string.IsNullOrEmpty(thisScreen) ? windowMonitor.ToString() : thisScreen;
        return windowMonitor.ToString();
    }
}
