namespace AltPane;

internal static class ProcessFootprint
{
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
}
