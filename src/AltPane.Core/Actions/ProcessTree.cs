namespace AltPane.Core.Actions;

public readonly record struct ProcessNode(int ProcessId, int ParentProcessId, string Name);

public static class ProcessTree
{
    public static IReadOnlyList<int> KillOrder(int rootProcessId, IEnumerable<ProcessNode> nodes)
    {
        var children = new Dictionary<int, List<int>>();
        foreach (var node in nodes)
        {
            if (node.ProcessId <= 0 || node.ParentProcessId <= 0 || node.ParentProcessId == node.ProcessId)
                continue;
            if (!children.TryGetValue(node.ParentProcessId, out var list))
            {
                list = new List<int>();
                children[node.ParentProcessId] = list;
            }
            list.Add(node.ProcessId);
        }

        var order = new List<int>();
        var seen = new HashSet<int>();
        void Walk(int pid)
        {
            if (pid <= 0 || !seen.Add(pid)) return;
            if (children.TryGetValue(pid, out var kids))
            {
                foreach (var child in kids)
                    Walk(child);
            }
            order.Add(pid);
        }

        Walk(rootProcessId);
        return order;
    }

    public static IReadOnlyList<int> SelectTargets(int rootProcessId, string? rootName, IReadOnlyList<ProcessNode> nodes, bool includeTree)
    {
        if (!ProcessActionPolicy.Evaluate(rootName, rootProcessId).Allowed)
            return Array.Empty<int>();
        if (!includeTree)
            return new[] { rootProcessId };

        var names = new Dictionary<int, string>();
        var children = new Dictionary<int, List<int>>();
        foreach (var node in nodes)
        {
            if (node.ProcessId <= 0) continue;
            names[node.ProcessId] = node.Name;
            if (node.ParentProcessId <= 0 || node.ParentProcessId == node.ProcessId) continue;
            if (!children.TryGetValue(node.ParentProcessId, out var list))
            {
                list = new List<int>();
                children[node.ParentProcessId] = list;
            }
            list.Add(node.ProcessId);
        }
        if (!names.ContainsKey(rootProcessId))
            names[rootProcessId] = rootName ?? "";

        var targets = new List<int>();
        var seen = new HashSet<int>();
        void Walk(int pid, bool isRoot)
        {
            if (pid <= 0 || !seen.Add(pid)) return;
            names.TryGetValue(pid, out var name);
            if (!isRoot && !ProcessActionPolicy.Evaluate(name, pid).Allowed)
                return;
            if (children.TryGetValue(pid, out var kids))
            {
                foreach (var child in kids)
                    Walk(child, false);
            }
            targets.Add(pid);
        }

        Walk(rootProcessId, true);
        return targets;
    }
}

public sealed class KillArm
{
    public int ProcessId { get; private set; }
    public bool IncludeTree { get; private set; }
    public bool IsArmed => ProcessId > 0;

    public bool Ready(int processId, bool includeTree, bool confirm)
    {
        if (processId <= 0)
        {
            Clear();
            return false;
        }
        if (!confirm)
        {
            Clear();
            return true;
        }
        if (ProcessId == processId && IncludeTree == includeTree)
        {
            Clear();
            return true;
        }
        ProcessId = processId;
        IncludeTree = includeTree;
        return false;
    }

    public void Clear()
    {
        ProcessId = 0;
        IncludeTree = false;
    }
}
