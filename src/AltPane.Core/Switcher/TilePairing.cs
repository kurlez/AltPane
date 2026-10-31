using AltPane.Core.Models;

namespace AltPane.Core.Switcher;

/// <summary>
/// A rectangle in the same pixel space as the monitor work area. Callers pass
/// physical pixels from <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> so the invisible
/// resize border is not part of the gap. DIP and physical pixels are not mixed.
/// </summary>
public readonly record struct TileRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public long Area => Width <= 0 || Height <= 0 ? 0 : (long)Width * Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// One visible window measured against the work area of the monitor it sits on.
/// <see cref="UsageRank"/> is lower for a more recently used window.
/// </summary>
public sealed record TileWindow(
    long Handle,
    int MonitorNumber,
    TileRect Frame,
    TileRect WorkArea,
    bool Minimized = false,
    bool Visible = true,
    bool Cloaked = false,
    int UsageRank = int.MaxValue);

/// <summary>
/// Two windows that share an edge. <see cref="First"/> is the left or top one,
/// <see cref="Second"/> the right or bottom one, and <see cref="FocusHandle"/>
/// is the one used more recently.
/// </summary>
public readonly record struct TilePair(long First, long Second, long FocusHandle, int MonitorNumber);

/// <summary>
/// Finds windows that are tiled on one monitor: left/right or top/bottom, or
/// adjacent cells of a larger snap (three columns, a 2×2 grid, a half plus two
/// quarters). Overlapping, tiny, minimized, cloaked, and off-screen windows are
/// left out. The tolerance scales with the work area so a few physical pixels
/// of border, at any DPI, still count as a shared edge.
/// </summary>
public static class TilePairing
{
    public const double MinAreaFraction = 0.18;
    public const double MaxAreaFraction = 0.88;
    public const double MinSideFraction = 0.18;
    public const double MinInsideFraction = 0.85;
    public const double MinCoverage = 0.80;
    public const double MinSharedEdge = 0.72;
    public const double MaxOverlapFraction = 0.08;

    /// <summary>
    /// About 2% of the shorter work-area side, and at least 8 physical pixels.
    /// At 1080p that is roughly 20 pixels; at 4K it grows with the work area.
    /// </summary>
    public static int EdgeTolerance(TileRect work)
    {
        var minor = Math.Min(Math.Max(0, work.Width), Math.Max(0, work.Height));
        if (minor <= 0) return 8;
        return Math.Max(8, minor / 50);
    }

    public static IReadOnlyList<TilePair> Find(IReadOnlyList<TileWindow>? windows)
    {
        if (windows is null || windows.Count < 2) return Array.Empty<TilePair>();
        var groups = new Dictionary<int, List<TileWindow>>();
        foreach (var window in windows)
        {
            if (!FlagsOk(window)) continue;
            if (!groups.TryGetValue(window.MonitorNumber, out var list))
            {
                list = new List<TileWindow>();
                groups[window.MonitorNumber] = list;
            }
            var existing = list.FindIndex(item => item.Handle == window.Handle);
            if (existing >= 0)
            {
                if (window.UsageRank < list[existing].UsageRank)
                    list[existing] = window;
                continue;
            }
            list.Add(window);
        }

        var found = new List<Found>();
        foreach (var group in groups.Values)
        {
            if (group.Count < 2) continue;
            var work = group[0].WorkArea;
            var tolerance = EdgeTolerance(work);
            var eligible = new List<TileWindow>();
            foreach (var window in group)
            {
                if (!SameWork(window.WorkArea, work, tolerance)) continue;
                if (!SizeOk(window, work)) continue;
                eligible.Add(window);
            }
            if (eligible.Count < 2) continue;

            var tiling = TilingSet(eligible, tolerance);
            if (tiling.Count < 2) continue;
            var fills = Coverage(tiling, work) >= MinCoverage;
            var emitted = new HashSet<(long, long)>();
            for (var i = 0; i < tiling.Count; i++)
            {
                for (var j = i + 1; j < tiling.Count; j++)
                {
                    if (!TryAdjacent(tiling[i], tiling[j], tolerance, out var first, out var second, out var top, out var left))
                        continue;
                    if (!fills && Coverage(new[] { tiling[i], tiling[j] }, work) < MinCoverage)
                        continue;
                    var key = first.Handle < second.Handle
                        ? (first.Handle, second.Handle)
                        : (second.Handle, first.Handle);
                    if (!emitted.Add(key)) continue;
                    found.Add(new Found(
                        first.Handle,
                        second.Handle,
                        FocusOf(first, second),
                        first.MonitorNumber,
                        top,
                        left));
                }
            }
        }

        found.Sort(static (a, b) =>
        {
            var monitor = a.Monitor.CompareTo(b.Monitor);
            if (monitor != 0) return monitor;
            var top = a.Top.CompareTo(b.Top);
            if (top != 0) return top;
            var left = a.Left.CompareTo(b.Left);
            if (left != 0) return left;
            return a.First.CompareTo(b.First);
        });
        var pairs = new TilePair[found.Count];
        for (var i = 0; i < found.Count; i++)
        {
            var item = found[i];
            pairs[i] = new TilePair(item.First, item.Second, item.Focus, item.Monitor);
        }
        return pairs;
    }

