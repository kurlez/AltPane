using AltPane.Core.Apps;
using AltPane.Core.Load;
using AltPane.Core.Models;
using AltPane.Core.Search;

namespace AltPane.Core.Switcher;

public sealed class SwitcherSession
{
    public const int PageSize = 8;
    readonly List<WindowEntry> _all;
    readonly Dictionary<long, TaskLoad> _loads = new();
    List<WindowEntry> _filtered;
    readonly List<long> _frozen = new();
    readonly Dictionary<long, string> _index = new();
    int _nextIndex;
    List<AppHit> _apps = new();
    IReadOnlyList<AppEntry> _catalog = Array.Empty<AppEntry>();
    IReadOnlyDictionary<string, LaunchMemory> _usage = new Dictionary<string, LaunchMemory>();
    IReadOnlyList<string> _excludedApps = Array.Empty<string>();
    bool _launchEnabled;
    DateTime _usageUtc = DateTime.UnixEpoch;
    string _query = "";
    long _queryAnchor;
    LoadSortMode _sort = LoadSortMode.Recent;

    public IReadOnlyDictionary<long, string> Indexes => _index;
    public string IndexForHandle(long handle) => _index.TryGetValue(handle, out var code) ? code : "";
    public string IndexForApp(int index) => RowIndex.Code(_nextIndex + Math.Max(0, index));
    int _monitorFilter;

    public SwitcherSession(IEnumerable<WindowEntry> windows)
    {
        _all = windows.ToList();
        _filtered = _all.ToList();
        SelectedIndex = _filtered.Count > 0 ? 0 : -1;
        CaptureOrder();
        Recompute(resetSelection: false);
    }

    public IReadOnlyList<WindowEntry> All => _all;
    public IReadOnlyList<WindowEntry> Filtered => _filtered;
    public IReadOnlyList<AppHit> AppHits => _apps;
    public int ResultCount => _filtered.Count + _apps.Count;
    public string Query => _query;
    public int SelectedIndex { get; private set; }
    public WindowEntry? Selected =>
        SelectedIndex >= 0 && SelectedIndex < _filtered.Count ? _filtered[SelectedIndex] : null;
    public AppHit? SelectedApp
    {
        get
        {
            var index = SelectedIndex - _filtered.Count;
            return index >= 0 && index < _apps.Count ? _apps[index] : null;
        }
    }

    public void Move(int delta)
    {
        var count = ResultCount;
        if (count == 0)
        {
            SelectedIndex = -1;
            return;
        }
        if (SelectedIndex < 0) SelectedIndex = 0;
        var next = SelectedIndex + delta;
        if (next < 0) next = 0;
        else if (next >= count) next = count - 1;
        SelectedIndex = next;
    }

