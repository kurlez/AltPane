using AltPane.Core.Models;

namespace AltPane.Core.Switcher;

/// <summary>
/// Stable grouped order. Row 0 is the window that was active just before the one
/// in front when the switcher opened, so Alt+Tab and Enter return to it. The
/// foreground window stays in the list, inside its application group, at its
/// recent-use place in that group. Hiding it would drop the window the user was
/// just looking at; native Alt+Tab keeps it too.
/// Other windows are grouped by <see cref="InitialOrder.AppName"/>. Groups follow
/// that name's initial (Latin A–Z, Chinese by pinyin, then digits and symbols)
/// and, for the same initial, the name itself, so a group's place does not jump
/// between opens. Inside a group, most recent use wins, then Z-order
/// (<see cref="WindowEntry.OriginalIndex"/>, smaller is closer to the front).
/// The pinned row is the one exception to contiguity: the rest of its application
/// stays in the alphabetical group.
/// </summary>
public static class StableGroupedOrder
{
    public static List<WindowEntry> Arrange(
        IReadOnlyList<WindowEntry> items,
        long foregroundHandle,
        Func<long, int>? mruRank)
    {
        var ordered = Group(items, foregroundHandle, mruRank);
        var previous = Previous(items, foregroundHandle, mruRank);
        if (previous is null) return ordered;
        var index = ordered.FindIndex(window => window.Handle == previous.Handle);
        if (index <= 0) return ordered;
        ordered.RemoveAt(index);
        ordered.Insert(0, previous);
        return ordered;
    }

    /// <summary>Grouped order before the previous window is pulled to row 0.</summary>
    public static List<WindowEntry> Group(
        IReadOnlyList<WindowEntry> items,
        long foregroundHandle,
        Func<long, int>? mruRank)
    {
        var groups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            var name = InitialOrder.AppName(items[i]);
            if (!groups.TryGetValue(name, out var list))
            {
                list = new List<int>();
                groups[name] = list;
                names.Add(name);
            }
            list.Add(i);
        }

        foreach (var list in groups.Values)
        {
            list.Sort((a, b) => CompareInside(items, a, b, foregroundHandle, mruRank));
        }

        names.Sort(static (a, b) =>
        {
            var byInitial = InitialOrder.Compare(InitialOrder.OfName(a, 0), InitialOrder.OfName(b, 0));
            if (byInitial != 0) return byInitial;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });

        var ordered = new List<WindowEntry>(items.Count);
        foreach (var name in names)
        {
            foreach (var index in groups[name])
                ordered.Add(items[index]);
        }
        return ordered;
    }

    /// <summary>
    /// Pure most-recent order. The foreground window is first. Everyone else follows
    /// the activation history, then Z-order. No groups and no pinned row.
    /// </summary>
    public static List<WindowEntry> ByRecent(
        IReadOnlyList<WindowEntry> items,
        long foregroundHandle,
        Func<long, int>? mruRank)
    {
        var order = new List<int>(items.Count);
        for (var i = 0; i < items.Count; i++) order.Add(i);
        order.Sort((a, b) =>
        {
            var rank = RecentRank(items[a], foregroundHandle, mruRank)
                .CompareTo(RecentRank(items[b], foregroundHandle, mruRank));
            if (rank != 0) return rank;
            var z = items[a].OriginalIndex.CompareTo(items[b].OriginalIndex);
            if (z != 0) return z;
            return a.CompareTo(b);
        });
        var result = new List<WindowEntry>(order.Count);
        foreach (var index in order)
            result.Add(items[index]);
        return result;
    }

    /// <summary>
    /// A heading on the first visible row of each contiguous application run.
    /// Empty when the name is blank, so a row without an application stays compact.
    /// </summary>
    public static string[] Sections(IReadOnlyList<WindowEntry> filtered)
    {
        var sections = new string[filtered.Count];
        for (var i = 0; i < filtered.Count; i++)
        {
            var name = InitialOrder.AppName(filtered[i]);
            if (string.IsNullOrWhiteSpace(name)
                || (i > 0 && string.Equals(name, InitialOrder.AppName(filtered[i - 1]), StringComparison.OrdinalIgnoreCase)))
            {
                sections[i] = "";
                continue;
            }
            sections[i] = name;
        }
        return sections;
    }

    /// <summary>
    /// Stable grouped already starts on the previous window, so a forward open
    /// stays on row 0. A reverse chord selects the last row. Other modes return
    /// null and the caller applies its usual move.
    /// </summary>
    public static int? OpeningIndex(LoadSortMode mode, int count, int delta)
    {
        if (mode != LoadSortMode.StableGrouped) return null;
        if (count <= 0) return -1;
        return delta < 0 ? count - 1 : 0;
    }

    static WindowEntry? Previous(
        IReadOnlyList<WindowEntry> items,
        long foregroundHandle,
        Func<long, int>? mruRank)
    {
        WindowEntry? best = null;
        var bestRank = int.MaxValue;
        var bestZ = int.MaxValue;
        var bestInput = int.MaxValue;
        for (var i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            if (foregroundHandle != 0 && entry.Handle == foregroundHandle) continue;
            var rank = Mru(entry.Handle, mruRank);
            var z = entry.OriginalIndex;
            var better = best is null
                || rank < bestRank
                || (rank == bestRank && z < bestZ)
                || (rank == bestRank && z == bestZ && i < bestInput);
            if (!better) continue;
            best = entry;
            bestRank = rank;
            bestZ = z;
            bestInput = i;
        }
        return best;
    }

    static int CompareInside(
        IReadOnlyList<WindowEntry> items,
        int left,
        int right,
        long foregroundHandle,
        Func<long, int>? mruRank)
    {
        var rank = RecentRank(items[left], foregroundHandle, mruRank)
            .CompareTo(RecentRank(items[right], foregroundHandle, mruRank));
        if (rank != 0) return rank;
        var z = items[left].OriginalIndex.CompareTo(items[right].OriginalIndex);
        if (z != 0) return z;
        return left.CompareTo(right);
    }

    static int RecentRank(WindowEntry entry, long foregroundHandle, Func<long, int>? mruRank)
    {
        if (foregroundHandle != 0 && entry.Handle == foregroundHandle) return -1;
        return Mru(entry.Handle, mruRank);
    }

    static int Mru(long handle, Func<long, int>? mruRank)
    {
        if (mruRank is null) return int.MaxValue;
        var rank = mruRank(handle);
        return rank < 0 ? int.MaxValue : rank;
    }
}
