using AltPane.Core.Models;

namespace AltPane.Core.Load;

public static class TaskLoadAggregator
{
    public static Dictionary<int, TaskLoad> ForWindows(
        IEnumerable<int> windowProcessIds,
        IReadOnlyDictionary<int, ProcessSample> previous,
        IReadOnlyList<ProcessSample> current,
        long previousWallTicks,
        long currentWallTicks,
        int processorCount)
    {
        var byPid = new Dictionary<int, ProcessSample>();
        foreach (var sample in current)
        {
            if (sample.ProcessId > 0)
                byPid[sample.ProcessId] = sample;
        }

        var children = new Dictionary<int, List<int>>();
        foreach (var sample in byPid.Values)
        {
            var parent = sample.ParentProcessId;
            if (parent <= 0 || parent == sample.ProcessId || !byPid.ContainsKey(parent)) continue;
            if (!children.TryGetValue(parent, out var list))
            {
                list = new List<int>();
                children[parent] = list;
            }
            list.Add(sample.ProcessId);
        }

        var owned = new HashSet<int>();
        foreach (var pid in windowProcessIds)
        {
            if (pid > 0) owned.Add(pid);
        }

        var result = new Dictionary<int, TaskLoad>();
        foreach (var pid in owned)
            result[pid] = RollUp(pid, byPid, children, owned, previous, previousWallTicks, currentWallTicks, processorCount);
        return result;
    }

    static TaskLoad RollUp(
        int root,
        Dictionary<int, ProcessSample> byPid,
        Dictionary<int, List<int>> children,
        HashSet<int> owned,
        IReadOnlyDictionary<int, ProcessSample> previous,
        long previousWallTicks,
        long currentWallTicks,
        int processorCount)
    {
        long cpu = 0;
        long workingSet = 0;
        long privateBytes = 0;
        var count = 0;
        var anyCpu = false;
        var seen = new HashSet<int>();

        void Walk(int pid, bool isRoot)
        {
            if (!seen.Add(pid)) return;
            if (!isRoot && owned.Contains(pid)) return;
            if (!byPid.TryGetValue(pid, out var sample)) return;
            count++;
            workingSet += NonNegative(sample.WorkingSetBytes);
            privateBytes += NonNegative(sample.PrivateBytes);
            if (previous.TryGetValue(pid, out var prior) && sample.CpuTicks >= prior.CpuTicks)
            {
                cpu += sample.CpuTicks - prior.CpuTicks;
                anyCpu = true;
            }
            if (!children.TryGetValue(pid, out var kids)) return;
            foreach (var child in kids)
                Walk(child, false);
        }

        Walk(root, true);
        var percent = anyCpu
            ? CpuMath.Percent(0, cpu, previousWallTicks, currentWallTicks, processorCount)
            : null;
        return new TaskLoad(percent, workingSet, privateBytes, count);
    }

    static long NonNegative(long value) => value < 0 ? 0 : value;
}

public static class LoadSorter
{
    public static List<T> Sort<T>(
        IReadOnlyList<T> items,
        LoadSortMode mode,
        Func<T, int> zOrder,
        Func<T, double?> cpu,
        Func<T, long> memory)
    {
        if (mode is LoadSortMode.Recent or LoadSortMode.Initial or LoadSortMode.FocusTime || items.Count < 2)
            return items.ToList();

        var ranked = new List<(T Item, double Cpu, long Memory, int Order)>(items.Count);
        foreach (var item in items)
        {
            var percent = cpu(item);
            ranked.Add((item, percent ?? -1, memory(item), zOrder(item)));
        }

        ranked.Sort(mode == LoadSortMode.Memory
            ? static (a, b) =>
            {
                var byMemory = b.Memory.CompareTo(a.Memory);
                return byMemory != 0 ? byMemory : a.Order.CompareTo(b.Order);
            }
            : static (a, b) =>
            {
                var byCpu = b.Cpu.CompareTo(a.Cpu);
                return byCpu != 0 ? byCpu : a.Order.CompareTo(b.Order);
            });

        var result = new List<T>(ranked.Count);
        foreach (var entry in ranked)
            result.Add(entry.Item);
        return result;
    }
}
