namespace AltPane.Core.Layout;

public enum HintDirection
{
    Left,
    Right,
    Up,
    Down,
    UpLeft,
    UpRight,
    DownLeft,
    DownRight
}

/// <summary>
/// A monitor in physical pixels. <see cref="X"/> and <see cref="Width"/> are the work area
/// (hints stay off the taskbar). <see cref="ScreenX"/> is the full display, used to place windows.
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

public readonly record struct HintPlan(
    int HintMonitorIndex,
    int HostNumber,
    string HostName,
    HintDirection Direction,
    int X,
    int Y,
    int Width,
    int Height);

/// <summary>
/// Where to put a reminder on every monitor that is not showing the switcher.
/// Coordinates are physical pixels. One monitor produces no hints.
/// </summary>
public static class OtherMonitorHints
{
    public const int WidthDip = 360;
    public const int HeightDip = 96;

    public static IReadOnlyList<DisplayBounds> NumberLeftToRight(IReadOnlyList<DisplayBounds> monitors)
    {
        var ordered = new List<DisplayBounds>(monitors);
        ordered.Sort(static (a, b) =>
        {
            var byX = a.X.CompareTo(b.X);
            return byX != 0 ? byX : a.Y.CompareTo(b.Y);
        });
        for (var i = 0; i < ordered.Count; i++)
            ordered[i] = ordered[i] with { Number = i + 1 };
        return ordered;
    }

    public static int HostIndex(IReadOnlyList<DisplayBounds> monitors, int left, int top, int width, int height)
    {
        if (monitors.Count == 0 || width <= 0 || height <= 0) return -1;
        var cx = left + width / 2;
        var cy = top + height / 2;
        for (var i = 0; i < monitors.Count; i++)
        {
            if (Contains(monitors[i], cx, cy)) return i;
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

    public static IReadOnlyList<HintPlan> Plan(
        IReadOnlyList<DisplayBounds> monitors,
        int hostIndex,
        int hintWidthDip = WidthDip,
        int hintHeightDip = HeightDip)
    {
        if (monitors.Count < 2 || hostIndex < 0 || hostIndex >= monitors.Count)
            return Array.Empty<HintPlan>();

        var host = monitors[hostIndex];
        var plans = new List<HintPlan>(monitors.Count - 1);
        for (var i = 0; i < monitors.Count; i++)
        {
            if (i == hostIndex || SameWorkArea(monitors[i], host)) continue;
            var monitor = monitors[i];
            var scaleX = monitor.DpiX <= 0 ? 1d : monitor.DpiX / 96d;
            var scaleY = monitor.DpiY <= 0 ? 1d : monitor.DpiY / 96d;
            var width = Math.Max(1, (int)Math.Round(hintWidthDip * scaleX));
            var height = Math.Max(1, (int)Math.Round(hintHeightDip * scaleY));
            width = Math.Min(width, Math.Max(1, monitor.Width - 16));
            height = Math.Min(height, Math.Max(1, monitor.Height - 16));
            var x = monitor.X + Math.Max(0, (monitor.Width - width) / 2);
            var y = monitor.Y + Math.Max(0, (monitor.Height - height) / 2);
            plans.Add(new HintPlan(
                i,
                host.Number,
                host.Name ?? "",
                Direction(monitor.CenterX, monitor.CenterY, host.CenterX, host.CenterY),
                x,
                y,
                width,
                height));
        }
        return plans;
    }

    /// <summary>
    /// Direction from a hint toward the switcher. Screen Y grows downward.
    /// A diagonal is used when the shorter axis is at least half the longer one.
    /// </summary>
    public static HintDirection Direction(int fromX, int fromY, int toX, int toY)
    {
        var dx = toX - fromX;
        var dy = toY - fromY;
        var ax = Math.Abs(dx);
        var ay = Math.Abs(dy);
        if (ax == 0 && ay == 0) return HintDirection.Right;
        if (ax > 0 && ay > 0 && Math.Min(ax, ay) * 2 >= Math.Max(ax, ay))
        {
            if (dy < 0) return dx < 0 ? HintDirection.UpLeft : HintDirection.UpRight;
            return dx < 0 ? HintDirection.DownLeft : HintDirection.DownRight;
        }
        if (ax >= ay) return dx < 0 ? HintDirection.Left : HintDirection.Right;
        return dy < 0 ? HintDirection.Up : HintDirection.Down;
    }

    public static string Arrow(HintDirection direction) => direction switch
    {
        HintDirection.Left => "←",
        HintDirection.Right => "→",
        HintDirection.Up => "↑",
        HintDirection.Down => "↓",
        HintDirection.UpLeft => "↖",
        HintDirection.UpRight => "↗",
        HintDirection.DownLeft => "↙",
        HintDirection.DownRight => "↘",
        _ => "→"
    };

    static bool Contains(DisplayBounds monitor, int x, int y) =>
        x >= monitor.X && y >= monitor.Y && x < monitor.X + monitor.Width && y < monitor.Y + monitor.Height;

    static bool SameWorkArea(DisplayBounds a, DisplayBounds b) =>
        a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;
}