    static bool FlagsOk(TileWindow window) =>
        window.Handle != 0
        && window.MonitorNumber > 0
        && window.Visible
        && !window.Minimized
        && !window.Cloaked
        && !window.Frame.IsEmpty
        && !window.WorkArea.IsEmpty;

    static bool SizeOk(TileWindow window, TileRect work)
    {
        var frame = window.Frame;
        if (frame.IsEmpty || work.IsEmpty) return false;
        var inside = Intersect(frame, work);
        if (inside.IsEmpty || frame.Area <= 0 || work.Area <= 0) return false;
        if (inside.Area < frame.Area * MinInsideFraction) return false;
        if (inside.Area < work.Area * MinAreaFraction) return false;
        if (inside.Area > work.Area * MaxAreaFraction) return false;
        if (inside.Width < work.Width * MinSideFraction) return false;
        if (inside.Height < work.Height * MinSideFraction) return false;
        return true;
    }

    /// <summary>
    /// Drops windows that sit on top of the tiles. The window that overlaps the
    /// others the most is removed first, so two clean halves survive a window
    /// that covers both of them.
    /// </summary>
    static List<TileWindow> TilingSet(List<TileWindow> eligible, int tolerance)
    {
        var set = new List<TileWindow>(eligible);
        while (set.Count >= 2)
        {
            var worst = -1;
            long worstOverlap = 0;
            for (var i = 0; i < set.Count; i++)
            {
                long overlap = 0;
                for (var j = 0; j < set.Count; j++)
                {
                    if (i == j) continue;
                    if (!OverlapsTooMuch(set[i].Frame, set[j].Frame, tolerance)) continue;
                    overlap += IntersectionArea(set[i].Frame, set[j].Frame);
                }
                if (overlap > worstOverlap)
                {
                    worstOverlap = overlap;
                    worst = i;
                }
            }
            if (worst < 0) break;
            set.RemoveAt(worst);
        }
        return set;
    }

    static bool TryAdjacent(
        TileWindow a,
        TileWindow b,
        int tolerance,
        out TileWindow first,
        out TileWindow second,
        out int top,
        out int left)
    {
        first = a;
        second = b;
        top = 0;
        left = 0;
        if (OverlapsTooMuch(a.Frame, b.Frame, tolerance)) return false;
        var horizontal = HorizontalNeighbors(a, b, tolerance);
        var vertical = VerticalNeighbors(a, b, tolerance);
        if (!horizontal && !vertical) return false;
        var useHorizontal = horizontal && (!vertical || Math.Abs(HorizontalGap(a, b)) <= Math.Abs(VerticalGap(a, b)));
        if (useHorizontal)
        {
            if (a.Frame.Left < b.Frame.Left || (a.Frame.Left == b.Frame.Left && a.Frame.Top <= b.Frame.Top))
            {
                first = a;
                second = b;
            }
            else
            {
                first = b;
                second = a;
            }
        }
        else if (a.Frame.Top < b.Frame.Top || (a.Frame.Top == b.Frame.Top && a.Frame.Left <= b.Frame.Left))
        {
            first = a;
            second = b;
        }
        else
        {
            first = b;
            second = a;
        }
        top = Math.Min(first.Frame.Top, second.Frame.Top);
        left = Math.Min(first.Frame.Left, second.Frame.Left);
        return true;
    }

