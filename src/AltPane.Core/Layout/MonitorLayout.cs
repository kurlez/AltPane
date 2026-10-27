namespace AltPane.Core.Layout;

/// <summary>
/// A monitor in physical pixels. <see cref="X"/> and <see cref="Width"/> are the work area.
/// <see cref="ScreenX"/> is the full display, used to place windows.
/// <see cref="Number"/> is the Windows GDI display index when those numbers are unique, otherwise left to right.
/// </summary>
public readonly record struct DisplayBounds(
    int X,
    int Y,
    int Width,
    int Height,
    int DpiX,
    int DpiY,
    int Number,
    string Name,
    bool Primary = false,
    int ScreenX = 0,
    int ScreenY = 0,
    int ScreenWidth = 0,
    int ScreenHeight = 0,
    string FriendlyName = "")
{
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
}

public readonly record struct MonitorColor(byte R, byte G, byte B);

/// <summary>
/// Which monitor a window rect belongs to, and how those monitors are numbered.
/// The overlap rule matches <c>MonitorFromWindow</c>: the display with the largest
/// intersection wins. A minimized window passes its restore rectangle instead.
/// </summary>
public static class MonitorLayout
{
    public static IReadOnlyList<MonitorColor> Palette { get; } = new MonitorColor[]
    {
        new(0x4C, 0x8D, 0xFF),
        new(0x3D, 0xB8, 0x7A),
        new(0xE0, 0x8A, 0x2F),
        new(0xC4, 0x6B, 0xD6),
        new(0xE0, 0x5A, 0x6A),
        new(0x2A, 0xB5, 0xC6),
        new(0xD4, 0xB1, 0x3A),
        new(0x7E, 0x8A, 0xA0)
    };

    public static bool ShowBadges(int monitorCount) => monitorCount >= 2;

    public static int ColorSlot(int number)
    {
        if (Palette.Count == 0 || number <= 0) return 0;
        return (number - 1) % Palette.Count;
    }

    public static MonitorColor Color(int number) => Palette[ColorSlot(number)];

    /// <summary>Trailing index of <c>\\.\DISPLAYn</c>. Zero and non-numeric names are ignored.</summary>
    public static int? DisplayIndex(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = name.Trim();
        const string marker = "DISPLAY";
        var at = trimmed.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var rest = trimmed[(at + marker.Length)..];
        var digits = 0;
        while (digits < rest.Length && char.IsDigit(rest[digits])) digits++;
        if (digits == 0) return null;
        if (digits < rest.Length && rest[digits] != '\\') return null;
        if (!int.TryParse(rest[..digits], out var number) || number <= 0 || number > 64) return null;
        return number;
    }

    /// <summary>
    /// Index of the work area that contains the center of the rect.
    /// Otherwise the nearest work area. Empty list or a non-positive size returns -1.
    /// </summary>
    public static int HostIndex(IReadOnlyList<DisplayBounds> monitors, int left, int top, int width, int height)
    {
        if (monitors.Count == 0 || width <= 0 || height <= 0) return -1;
        var cx = left + width / 2;
        var cy = top + height / 2;
        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            if (cx >= monitor.X && cy >= monitor.Y && cx < monitor.X + monitor.Width && cy < monitor.Y + monitor.Height)
                return i;
        }

