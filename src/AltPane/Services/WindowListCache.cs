using System.Diagnostics;
using System.Runtime.InteropServices;
using AltPane.Core.Apps;
using AltPane.Core.Filtering;
using AltPane.Core.Models;
using AltPane.Core.Switcher;

namespace AltPane;

/// <summary>
/// Keeps the Alt+Tab list warm on a background thread. WinEvent callbacks only
/// enqueue a handle. The thread applies those edits and, when the queue overflows
/// or the list is old, falls back to EnumWindows. Opening clones this list.
/// </summary>
internal sealed class WindowListCache : IDisposable
{
    const int QueueCap = 512;
    const int FullScanIntervalMs = 15000;
    const int UsageSaveIntervalMs = 5000;
    const int ArmWindowMs = 2000;
    const uint EventForeground = 0x0003;
    const uint EventObjectCreate = 0x8000;
    const uint EventObjectHide = 0x8003;
    const uint EventObjectNameChange = 0x800C;

    readonly ActivationMru _history;
    readonly Func<AppSettings> _settings;
    readonly ForegroundDwell _dwell = new();
    readonly AppUsageTable _usage = new();
    readonly object _gate = new();
    readonly Queue<nint> _pending = new();
    readonly uint _self = (uint)Environment.ProcessId;
    readonly nint[] _hooks = new nint[3];
    List<WindowEntry> _windows = new();
    WinEventProc? _callback;
    Thread? _thread;
    volatile uint _threadId;
    volatile bool _stop;
    bool _ready;
    bool _stale = true;
    bool _usageDirty;
    bool _sidebarDirty = true;
    bool _sidebarReady;
    int _sidebarStamp;
    IReadOnlyList<FrequentApp> _sidebar = Array.Empty<FrequentApp>();
    IReadOnlyList<AppEntry>? _catalog;
    IReadOnlyList<AppEntry>? _appliedCatalog;
    long _armedHandle;
    long _armedAt;
    long _nextUsageSave;
    bool _includeOther;
    long _lastScan;
    long _retryAfter;
    bool _disposed;

