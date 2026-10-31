using AltPane.Core.Models;

namespace AltPane.Core.Switcher;

/// <summary>
/// Focus-time order, the default. Row 0 is the most recent user-activated window
/// that is not in the short tier, other than the one in front, so Alt+Tab and
/// Enter return to it. The foreground window stays in the list.
/// The main tier is pinned apps and apps at or above the focus threshold, including
/// apps with no usage yet. Those windows follow most recent user activation, then
/// Z-order. A pin keeps an app in this tier; it does not jump ahead of a more
/// recently used window. The short tier (under the threshold, or demoted) is always
/// last, under the "rarely used" separator, and is also ordered by recent use.
/// A short-tier window never takes row 0 while a main-tier window exists.
/// With no activation history, both tiers follow Z-order.
/// A combined tile row sorts as recently as its more recently used window and
/// never takes the previous-window slot at row 0.
/// </summary>
public static class StableGroupedOrder
{
    public static List<WindowEntry> Arrange(
        IReadOnlyList<WindowEntry> items,
        long foregroundHandle,
        Func<long, int>? mruRank,
        Func<WindowEntry, UsageTier>? tierOf = null,
        Func<WindowEntry, bool>? isDemoted = null)
    {
        var ordered = Group(items, foregroundHandle, mruRank, tierOf);
        var previous = Previous(items, foregroundHandle, mruRank, entry => IsShort(entry, tierOf) || isDemoted?.Invoke(entry) == true);
        if (previous is null) return ordered;
        var index = ordered.FindIndex(window => window.Handle == previous.Handle);
        if (index <= 0) return ordered;
        ordered.RemoveAt(index);
        ordered.Insert(0, previous);
        return ordered;
    }

    /// <summary>Focus-time order before the previous window is pulled to row 0.</summary>
    public static List<WindowEntry> Group(
        IReadOnlyList<WindowEntry> items,
        long foregroundHandle,
        Func<long, int>? mruRank,
        Func<WindowEntry, UsageTier>? tierOf = null)
    {
        _ = foregroundHandle;
        var order = new List<int>(items.Count);
        for (var i = 0; i < items.Count; i++) order.Add(i);
        order.Sort((a, b) =>
        {
            var tier = Band(items[a], tierOf).CompareTo(Band(items[b], tierOf));
            if (tier != 0) return tier;
            var rank = UsageRank(items[a], mruRank).CompareTo(UsageRank(items[b], mruRank));
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
    /// Pure most-recent order. The foreground window is first. Everyone else follows
    /// the activation history, then Z-order. No tiers.
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
    /// A heading when the application name changes, and the rare-use label on the
    /// first short-tier row. Empty when the name is blank and the tier does not change.
    /// </summary>
    public static string[] Sections(IReadOnlyList<WindowEntry> filtered, Func<WindowEntry, UsageTier>? tierOf = null, string infrequentHeading = "")
    {
        var sections = new string[filtered.Count];
        for (var i = 0; i < filtered.Count; i++)
        {
            var name = InitialOrder.AppName(filtered[i]);
            var tier = tierOf?.Invoke(filtered[i]) ?? UsageTier.Frequent;
            var previousTier = i > 0 && tierOf is not null ? tierOf(filtered[i - 1]) : (UsageTier?)null;
            var startsInfrequent = tier == UsageTier.Infrequent && previousTier != UsageTier.Infrequent && infrequentHeading.Length > 0;
            if (filtered[i].IsTilePair)
            {
                sections[i] = startsInfrequent ? infrequentHeading : "";
                continue;
            }
            var sameGroup = i > 0 && string.Equals(name, InitialOrder.AppName(filtered[i - 1]), StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(name) || sameGroup)
            {
                sections[i] = startsInfrequent ? infrequentHeading : "";
                continue;
            }
            sections[i] = startsInfrequent ? infrequentHeading + "  ·  " + name : name;
        }
        return sections;
    }

    /// <summary>
    /// Focus time already starts on the previous window, so a forward open
    /// stays on row 0. A reverse chord selects the last row. Other modes return
    /// null and the caller applies its usual move.
    /// </summary>
    public static int? OpeningIndex(LoadSortMode mode, int count, int delta)
    {
        if (mode != LoadSortMode.FocusTime) return null;
        if (count <= 0) return -1;
        return delta < 0 ? count - 1 : 0;
    }

    static bool IsShort(WindowEntry entry, Func<WindowEntry, UsageTier>? tierOf) =>
        tierOf is not null && tierOf(entry) == UsageTier.Infrequent;

    static int Band(WindowEntry entry, Func<WindowEntry, UsageTier>? tierOf) =>
        IsShort(entry, tierOf) ? 1 : 0;

    static WindowEntry? Previous(
        IReadOnlyList<WindowEntry> items,
        long foregroundHandle,
        Func<long, int>? mruRank,
        Func<WindowEntry, bool>? skip)
    {
        WindowEntry? best = null;
        var bestRank = int.MaxValue;
        var bestZ = int.MaxValue;
        var bestInput = int.MaxValue;
        for (var i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            if (entry.IsTilePair) continue;
            if (foregroundHandle != 0 && entry.Handle == foregroundHandle) continue;
            if (skip?.Invoke(entry) == true) continue;
            var rank = UsageRank(entry, mruRank);
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

    static int RecentRank(WindowEntry entry, long foregroundHandle, Func<long, int>? mruRank)
    {
        if (!entry.IsTilePair && foregroundHandle != 0 && entry.Handle == foregroundHandle) return -1;
        return UsageRank(entry, mruRank);
    }

    /// <summary>
    /// A combined row uses the more recent of its two windows, so it sits with
    /// that window instead of at the bottom with an unknown handle.
    /// </summary>
    static int UsageRank(WindowEntry entry, Func<long, int>? mruRank)
    {
        if (!entry.IsTilePair) return RankHandle(entry.Handle, mruRank);
        var left = RankHandle(entry.TileLeft, mruRank);
        var right = RankHandle(entry.TileRight, mruRank);
        return left < right ? left : right;
    }

    static int RankHandle(long handle, Func<long, int>? mruRank)
    {
        if (mruRank is null) return int.MaxValue;
        var rank = mruRank(handle);
        return rank < 0 ? int.MaxValue : rank;
    }
}
