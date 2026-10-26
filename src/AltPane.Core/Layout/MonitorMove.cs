namespace AltPane.Core.Layout;

public enum SnapEdge
{
    None,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>
/// Where a window should sit on the switcher's monitor. Coordinates are physical pixels.
/// Fractions of the source work area are reused on the target, so a DPI change is not applied twice.
/// </summary>
public readonly record struct MovePlan(bool Move, bool Maximize, int X, int Y, int Width, int Height);

public static class MonitorMove
{
    public const int SnapSlack = 12;

    public static MovePlan Plan(
        DisplayBounds source,
        DisplayBounds target,
        int left,
        int top,
        int width,
        int height,
        bool minimized,
        bool maximized,
        bool fullscreen,
        int restoreLeft,
        int restoreTop,
        int restoreWidth,
        int restoreHeight,
        bool restoreToMaximized)
    {
        if (SameMonitor(source, target))
            return new MovePlan(false, false, left, top, Math.Max(1, width), Math.Max(1, height));

        var useRestore = minimized || maximized || restoreToMaximized;
        var hasRestore = restoreWidth > 0 && restoreHeight > 0;
        var rx = hasRestore ? restoreLeft : left;
        var ry = hasRestore ? restoreTop : top;
        var rw = hasRestore ? restoreWidth : width;
        var rh = hasRestore ? restoreHeight : height;
        var wantMax = maximized || fullscreen || (minimized && restoreToMaximized);

        if (wantMax)
        {
            var mapped = ScaleToWork(source, target, rx, ry, rw, rh);
            return new MovePlan(true, true, mapped.X, mapped.Y, mapped.Width, mapped.Height);
        }

        var basisLeft = useRestore ? rx : left;
        var basisTop = useRestore ? ry : top;
        var basisWidth = useRestore ? rw : width;
        var basisHeight = useRestore ? rh : height;
        var snap = DetectSnap(source, basisLeft, basisTop, basisWidth, basisHeight);
        if (snap != SnapEdge.None)
        {
            var snapped = Replay(target, snap);
            return new MovePlan(true, false, snapped.X, snapped.Y, snapped.Width, snapped.Height);
        }

        var fitted = ScaleToWork(source, target, basisLeft, basisTop, basisWidth, basisHeight);
        return new MovePlan(true, false, fitted.X, fitted.Y, fitted.Width, fitted.Height);
    }

    public static bool SameMonitor(DisplayBounds source, DisplayBounds target) =>
        source.Width > 0
        && source.Height > 0
        && source.Number == target.Number
        && source.X == target.X
        && source.Y == target.Y;

    public static bool CoversScreen(DisplayBounds monitor, int left, int top, int width, int height)
    {
        var x = monitor.ScreenWidth > 0 ? monitor.ScreenX : monitor.X;
        var y = monitor.ScreenHeight > 0 ? monitor.ScreenY : monitor.Y;
        var w = monitor.ScreenWidth > 0 ? monitor.ScreenWidth : monitor.Width;
        var h = monitor.ScreenHeight > 0 ? monitor.ScreenHeight : monitor.Height;
        if (w <= 0 || h <= 0 || width <= 0 || height <= 0) return false;
        return Math.Abs(left - x) <= 2
            && Math.Abs(top - y) <= 2
            && Math.Abs(width - w) <= 4
            && Math.Abs(height - h) <= 4;
    }