    static bool HorizontalNeighbors(TileWindow a, TileWindow b, int tolerance)
    {
        var left = a.Frame.Left <= b.Frame.Left ? a : b;
        var right = ReferenceEquals(left, a) ? b : a;
        var gap = right.Frame.Left - left.Frame.Right;
        if (gap < -tolerance || gap > tolerance) return false;
        var overlap = OverlapLength(left.Frame.Top, left.Frame.Bottom, right.Frame.Top, right.Frame.Bottom);
        var shorter = Math.Min(left.Frame.Height, right.Frame.Height);
        return shorter > 0 && overlap >= shorter * MinSharedEdge;
    }

    static bool VerticalNeighbors(TileWindow a, TileWindow b, int tolerance)
    {
        var top = a.Frame.Top <= b.Frame.Top ? a : b;
        var bottom = ReferenceEquals(top, a) ? b : a;
        var gap = bottom.Frame.Top - top.Frame.Bottom;
        if (gap < -tolerance || gap > tolerance) return false;
        var overlap = OverlapLength(top.Frame.Left, top.Frame.Right, bottom.Frame.Left, bottom.Frame.Right);
        var shorter = Math.Min(top.Frame.Width, bottom.Frame.Width);
        return shorter > 0 && overlap >= shorter * MinSharedEdge;
    }

    static int HorizontalGap(TileWindow a, TileWindow b)
    {
        var left = a.Frame.Left <= b.Frame.Left ? a : b;
        var right = ReferenceEquals(left, a) ? b : a;
        return right.Frame.Left - left.Frame.Right;
    }

    static int VerticalGap(TileWindow a, TileWindow b)
    {
        var top = a.Frame.Top <= b.Frame.Top ? a : b;
        var bottom = ReferenceEquals(top, a) ? b : a;
        return bottom.Frame.Top - top.Frame.Bottom;
    }

    static bool OverlapsTooMuch(TileRect a, TileRect b, int tolerance)
    {
        var left = Math.Max(a.Left, b.Left);
        var right = Math.Min(a.Right, b.Right);
        var top = Math.Max(a.Top, b.Top);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top) return false;
        var depthX = right - left;
        var depthY = bottom - top;
        if (depthX <= tolerance || depthY <= tolerance) return false;
        var min = Math.Min(a.Area, b.Area);
        return min > 0 && (long)depthX * depthY > min * MaxOverlapFraction;
    }

    static int OverlapLength(int a0, int a1, int b0, int b1)
    {
        var start = Math.Max(a0, b0);
        var end = Math.Min(a1, b1);
        return Math.Max(0, end - start);
    }

    static long FocusOf(TileWindow first, TileWindow second) =>
        second.UsageRank < first.UsageRank ? second.Handle : first.Handle;

    static bool SameWork(TileRect a, TileRect b, int tolerance) =>
        Math.Abs(a.Left - b.Left) <= tolerance
        && Math.Abs(a.Top - b.Top) <= tolerance
        && Math.Abs(a.Right - b.Right) <= tolerance
        && Math.Abs(a.Bottom - b.Bottom) <= tolerance;

    static double Coverage(IReadOnlyList<TileWindow> windows, TileRect work)
    {
        if (work.Area <= 0) return 0;
        var clipped = new List<TileRect>(windows.Count);
        foreach (var window in windows)
        {
            var inside = Intersect(window.Frame, work);
            if (!inside.IsEmpty) clipped.Add(inside);
        }
        return (double)UnionArea(clipped) / work.Area;
    }

    static TileRect Intersect(TileRect a, TileRect b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top) return default;
        return new TileRect(left, top, right, bottom);
    }

    static long IntersectionArea(TileRect a, TileRect b) => Intersect(a, b).Area;

    static long UnionArea(List<TileRect> rects)
    {
        if (rects.Count == 0) return 0;
        var ys = new List<int>(rects.Count * 2);
        foreach (var rect in rects)
        {
            ys.Add(rect.Top);
            ys.Add(rect.Bottom);
        }
        ys.Sort();
        long area = 0;
        for (var i = 1; i < ys.Count; i++)
        {
            var y0 = ys[i - 1];
            var y1 = ys[i];
            if (y1 <= y0) continue;
            var spans = new List<(int L, int R)>();
            foreach (var rect in rects)
            {
                if (rect.Top <= y0 && rect.Bottom >= y1 && rect.Right > rect.Left)
                    spans.Add((rect.Left, rect.Right));
            }
            if (spans.Count == 0) continue;
            spans.Sort(static (a, b) => a.L.CompareTo(b.L));
            var curL = spans[0].L;
            var curR = spans[0].R;
            long width = 0;
            for (var s = 1; s < spans.Count; s++)
            {
                if (spans[s].L <= curR)
                {
                    curR = Math.Max(curR, spans[s].R);
                    continue;
                }
                width += curR - curL;
                curL = spans[s].L;
                curR = spans[s].R;
            }
            width += curR - curL;
            area += width * (y1 - y0);
        }
        return area;
    }

    readonly record struct Found(long First, long Second, long Focus, int Monitor, int Top, int Left);
}

