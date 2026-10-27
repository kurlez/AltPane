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

    public static long Megabytes()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64 / (1024 * 1024);
    }
}
