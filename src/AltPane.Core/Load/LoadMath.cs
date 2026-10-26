namespace AltPane.Core.Load;

public readonly record struct SystemLoad(double? CpuPercent, long MemoryUsedBytes, long MemoryTotalBytes);

public readonly record struct TaskLoad(double? CpuPercent, long WorkingSetBytes, long PrivateBytes, int ProcessCount);

public readonly record struct ProcessSample(
    int ProcessId,
    int ParentProcessId,
    long CpuTicks,
    long WorkingSetBytes,
    long PrivateBytes,
    string Name = "");

public enum LoadHeat
{
    Calm = 0,
    Warm = 1,
    Hot = 2
}

/// <summary>
/// CPU percentages use 100-nanosecond ticks for both processor time and wall time.
/// The result is a share of the whole machine, from 0 to 100.
/// </summary>
public static class CpuMath
{
    public const long MinIntervalTicks = 50L * TimeSpan.TicksPerMillisecond;

    public static double? Percent(long previousCpuTicks, long currentCpuTicks, long previousWallTicks, long currentWallTicks, int processorCount)
    {
        if (processorCount < 1) return null;
        var wall = currentWallTicks - previousWallTicks;
        var cpu = currentCpuTicks - previousCpuTicks;
        if (wall < MinIntervalTicks || cpu < 0) return null;
        var ratio = (double)cpu / wall / processorCount * 100.0;
        if (double.IsNaN(ratio) || double.IsInfinity(ratio)) return null;
        return Math.Clamp(ratio, 0, 100);
    }

    public static double? SystemPercent(
        long previousIdle,
        long previousKernel,
        long previousUser,
        long idle,
        long kernel,
        long user,
        long previousWallTicks,
        long currentWallTicks,
        int processorCount)
    {
        var previousBusy = previousKernel + previousUser - previousIdle;
        var busy = kernel + user - idle;
        if (previousBusy < 0) previousBusy = 0;
        if (busy < previousBusy) return null;
        return Percent(previousBusy, busy, previousWallTicks, currentWallTicks, processorCount);
    }
}

public static class LoadFormat
{
    public static string Cpu(double? percent)
    {
        if (percent is null) return "…";
        var value = Math.Clamp(percent.Value, 0, 100);
        if (value >= 10)
            return $"{Math.Round(value, 0, MidpointRounding.AwayFromZero):0}%";
        return $"{Math.Round(value, 1, MidpointRounding.AwayFromZero):0.0}%";
    }

    public static string Bytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        const double kb = 1024d;
        var gb = bytes / (kb * kb * kb);
        if (gb >= 1)
            return $"{Math.Round(gb, 1, MidpointRounding.AwayFromZero):0.0} GB";
        var mb = bytes / (kb * kb);
        if (mb >= 100)
            return $"{Math.Round(mb, 0, MidpointRounding.AwayFromZero):0} MB";
        return $"{Math.Round(mb, 1, MidpointRounding.AwayFromZero):0.0} MB";
    }

    public static string MemoryPair(long used, long total) => $"{Bytes(used)} / {Bytes(total)}";

    public static string Row(in TaskLoad load)
    {
        if (load.ProcessCount <= 0) return "—";
        return $"{Cpu(load.CpuPercent)} · {Bytes(load.WorkingSetBytes)}";
    }
}

public static class LoadHeatMap
{
    public const double WarmCpu = 8;
    public const double HotCpu = 25;
    public const long WarmMemory = 400L * 1024 * 1024;
    public const long HotMemory = 1500L * 1024 * 1024;

    public static LoadHeat Classify(double? cpuPercent, long workingSetBytes)
    {
        var cpu = cpuPercent ?? 0;
        if (cpu >= HotCpu || workingSetBytes >= HotMemory) return LoadHeat.Hot;
        if (cpu >= WarmCpu || workingSetBytes >= WarmMemory) return LoadHeat.Warm;
        return LoadHeat.Calm;
    }
}