    public void Page(int direction)
    {
        if (ResultCount == 0)
        {
            SelectedIndex = -1;
            return;
        }
        if (SelectedIndex < 0) SelectedIndex = 0;
        var delta = direction < 0 ? -PageSize : PageSize;
        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, ResultCount - 1);
    }

    public void SelectFirst() => SelectedIndex = ResultCount > 0 ? 0 : -1;
    public void SelectLast() => SelectedIndex = ResultCount > 0 ? ResultCount - 1 : -1;

    public bool SelectHandle(long handle)
    {
        var index = _filtered.FindIndex(w => w.Handle == handle);
        if (index < 0) return false;
        SelectedIndex = index;
        return true;
    }

    public bool SelectApp(string id)
    {
        var index = _apps.FindIndex(hit => string.Equals(hit.App.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        SelectedIndex = _filtered.Count + index;
        return true;
    }

    public void SetCatalog(
        IReadOnlyList<AppEntry>? apps,
        IReadOnlyDictionary<string, LaunchMemory>? usage,
        IReadOnlyList<string>? exclusions,
        bool enabled,
        DateTime? utcNow = null)
    {
        _catalog = apps ?? Array.Empty<AppEntry>();
        _usage = usage ?? new Dictionary<string, LaunchMemory>();
        _excludedApps = exclusions ?? Array.Empty<string>();
        _launchEnabled = enabled;
        _usageUtc = utcNow ?? DateTime.UtcNow;
        Recompute(resetSelection: false);
    }

    public void AppendQuery(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        NoteQueryStart(_query, _query + text);
        _query += text;
        Recompute(resetSelection: true);
    }

    /// <summary>
    /// Clears the query and selects the window that was current when the query became non-empty.
    /// Returns false when the query was already empty.
    /// </summary>
    public bool ClearQuery()
    {
        if (_query.Length == 0) return false;
        var anchor = _queryAnchor;
        _query = "";
        _queryAnchor = 0;
        Recompute(resetSelection: false);
        if (anchor != 0 && SelectHandle(anchor)) return true;
        SelectedIndex = ResultCount > 0 ? 0 : -1;
        return true;
    }

    public void Backspace()
    {
        if (_query.Length == 0) return;
        _query = _query[..^1];
        Recompute(resetSelection: true);
    }

    public int MonitorFilter => _monitorFilter;

    public void SetMonitorFilter(int number)
    {
        if (number < 0) number = 0;
        if (number == _monitorFilter) return;
        _monitorFilter = number;
        Recompute(resetSelection: false);
    }

    public LoadSortMode SortMode => _sort;
    public TaskLoad LoadFor(long handle) => _loads.TryGetValue(handle, out var load) ? load : default;

    public void SetSortMode(LoadSortMode mode)
    {
        if (!Enum.IsDefined(mode)) mode = LoadSortMode.Initial;
        if (mode == _sort) return;
        _sort = mode;
        CaptureOrder();
        Recompute(resetSelection: false);
    }

    /// <summary>
    /// Stores new CPU and memory figures. The row order captured at open, or by the last
    /// explicit sort change, stays put.
    /// </summary>
    public void UpdateLoads(IReadOnlyDictionary<long, TaskLoad> loads)
    {
        _loads.Clear();
        foreach (var pair in loads)
            _loads[pair.Key] = pair.Value;
    }

    /// <summary>
    /// A window that appears while the switcher is open is added at the end.
    /// Existing rows are not moved.
    /// </summary>
    public bool NoteArrived(WindowEntry entry)
    {
        if (entry is null || _all.Any(window => window.Handle == entry.Handle)) return false;
        _all.Add(entry);
        _frozen.Add(entry.Handle);
        _index[entry.Handle] = RowIndex.Code(_nextIndex++);
        Recompute(resetSelection: false);
        return true;
    }

    public void SetQuery(string? query)
    {
        var next = query ?? "";
        if (next == _query) return;
        NoteQueryStart(_query, next);
        if (next.Length == 0) _queryAnchor = 0;
        _query = next;
        Recompute(resetSelection: true);
    }

    public bool RemoveHandle(long handle)
    {
        var index = _all.FindIndex(w => w.Handle == handle);
        if (index < 0) return false;
        var selectedHandle = Selected?.Handle ?? 0;
        var selectedApp = SelectedApp?.App.Id;
        var selectedWasRemoved = selectedHandle == handle;
        var oldSelected = SelectedIndex;
        _all.RemoveAt(index);
        _frozen.Remove(handle);
        _index.Remove(handle);
        Recompute(resetSelection: false);
        if (!string.IsNullOrEmpty(selectedApp))
        {
            var keepApp = _apps.FindIndex(hit => string.Equals(hit.App.Id, selectedApp, StringComparison.OrdinalIgnoreCase));
            if (keepApp >= 0)
            {
                SelectedIndex = _filtered.Count + keepApp;
                return true;
            }
        }
        if (ResultCount == 0)
        {
            SelectedIndex = -1;
            return true;
        }
        if (_filtered.Count == 0)
        {
            SelectedIndex = 0;
            return true;
        }
        if (!selectedWasRemoved)
        {
            var keep = _filtered.FindIndex(w => w.Handle == selectedHandle);
            SelectedIndex = keep >= 0 ? keep : Math.Clamp(oldSelected, 0, _filtered.Count - 1);
        }
        else
        {
            SelectedIndex = Math.Clamp(oldSelected, 0, _filtered.Count - 1);
        }
        return true;
    }

    void NoteQueryStart(string previous, string next)
    {
        if (previous.Length == 0 && next.Length > 0)
            _queryAnchor = Selected?.Handle ?? 0;
    }

    void CaptureOrder()
    {
        var ordered = _sort == LoadSortMode.Initial
            ? InitialOrder.Sort(_all)
            : LoadSorter.Sort(
                _all,
                _sort,
                entry => entry.OriginalIndex,
                entry => LoadFor(entry.Handle).ProcessCount > 0 ? LoadFor(entry.Handle).CpuPercent : null,
                entry => LoadFor(entry.Handle).WorkingSetBytes);
        _frozen.Clear();
        _index.Clear();
        _nextIndex = 0;
        foreach (var entry in ordered)
        {
            _frozen.Add(entry.Handle);
            _index[entry.Handle] = RowIndex.Code(_nextIndex++);
        }
    }

    void Recompute(bool resetSelection)
    {
        var selectedHandle = Selected?.Handle ?? 0;
        var selectedApp = SelectedApp?.App.Id;
        var byHandle = new Dictionary<long, WindowEntry>(_all.Count);
        foreach (var entry in _all)
            byHandle[entry.Handle] = entry;
        var filtered = new List<WindowEntry>(_frozen.Count);
        foreach (var handle in _frozen)
        {
            if (!byHandle.TryGetValue(handle, out var entry)) continue;
            if (!SearchMatcher.Matches(_query, entry.Title, entry.DisplayProcessName)) continue;
            if (_monitorFilter > 0 && entry.MonitorNumber != _monitorFilter) continue;
            filtered.Add(entry);
        }
        var token = _query.Trim();
        var singleToken = token.Length > 0 && token.IndexOf(' ') < 0;
        long jumpHandle = 0;
        if (singleToken)
            jumpHandle = IncludeExactIndex(filtered, byHandle, token);
        _filtered = filtered;
        _apps = _launchEnabled && _query.Length > 0
            ? AppCatalog.Rank(_catalog, _query, _usage, _all, _filtered, _excludedApps, _usageUtc)
            : new List<AppHit>();
        if (ResultCount == 0)
        {
            SelectedIndex = -1;
            return;
        }
        if (resetSelection)
        {
            if (jumpHandle != 0)
            {
                var jumped = _filtered.FindIndex(window => window.Handle == jumpHandle);
                SelectedIndex = jumped >= 0 ? jumped : 0;
                return;
            }
            if (singleToken)
            {
                for (var i = 0; i < _apps.Count; i++)
                {
                    if (!RowIndex.Matches(token, IndexForApp(i))) continue;
                    SelectedIndex = _filtered.Count + i;
                    return;
                }
            }
            SelectedIndex = 0;
            return;
        }
        if (selectedHandle != 0)
        {
            var keep = _filtered.FindIndex(w => w.Handle == selectedHandle);
            if (keep >= 0)
            {
                SelectedIndex = keep;
                return;
            }
        }
        if (!string.IsNullOrEmpty(selectedApp))
        {
            var keepApp = _apps.FindIndex(hit => string.Equals(hit.App.Id, selectedApp, StringComparison.OrdinalIgnoreCase));
            if (keepApp >= 0)
            {
                SelectedIndex = _filtered.Count + keepApp;
                return;
            }
        }
        if (SelectedIndex >= ResultCount) SelectedIndex = ResultCount - 1;
        else if (SelectedIndex < 0) SelectedIndex = 0;
    }

    long IncludeExactIndex(List<WindowEntry> filtered, Dictionary<long, WindowEntry> byHandle, string token)
    {
        foreach (var pair in _index)
        {
            if (!RowIndex.Matches(token, pair.Value)) continue;
            if (!byHandle.TryGetValue(pair.Key, out var entry)) return 0;
            if (_monitorFilter > 0 && entry.MonitorNumber != _monitorFilter) return 0;
            if (filtered.Exists(window => window.Handle == pair.Key)) return pair.Key;
            var frozenPos = _frozen.IndexOf(pair.Key);
            var at = 0;
            for (var i = 0; i < filtered.Count; i++)
            {
                var otherPos = _frozen.IndexOf(filtered[i].Handle);
                if (otherPos >= 0 && otherPos < frozenPos) at = i + 1;
            }
            filtered.Insert(at, entry);
            return pair.Key;
        }
        return 0;
    }
}