    public WindowListCache(ActivationMru history, Func<AppSettings> settings)
    {
        _history = history;
        _settings = settings;
    }

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "AltPane.Windows"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public (List<WindowEntry> Entries, long EnumerationMilliseconds) Capture(AppSettings settings)
    {
        lock (_gate)
        {
            if (_ready && !_stale && _includeOther == settings.IncludeOtherVirtualDesktops)
                return (Clone(settings), 0);
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var list = WindowEnumerator.Enumerate(settings);
        var copy = CopyOf(list);
        lock (_gate)
        {
            _windows = copy;
            _includeOther = settings.IncludeOtherVirtualDesktops;
            _ready = true;
            _stale = false;
            _pending.Clear();
            _lastScan = Environment.TickCount64;
        }
        RememberSeen(copy);
        return (list, clock.ElapsedMilliseconds);
    }

    public void Invalidate()
    {
        lock (_gate) _stale = true;
    }

    /// <summary>The next foreground of this window counts as user-caused, because AltPane activated it.</summary>
    public void ArmActivation(long hwnd)
    {
        if (hwnd == 0) return;
        lock (_gate)
        {
            _armedHandle = hwnd;
            _armedAt = Environment.TickCount64;
        }
    }

    public PrioritySnapshot Priority(AppSettings settings) =>
        _usage.Snapshot(settings.PinnedApps, settings.DemotedApps, DateTime.UtcNow, settings.FocusThresholdMinutes);

    /// <summary>
    /// The sidebar ranked on the background thread. A settings change ranks again
    /// from memory. Icons are not decoded here.
    /// </summary>
    public IReadOnlyList<FrequentApp> SidebarFor(AppSettings settings)
    {
        if (settings is null || !settings.ShowFrequentSidebar)
            return Array.Empty<FrequentApp>();
        var stamp = SidebarStamp(settings);
        lock (_gate)
        {
            if (_sidebarReady && stamp == _sidebarStamp)
                return _sidebar;
        }
        var ranked = RankSidebar(settings);
        lock (_gate)
        {
            _sidebar = ranked;
            _sidebarStamp = stamp;
            _sidebarReady = true;
            _sidebarDirty = false;
        }
        return ranked;
    }

    public void RememberCatalog(IReadOnlyList<AppEntry>? apps)
    {
        Volatile.Write(ref _catalog, apps);
        _sidebarDirty = true;
    }

    public void RestampNames()
    {
        lock (_gate)
        {
            foreach (var entry in _windows)
                entry.DisplayProcessName = Friendly(entry);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop = true;
        var id = _threadId;
        if (id != 0)
            NativeMethods.PostThreadMessage(id, NativeMethods.WmQuit, 0, 0);
        try { _thread?.Join(TimeSpan.FromSeconds(2)); }
        catch (Exception ex) { AppLog.Error(ex); }
        FlushUsage();
    }

    void Loop()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        try
        {
            Install();
            try { _usage.Load(AppUsageTable.DefaultPath()); }
            catch (Exception ex) { AppLog.Error(ex); }
            try { MaintainSidebar(); }
            catch (Exception ex) { AppLog.Error(ex); }
            while (!_stop)
            {
                if (!Pump()) break;
                Drain();
                var now = Environment.TickCount64;
                if (Due(now))
                {
                    if (!FullScan())
                        _retryAfter = Environment.TickCount64 + 2000;
                    else
                        _retryAfter = 0;
                    if (!Pump()) break;
                    Drain();
                }
                if (_stop) break;
                ApplyDwell(_dwell.Poll(Environment.TickCount64));
                try { MaintainSidebar(); }
                catch (Exception ex) { AppLog.Error(ex); }
                MaybeSaveUsage();
                NativeMethods.MsgWaitForMultipleObjects(0, 0, false, 200, NativeMethods.QsAllInput);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
        finally
        {
            RemoveHooks();
            FlushUsage();
        }
    }

    void Install()
    {
        _callback = OnEvent;
        _hooks[0] = NativeMethods.SetWinEventHook(EventForeground, EventForeground, 0, _callback, 0, 0, 0);
        _hooks[1] = NativeMethods.SetWinEventHook(EventObjectCreate, EventObjectHide, 0, _callback, 0, 0, 0);
        _hooks[2] = NativeMethods.SetWinEventHook(EventObjectNameChange, EventObjectNameChange, 0, _callback, 0, 0, 0);
        if (_hooks[0] == 0)
            AppLog.Info("Foreground hook was not installed. Focus-time order falls back to Z-order until activations are recorded.");
        if (_hooks[1] == 0 || _hooks[2] == 0)
            AppLog.Info("Window list hooks were not installed. The switcher will refresh the list on a timer.");
    }

    void RemoveHooks()
    {
        for (var i = 0; i < _hooks.Length; i++)
        {
            if (_hooks[i] == 0) continue;
            NativeMethods.UnhookWinEvent(_hooks[i]);
            _hooks[i] = 0;
        }
    }

    void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        if (hwnd == 0 || _stop) return;
        if (eventType >= EventObjectCreate && (idObject != 0 || idChild != 0)) return;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == _self) return;
        if (eventType == EventForeground)
        {
            var now = Environment.TickCount64;
            var armed = TakeArm((long)hwnd, now);
            var user = UserActivation.IsUserCaused(time, LastInputTick(time), armed);
            ApplyDwell(_dwell.OnForeground((long)hwnd, now, user));
        }
        lock (_gate)
        {
            if (_pending.Count >= QueueCap)
            {
                _stale = true;
                _pending.Clear();
                return;
            }
            _pending.Enqueue(hwnd);
        }
    }

    bool Pump()
    {
        while (NativeMethods.PeekMessage(out var msg, 0, 0, 0, NativeMethods.PmRemove))
        {
            if (msg.message == NativeMethods.WmQuit)
            {
                _stop = true;
                return false;
            }
            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessage(ref msg);
        }
        return !_stop;
    }

    bool Due(long now)
    {
        if (_retryAfter != 0 && now < _retryAfter) return false;
        lock (_gate)
            return !_ready || _stale || now - _lastScan >= FullScanIntervalMs;
    }

    bool FullScan()
    {
        try
        {
            var settings = _settings();
            var list = WindowEnumerator.Enumerate(settings);
            var copy = CopyOf(list);
            lock (_gate)
            {
                _windows = copy;
                _includeOther = settings.IncludeOtherVirtualDesktops;
                _ready = true;
                _stale = false;
                _pending.Clear();
                _lastScan = Environment.TickCount64;
            }
            RememberSeen(copy);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
    }

    void Drain()
    {
        nint[] batch;
        lock (_gate)
        {
            if (_stale || _pending.Count == 0)
            {
                _pending.Clear();
                return;
            }
            batch = _pending.ToArray();
            _pending.Clear();
        }

        IReadOnlyList<LiveMonitor> monitors;
        try
        {
            monitors = MonitorUtil.Live();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            monitors = Array.Empty<LiveMonitor>();
        }

        var settings = _settings();
        foreach (var hwnd in batch)
        {
            int original;
            lock (_gate)
            {
                if (_stale) return;
                var index = _windows.FindIndex(item => item.Handle == (long)hwnd);
                if (index >= 0)
                {
                    original = _windows[index].OriginalIndex;
                }
                else
                {
                    original = 0;
                    foreach (var item in _windows)
                    {
                        if (item.OriginalIndex >= original) original = item.OriginalIndex + 1;
                    }
                }
            }

            var ok = false;
            WindowEntry? entry = null;
            try
            {
                ok = WindowEnumerator.TryRead(hwnd, settings, monitors, original, out entry);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
            }

            lock (_gate)
            {
                if (_stale) return;
                var current = _windows.FindIndex(item => item.Handle == (long)hwnd);
                if (!ok || entry is null)
                {
                    if (current >= 0) _windows.RemoveAt(current);
                    continue;
                }
                if (current >= 0) _windows[current] = entry;
                else _windows.Add(entry);
            }
        }
    }

    List<WindowEntry> Clone(AppSettings settings)
    {
        var list = new List<WindowEntry>(_windows.Count);
        foreach (var entry in _windows)
        {
            if (ProcessNames.IsExcluded(entry.ProcessName, settings.ExcludedProcesses)) continue;
            if (ProcessNames.IsExcluded(entry.HostedProcessName, settings.ExcludedProcesses)) continue;
            list.Add(entry.Copy());
        }
        return list;
    }

    static List<WindowEntry> CopyOf(List<WindowEntry> list)
    {
        var copy = new List<WindowEntry>(list.Count);
        foreach (var entry in list)
            copy.Add(entry.Copy());
        return copy;
    }

    bool TakeArm(long hwnd, long now)
    {
        lock (_gate)
        {
            if (_armedHandle != hwnd || now - _armedAt > ArmWindowMs) return false;
            _armedHandle = 0;
            return true;
        }
    }

    static uint LastInputTick(uint eventTick)
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!NativeMethods.GetLastInputInfo(ref info)) return unchecked(eventTick - 1000);
        return info.dwTime;
    }