        var best = 0;
        var bestDistance = long.MaxValue;
        for (var i = 0; i < monitors.Count; i++)
        {
            var dx = (long)monitors[i].CenterX - cx;
            var dy = (long)monitors[i].CenterY - cy;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    public static int Compare(DisplayBounds a, DisplayBounds b)
    {
        var byX = a.X.CompareTo(b.X);
        return byX != 0 ? byX : a.Y.CompareTo(b.Y);
    }

    /// <summary>
    /// Sort left to right, then number from the GDI display name when every monitor has a unique index.
    /// Otherwise number 1..n in that same order.
    /// </summary>
    public static IReadOnlyList<DisplayBounds> NumberAndSort(IReadOnlyList<DisplayBounds> monitors)
    {
        var ordered = new List<DisplayBounds>(monitors);
        ordered.Sort(static (a, b) => Compare(a, b));
        AssignNumbers(ordered);
        return ordered;
    }

    public static void AssignNumbers(IList<DisplayBounds> orderedLeftToRight)
    {
        if (!TryUniqueDisplayNumbers(orderedLeftToRight, out var numbers))
        {
            for (var i = 0; i < orderedLeftToRight.Count; i++)
                orderedLeftToRight[i] = orderedLeftToRight[i] with { Number = i + 1 };
            return;
        }

        for (var i = 0; i < orderedLeftToRight.Count; i++)
            orderedLeftToRight[i] = orderedLeftToRight[i] with { Number = numbers[i] };
    }

    public static (int Width, int Height) Resolution(DisplayBounds monitor)
    {
        if (monitor.ScreenWidth > 0 && monitor.ScreenHeight > 0)
            return (monitor.ScreenWidth, monitor.ScreenHeight);
        return (Math.Max(0, monitor.Width), Math.Max(0, monitor.Height));
    }

    public static (int X, int Y) WorkspaceOrigin(IReadOnlyList<DisplayBounds> monitors)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.Primary) return (monitor.X, monitor.Y);
        }
        return (0, 0);
    }

    /// <summary>
    /// Minimized restore rects are workspace coordinates. Keep them when they already hit a monitor.
    /// Otherwise slide by the primary work-area origin, which is the usual taskbar offset.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) PreferIntersecting(
        IReadOnlyList<DisplayBounds> monitors,
        int left,
        int top,
        int right,
        int bottom,
        int originX,
        int originY)
    {
        if (IndexForRect(monitors, left, top, right, bottom, nearest: false) >= 0)
            return (left, top, right, bottom);
        if ((originX != 0 || originY != 0)
            && IndexForRect(monitors, left + originX, top + originY, right + originX, bottom + originY, nearest: false) >= 0)
            return (left + originX, top + originY, right + originX, bottom + originY);
        return (left, top, right, bottom);
    }

    /// <summary>
    /// Monitor index with the largest overlap. Equal areas prefer the smaller display number.
    /// No overlap returns the nearest monitor, unless <paramref name="nearest"/> is false.
    /// </summary>
    public static int IndexForRect(
        IReadOnlyList<DisplayBounds> monitors,
        int left,
        int top,
        int right,
        int bottom,
        bool nearest = true)
    {
        if (monitors.Count == 0 || right <= left || bottom <= top) return -1;
        long bestArea = 0;
        var best = -1;
        for (var i = 0; i < monitors.Count; i++)
        {
            var area = Intersection(monitors[i], left, top, right, bottom);
            if (area <= 0) continue;
            if (best < 0 || area > bestArea || (area == bestArea && Prefer(monitors[i], i, monitors[best], best)))
            {
                bestArea = area;
                best = i;
            }
        }

        if (best >= 0) return best;
        if (!nearest) return -1;
        var cx = left + (right - left) / 2;
        var cy = top + (bottom - top) / 2;
        best = 0;
        var bestDistance = long.MaxValue;
        for (var i = 0; i < monitors.Count; i++)
        {
            var (x, y, _, _) = OverlapBounds(monitors[i]);
            var (width, height) = SizeOf(monitors[i]);
            var dx = (long)(x + width / 2) - cx;
            var dy = (long)(y + height / 2) - cy;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance || (distance == bestDistance && Prefer(monitors[i], i, monitors[best], best)))
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    public static bool IsUsefulName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        if (GenericNames.Contains(trimmed)) return false;
        return !IsBareDisplayName(trimmed);
    }

    /// <summary>
    /// Text after the resolution. The primary label wins, and a real monitor name is appended when it adds something.
    /// </summary>
    public static string? DetailSuffix(bool primary, string? friendlyName, string primaryLabel)
    {
        var name = IsUsefulName(friendlyName) ? friendlyName!.Trim() : null;
        var primaryText = primary && !string.IsNullOrEmpty(primaryLabel) ? primaryLabel : null;
        if (primaryText is not null && name is not null) return primaryText + " · " + name;
        return primaryText ?? name;
    }

    static bool TryUniqueDisplayNumbers(IList<DisplayBounds> monitors, out int[] numbers)
    {
        numbers = new int[monitors.Count];
        if (monitors.Count == 0) return false;
        var seen = new HashSet<int>();
        for (var i = 0; i < monitors.Count; i++)
        {
            var index = DisplayIndex(monitors[i].Name);
            if (index is not int number || !seen.Add(number)) return false;
            numbers[i] = number;
        }
        return true;
    }

    static bool Prefer(DisplayBounds candidate, int candidateIndex, DisplayBounds current, int currentIndex)
    {
        if (candidate.Number != current.Number) return candidate.Number < current.Number;
        return candidateIndex < currentIndex;
    }

    static long Intersection(DisplayBounds monitor, int left, int top, int right, int bottom)
    {
        var (x, y, width, height) = OverlapBounds(monitor);
        if (width <= 0 || height <= 0) return 0;
        var x1 = Math.Max(x, left);
        var y1 = Math.Max(y, top);
        var x2 = Math.Min(x + width, right);
        var y2 = Math.Min(y + height, bottom);
        if (x2 <= x1 || y2 <= y1) return 0;
        return (long)(x2 - x1) * (y2 - y1);
    }

    static (int X, int Y, int Width, int Height) OverlapBounds(DisplayBounds monitor)
    {
        if (monitor.ScreenWidth > 0 && monitor.ScreenHeight > 0)
            return (monitor.ScreenX, monitor.ScreenY, monitor.ScreenWidth, monitor.ScreenHeight);
        return (monitor.X, monitor.Y, monitor.Width, monitor.Height);
    }

    static (int Width, int Height) SizeOf(DisplayBounds monitor)
    {
        var (_, _, width, height) = OverlapBounds(monitor);
        return (width, height);
    }

    static bool IsBareDisplayName(string name)
    {
        var index = DisplayIndex(name);
        if (index is null) return false;
        var stripped = name.Trim().Replace('/', '\\');
        while (stripped.StartsWith('\\') || stripped.StartsWith('.'))
            stripped = stripped[1..];
        return stripped.Equals($"DISPLAY{index.Value}", StringComparison.OrdinalIgnoreCase)
            || stripped.StartsWith($"DISPLAY{index.Value}\\", StringComparison.OrdinalIgnoreCase);
    }

    static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Generic PnP Monitor",
        "Generic Monitor",
        "Default Monitor",
        "PnP Monitor",
        "通用即插即用监视器",
        "通用监视器",
        "即插即用监视器"
    };
}
