namespace AltPane;

/// <summary>
/// One timestamp from the hotkey (or the tray) until the switcher is visible.
/// A repeat does not move the start. A quick tap that never shows clears it.
/// </summary>
internal static class OpenTiming
{
    static long _start;

    public static void Mark()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        Interlocked.CompareExchange(ref _start, now, 0);
    }

    public static bool TryConsume(out long milliseconds)
    {
        var start = Interlocked.Exchange(ref _start, 0);
        if (start == 0)
        {
            milliseconds = 0;
            return false;
        }

        var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
        milliseconds = (long)(elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        if (milliseconds < 0) milliseconds = 0;
        return true;
    }

    public static void Clear() => Interlocked.Exchange(ref _start, 0);
}