    void ApplyDwell(DwellResult result)
    {
        if (result.UserActivated != 0)
        {
            _history.Note(result.UserActivated);
            Credit(result.UserActivated, TimeSpan.Zero);
        }
        if (result.Unsolicited != 0)
            Credit(result.Unsolicited, TimeSpan.Zero);
        if (result.FocusEnded != 0 && result.FocusMilliseconds > 0)
            Credit(result.FocusEnded, TimeSpan.FromMilliseconds(result.FocusMilliseconds));
    }

    void Credit(long hwnd, TimeSpan focus)
    {
        var described = Describe(hwnd);
        if (described.Key.Length == 0) return;
        var utc = DateTime.UtcNow;
        if (focus > TimeSpan.Zero)
        {
            _usage.NoteFocus(described.Key, focus, utc, described.Name, described.Path);
            _sidebarDirty = true;
        }
        else
        {
            _usage.NoteSeen(described.Key, utc);
        }
        _usageDirty = true;
    }

    readonly record struct WindowIdentity(string Key, string Name, string Path);

    WindowIdentity Describe(long hwnd)
    {
        lock (_gate)
        {
            foreach (var entry in _windows)
            {
                if (entry.Handle != hwnd) continue;
                var name = string.IsNullOrWhiteSpace(entry.DisplayProcessName) ? entry.ProcessName : entry.DisplayProcessName;
                return new WindowIdentity(AppIdentity.Key(entry), name ?? "", entry.IconPath ?? "");
            }
        }
        return new WindowIdentity(KeyFor(hwnd), "", "");
    }

