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
    List<AppHit> _apps = new();
    IReadOnlyList<AppEntry> _catalog = Array.Empty<AppEntry>();
    IReadOnlyDictionary<string, LaunchMemory> _usage = new Dictionary<string, LaunchMemory>();
    IReadOnlyList<string> _excludedApps = Array.Empty<string>();
    bool _launchEnabled;
    DateTime _usageUtc = DateTime.UnixEpoch;
    string _query = "";
    LoadSortMode _sort = LoadSortMode.Recent;

    public SwitcherSession(IEnumerable<WindowEntry> windows)
    {
        _all = windows.ToList();
        _filtered = _all.ToList();
        SelectedIndex = _filtered.Count > 0 ? 0 : -1;
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
        next %= count;
        if (next < 0) next += count;
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
        _query += text;
        Recompute(resetSelection: true);
    }

    public void Backspace()
    {
        if (_query.Length == 0) return;
        _query = _query[..^1];
        Recompute(resetSelection: true);
    }

    public LoadSortMode SortMode => _sort;
    public TaskLoad LoadFor(long handle) => _loads.TryGetValue(handle, out var load) ? load : default;

    public void SetSortMode(LoadSortMode mode)
    {
        if (!Enum.IsDefined(mode)) mode = LoadSortMode.Recent;
        if (mode == _sort) return;
        _sort = mode;
        Recompute(resetSelection: false);
    }

    public void UpdateLoads(IReadOnlyDictionary<long, TaskLoad> loads)
    {
        _loads.Clear();
        foreach (var pair in loads)
            _loads[pair.Key] = pair.Value;
        if (_query.Length == 0 && _sort != LoadSortMode.Recent)
            Recompute(resetSelection: false);
    }

    public void SetQuery(string? query)
    {
        var next = query ?? "";
        if (next == _query) return;
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

    void Recompute(bool resetSelection)
    {
        var selectedHandle = Selected?.Handle ?? 0;
        var selectedApp = SelectedApp?.App.Id;
        _filtered = SearchMatcher.Rank(_all, _query, e => e.Title, e => e.DisplayProcessName, e => e.OriginalIndex);
        if (_query.Length == 0 && _sort != LoadSortMode.Recent)
        {
            _filtered = LoadSorter.Sort(
                _filtered,
                _sort,
                e => e.OriginalIndex,
                e => LoadFor(e.Handle).ProcessCount > 0 ? LoadFor(e.Handle).CpuPercent : null,
                e => LoadFor(e.Handle).WorkingSetBytes);
        }
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
}