/// <summary>
/// Builds the extra switcher row for a detected pair. The two source rows stay
/// in the list; this one is added beside them.
/// </summary>
public static class TilePairListing
{
    public const string Separator = " \uFF5C ";

    public static List<WindowEntry> Append(IReadOnlyList<WindowEntry> windows, IReadOnlyList<TilePair> pairs)
    {
        var result = new List<WindowEntry>(windows.Count + pairs.Count);
        var byHandle = new Dictionary<long, WindowEntry>(windows.Count);
        foreach (var window in windows)
        {
            result.Add(window);
            byHandle.TryAdd(window.Handle, window);
        }
        if (pairs.Count == 0) return result;
        var synthetic = -1L;
        foreach (var pair in pairs)
        {
            if (!byHandle.TryGetValue(pair.First, out var first) || first.IsTilePair) continue;
            if (!byHandle.TryGetValue(pair.Second, out var second) || second.IsTilePair) continue;
            while (byHandle.ContainsKey(synthetic)) synthetic--;
            var entry = Create(first, second, synthetic, pair.FocusHandle);
            result.Add(entry);
            byHandle[synthetic] = entry;
            synthetic--;
        }
        return result;
    }

    public static WindowEntry Create(WindowEntry first, WindowEntry second, long handle, long focusHandle)
    {
        var focus = focusHandle == second.Handle ? second : first;
        return new WindowEntry
        {
            Handle = handle,
            Title = Join(Caption(first), Caption(second)),
            ClassName = "",
            ProcessName = focus.ProcessName,
            HostedProcessName = focus.HostedProcessName,
            DisplayProcessName = Join(ProcessLabel(first), ProcessLabel(second)),
            ProcessId = focus.ProcessId,
            HostedProcessId = focus.HostedProcessId,
            KillProcessId = 0,
            KillProcessName = "",
            IconPath = first.IconPath ?? "",
            SecondIconPath = second.IconPath ?? "",
            SecondTitle = Caption(second),
            IsIconic = false,
            IsZoomed = false,
            OriginalIndex = focus.OriginalIndex,
            MonitorNumber = first.MonitorNumber,
            MonitorWidth = first.MonitorWidth,
            MonitorHeight = first.MonitorHeight,
            MonitorPrimary = first.MonitorPrimary,
            MonitorFriendlyName = first.MonitorFriendlyName ?? "",
            IsTilePair = true,
            TileLeft = first.Handle,
            TileRight = second.Handle,
            FocusHandle = focus.Handle,
            TileLeftTitle = first.Title ?? "",
            TileLeftProcess = first.DisplayProcessName ?? "",
            TileRightTitle = second.Title ?? "",
            TileRightProcess = second.DisplayProcessName ?? ""
        };
    }

    public static string Join(string? left, string? right)
    {
        var a = (left ?? "").Trim();
        var b = (right ?? "").Trim();
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        return a + Separator + b;
    }

    static string Caption(WindowEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Title)) return entry.Title.Trim();
        return ProcessLabel(entry);
    }

    static string ProcessLabel(WindowEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.DisplayProcessName)) return entry.DisplayProcessName.Trim();
        if (!string.IsNullOrWhiteSpace(entry.ProcessName)) return entry.ProcessName.Trim();
        return (entry.Title ?? "").Trim();
    }
}