    void MaintainSidebar()
    {
        ApplyCatalog();
        var settings = _settings();
        var stamp = SidebarStamp(settings);
        if (!_sidebarDirty && _sidebarReady && stamp == _sidebarStamp) return;
        var ranked = settings.ShowFrequentSidebar ? RankSidebar(settings) : Array.Empty<FrequentApp>();
        lock (_gate)
        {
            _sidebar = ranked;
            _sidebarStamp = stamp;
            _sidebarReady = true;
            _sidebarDirty = false;
        }
        if (ranked.Count > 0) WarmIcons(ranked);
    }

    void ApplyCatalog()
    {
        var catalog = Volatile.Read(ref _catalog);
        if (ReferenceEquals(catalog, _appliedCatalog)) return;
        _appliedCatalog = catalog;
        if (catalog is null) return;
        foreach (var app in catalog)
        {
            var key = ProcessNames.Normalize(app.ProcessName);
            var path = !string.IsNullOrWhiteSpace(app.ResolvedPath) ? app.ResolvedPath : app.LaunchPath;
            if (key.Length == 0 && !string.IsNullOrWhiteSpace(path))
                key = ProcessNames.Normalize(Path.GetFileNameWithoutExtension(path));
            var launch = app.Kind == AppKind.Shortcut && !string.IsNullOrWhiteSpace(app.LaunchPath)
                ? app.LaunchPath
                : path;
            _usage.RememberLaunch(key, app.Name, launch);
        }
        _sidebarDirty = true;
    }

    IReadOnlyList<FrequentApp> RankSidebar(AppSettings settings) =>
        FrequentApps.Rank(
            _usage.Export(),
            DateTime.UtcNow,
            FrequentApps.DefaultWindow,
            settings.FrequentSidebarCount,
            settings.SidebarPinnedApps,
            settings.SidebarHiddenApps,
            settings.DemotedApps,
            null);

    static void WarmIcons(IReadOnlyList<FrequentApp> apps)
    {
        foreach (var app in apps)
        {
            if (string.IsNullOrWhiteSpace(app.LaunchPath)) continue;
            try
            {
                if (IconLoader.Cached(app.LaunchPath, 0) is not null) continue;
                if (!IconLoader.TryDecode(app.LaunchPath, 0, out var pixels)) continue;
                IconLoader.Keep(app.LaunchPath, 0, pixels);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
            }
        }
    }

    static int SidebarStamp(AppSettings settings)
    {
        var hash = new HashCode();
        hash.Add(settings.ShowFrequentSidebar);
        hash.Add(settings.FrequentSidebarCount);
        AddStamp(ref hash, settings.SidebarPinnedApps);
        AddStamp(ref hash, settings.SidebarHiddenApps);
        AddStamp(ref hash, settings.DemotedApps);
        return hash.ToHashCode();
    }

    static void AddStamp(ref HashCode hash, IReadOnlyList<string>? items)
    {
        if (items is null) return;
        hash.Add(items.Count);
        foreach (var item in items)
            hash.Add(item, StringComparer.OrdinalIgnoreCase);
    }

    void RememberSeen(List<WindowEntry> windows)
    {
        var utc = DateTime.UtcNow;
        foreach (var entry in windows)
            _usage.NoteSeen(AppIdentity.Key(entry), utc);
        if (windows.Count > 0) _usageDirty = true;
    }

    string KeyFor(long hwnd)
    {
        lock (_gate)
        {
            foreach (var entry in _windows)
            {
                if (entry.Handle != hwnd) continue;
                return AppIdentity.Key(entry);
            }
        }
        try
        {
            NativeMethods.GetWindowThreadProcessId((nint)hwnd, out var pid);
            if (pid == 0) return "";
            using var process = Process.GetProcessById((int)pid);
            return ProcessNames.Normalize(process.ProcessName);
        }
        catch (Exception)
        {
            return "";
        }
    }

    void MaybeSaveUsage()
    {
        if (!_usageDirty) return;
        var now = Environment.TickCount64;
        if (now < _nextUsageSave) return;
        FlushUsage();
        _nextUsageSave = Environment.TickCount64 + UsageSaveIntervalMs;
    }

    void FlushUsage()
    {
        if (!_usageDirty) return;
        try
        {
            _usage.Save(AppUsageTable.DefaultPath());
            _usageDirty = false;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    static string Friendly(WindowEntry entry)
    {
        var process = string.IsNullOrWhiteSpace(entry.HostedProcessName) ? entry.ProcessName : entry.HostedProcessName;
        return AppDisplayNames.Resolve(process, entry.IconPath);
    }
}
