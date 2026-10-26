using System.Runtime.InteropServices;
using AltPane.Core.Load;

namespace AltPane;

internal readonly record struct LoadUpdate(SystemLoad System, IReadOnlyDictionary<int, TaskLoad> ByProcessId);

/// <summary>
/// Samples CPU and memory on the thread pool. It runs only while the switcher is open.
/// </summary>
internal sealed class LoadSampler : IDisposable
{
    static readonly long FreshTicks = TimeSpan.FromSeconds(2).Ticks;
    readonly object _gate = new();
    readonly int _processors = Math.Max(1, Environment.ProcessorCount);
    int[] _roots = Array.Empty<int>();
    Dictionary<int, ProcessSample> _previous = new();
    long _previousWall;
    long _previousIdle;
    long _previousKernel;
    long _previousUser;
    Timer? _timer;
    int _generation;
    int _sampling;

    public event Action<LoadUpdate>? Updated;

    public void Watch(IReadOnlyCollection<int> roots)
    {
        lock (_gate)
            _roots = roots.Where(pid => pid > 0).Distinct().ToArray();
        if (_timer is not null) return;
        var generation = Volatile.Read(ref _generation);
        _timer = new Timer(_ => Tick(generation), null, 350, 800);
        ThreadPool.QueueUserWorkItem(_ => Tick(generation));
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();

    public static List<ProcessSample> CaptureProcesses()
    {
        var listed = ListProcesses();
        var samples = new List<ProcessSample>(listed.Count);
        foreach (var process in listed)
            samples.Add(new ProcessSample(process.ProcessId, process.ParentProcessId, 0, 0, 0, process.Name));
        return samples;
    }

    void Tick(int generation)
    {
        if (generation != Volatile.Read(ref _generation)) return;
        if (Interlocked.Exchange(ref _sampling, 1) == 1) return;
        try
        {
            int[] roots;
            lock (_gate) roots = _roots;
            var update = Sample(roots);
            if (generation != Volatile.Read(ref _generation)) return;
            Updated?.Invoke(update);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
        finally
        {
            Interlocked.Exchange(ref _sampling, 0);
        }
    }

    LoadUpdate Sample(IReadOnlyCollection<int> roots)
    {
        var wall = DateTime.UtcNow.Ticks;
        var listed = ListProcesses();
        var children = new Dictionary<int, List<int>>();
        var byId = new Dictionary<int, ListedProcess>();
        foreach (var process in listed)
        {
            byId[process.ProcessId] = process;
            if (process.ParentProcessId <= 0 || process.ParentProcessId == process.ProcessId) continue;
            if (!children.TryGetValue(process.ParentProcessId, out var kids))
            {
                kids = new List<int>();
                children[process.ParentProcessId] = kids;
            }
            kids.Add(process.ProcessId);
        }

        var needed = new HashSet<int>();
        var stack = new Stack<int>();
        foreach (var root in roots)
            if (root > 0) stack.Push(root);
        while (stack.Count > 0)
        {
            var pid = stack.Pop();
            if (!needed.Add(pid)) continue;
            if (children.TryGetValue(pid, out var kids))
            {
                foreach (var child in kids)
                    stack.Push(child);
            }
        }

        var current = new List<ProcessSample>(needed.Count);
        foreach (var pid in needed)
        {
            if (!byId.TryGetValue(pid, out var listedProcess)) continue;
            if (!TryReadProcess(pid, out var cpu, out var workingSet, out var privateBytes))
                continue;
            current.Add(new ProcessSample(pid, listedProcess.ParentProcessId, cpu, workingSet, privateBytes, listedProcess.Name));
        }

        var fresh = _previousWall != 0 && wall - _previousWall <= FreshTicks && wall > _previousWall;
        var previous = fresh ? _previous : new Dictionary<int, ProcessSample>();
        var loads = TaskLoadAggregator.ForWindows(roots, previous, current, fresh ? _previousWall : wall, wall, _processors);
        var system = ReadSystem(wall, fresh);
        _previous = current.ToDictionary(sample => sample.ProcessId);
        _previousWall = wall;
        return new LoadUpdate(system, loads);
    }

    SystemLoad ReadSystem(long wall, bool fresh)
    {
        long used = 0, total = 0;
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            total = (long)Math.Min(status.ullTotalPhys, long.MaxValue);
            var avail = (long)Math.Min(status.ullAvailPhys, long.MaxValue);
            used = Math.Max(0, total - avail);
        }

        double? cpu = null;
        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            if (fresh)
            {
                cpu = CpuMath.SystemPercent(
                    _previousIdle, _previousKernel, _previousUser,
                    idle, kernel, user,
                    _previousWall, wall, _processors);
            }
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
        }
        return new SystemLoad(cpu, used, total);
    }

    static bool TryReadProcess(int pid, out long cpu, out long workingSet, out long privateBytes)
    {
        cpu = 0;
        workingSet = 0;
        privateBytes = 0;
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (handle == 0) return false;
        try
        {
            if (NativeMethods.GetProcessTimes(handle, out _, out _, out var kernel, out var user))
                cpu = Math.Max(0, kernel) + Math.Max(0, user);
            var counters = new ProcessMemoryCounters { cb = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
            if (NativeMethods.GetProcessMemoryInfo(handle, ref counters, counters.cb))
            {
                workingSet = (long)counters.WorkingSetSize;
                privateBytes = (long)counters.PagefileUsage;
            }
            return true;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    static List<ListedProcess> ListProcesses()
    {
        var list = new List<ListedProcess>();
        var snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.Th32CsSnapProcess, 0);
        if (snapshot == 0 || snapshot == new nint(-1)) return list;
        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!NativeMethods.Process32First(snapshot, ref entry)) return list;
            do
            {
                var pid = unchecked((int)entry.th32ProcessID);
                if (pid > 0)
                {
                    list.Add(new ListedProcess(
                        pid,
                        unchecked((int)entry.th32ParentProcessID),
                        Path.GetFileNameWithoutExtension(entry.szExeFile ?? "")));
                }
                entry.dwSize = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (NativeMethods.Process32Next(snapshot, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }
        return list;
    }

    readonly record struct ListedProcess(int ProcessId, int ParentProcessId, string Name);
}
