namespace AltPane;

internal static class ProcessFootprint
{
    static Timer? _trim;

    /// <summary>Return idle pages a few seconds after the switcher hides, off the UI thread.</summary>
    public static void ScheduleTrim()
    {
        _trim ??= new Timer(_ =>
        {
            try
            {
                TrimOnce();
                AppLog.Info($"working set idle {Megabytes()} MB, private {PrivateMegabytes()} MB");
            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
            }
        }, null, Timeout.Infinite, Timeout.Infinite);
        try { _trim.Change(TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan); } catch { }
    }

    public static void Trim()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), (nint)(-1), (nint)(-1));
    }

    /// <summary>One compacting collection after a file-index scan, then return idle pages.</summary>
    public static void TrimOnce()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), (nint)(-1), (nint)(-1));
    }

    public static long Megabytes()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64 / (1024 * 1024);
    }

    public static long PrivateMegabytes()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        process.Refresh();
        return process.PrivateMemorySize64 / (1024 * 1024);
    }
}