    public static SnapEdge DetectSnap(DisplayBounds work, int left, int top, int width, int height)
    {
        if (work.Width <= 0 || work.Height <= 0 || width <= 0 || height <= 0) return SnapEdge.None;
        var nearLeft = Math.Abs(left - work.X) <= SnapSlack;
        var nearRight = Math.Abs((left + width) - (work.X + work.Width)) <= SnapSlack;
        var nearTop = Math.Abs(top - work.Y) <= SnapSlack;
        var nearBottom = Math.Abs((top + height) - (work.Y + work.Height)) <= SnapSlack;
        var halfWidth = Math.Abs(width * 2 - work.Width) <= SnapSlack * 2 + 8;
        var halfHeight = Math.Abs(height * 2 - work.Height) <= SnapSlack * 2 + 8;
        var fullWidth = Math.Abs(width - work.Width) <= SnapSlack;
        var fullHeight = Math.Abs(height - work.Height) <= SnapSlack;

        if (nearLeft && nearTop && halfWidth && halfHeight) return SnapEdge.TopLeft;
        if (nearRight && nearTop && halfWidth && halfHeight) return SnapEdge.TopRight;
        if (nearLeft && nearBottom && halfWidth && halfHeight) return SnapEdge.BottomLeft;
        if (nearRight && nearBottom && halfWidth && halfHeight) return SnapEdge.BottomRight;
        if (nearLeft && halfWidth && (fullHeight || nearTop && nearBottom)) return SnapEdge.Left;
        if (nearRight && halfWidth && (fullHeight || nearTop && nearBottom)) return SnapEdge.Right;
        if (nearTop && halfHeight && (fullWidth || nearLeft && nearRight)) return SnapEdge.Top;
        if (nearBottom && halfHeight && (fullWidth || nearLeft && nearRight)) return SnapEdge.Bottom;
        return SnapEdge.None;
    }

    public static MovePlan Replay(DisplayBounds work, SnapEdge edge)
    {
        var halfW = Math.Max(1, work.Width / 2);
        var halfH = Math.Max(1, work.Height / 2);
        var restW = Math.Max(1, work.Width - halfW);
        var restH = Math.Max(1, work.Height - halfH);
        return edge switch
        {
            SnapEdge.Left => new MovePlan(true, false, work.X, work.Y, halfW, work.Height),
            SnapEdge.Right => new MovePlan(true, false, work.X + halfW, work.Y, restW, work.Height),
            SnapEdge.Top => new MovePlan(true, false, work.X, work.Y, work.Width, halfH),
            SnapEdge.Bottom => new MovePlan(true, false, work.X, work.Y + halfH, work.Width, restH),
            SnapEdge.TopLeft => new MovePlan(true, false, work.X, work.Y, halfW, halfH),
            SnapEdge.TopRight => new MovePlan(true, false, work.X + halfW, work.Y, restW, halfH),
            SnapEdge.BottomLeft => new MovePlan(true, false, work.X, work.Y + halfH, halfW, restH),
            SnapEdge.BottomRight => new MovePlan(true, false, work.X + halfW, work.Y + halfH, restW, restH),
            _ => new MovePlan(false, false, work.X, work.Y, work.Width, work.Height)
        };
    }

    public static (int X, int Y, int Width, int Height) ScaleToWork(
        DisplayBounds source,
        DisplayBounds target,
        int left,
        int top,
        int width,
        int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (source.Width <= 0 || source.Height <= 0 || target.Width <= 0 || target.Height <= 0)
            return Clamp(target, target.X, target.Y, width, height);

        var fx = (left - source.X) / (double)source.Width;
        var fy = (top - source.Y) / (double)source.Height;
        var fw = width / (double)source.Width;
        var fh = height / (double)source.Height;
        var w = Math.Max(1, (int)Math.Round(fw * target.Width));
        var h = Math.Max(1, (int)Math.Round(fh * target.Height));
        var x = target.X + (int)Math.Round(fx * target.Width);
        var y = target.Y + (int)Math.Round(fy * target.Height);
        return Clamp(target, x, y, w, h);
    }

    public static (int X, int Y, int Width, int Height) Clamp(DisplayBounds work, int x, int y, int width, int height)
    {
        if (work.Width <= 0 || work.Height <= 0)
            return (x, y, Math.Max(1, width), Math.Max(1, height));
        width = Math.Clamp(Math.Max(1, width), 1, work.Width);
        height = Math.Clamp(Math.Max(1, height), 1, work.Height);
        var maxX = work.X + work.Width - width;
        var maxY = work.Y + work.Height - height;
        x = Math.Clamp(x, work.X, Math.Max(work.X, maxX));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, maxY));
        return (x, y, width, height);
    }
}
