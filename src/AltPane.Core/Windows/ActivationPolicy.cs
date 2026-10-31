namespace AltPane.Core.Windows;

/// <summary>
/// How activation may touch another process's window. Z-order changes pass
/// SWP_NOSIZE | SWP_NOMOVE so a zero cx/cy cannot shrink the window.
/// A visible window is not shown again: SW_SHOW and SwitchToThisWindow(true)
/// restore a maximized or snapped window onto rcNormalPosition, which is
/// sometimes a few dozen pixels. Only a minimized window is passed SW_RESTORE.
/// </summary>
public readonly record struct WindowFrame(int X, int Y, int Width, int Height, bool Iconic, bool Zoomed, bool Visible)
{
    public override string ToString() =>
        $"{X},{Y} {Width}x{Height} iconic={Iconic} zoomed={Zoomed} visible={Visible}";
}

public static class ActivationPolicy
{
    public const int SwRestore = 9;
    public const int SwShow = 5;
    public const int SwShowMaximized = 3;

    /// <summary>SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING.</summary>
    public const uint PreserveZOrder = 0x0001 | 0x0002 | 0x0010 | 0x0200 | 0x0400;

    public static bool PreservesFrame(uint flags) =>
        (flags & 0x0001) != 0 && (flags & 0x0002) != 0;

    /// <summary>
    /// Null means do not call ShowWindow. A visible window is already on screen.
    /// </summary>
    public static int? ShowCommand(bool iconic, bool visible)
    {
        if (iconic) return SwRestore;
        if (!visible) return SwShow;
        return null;
    }

    public static bool LostMaximized(WindowFrame before, WindowFrame after) =>
        !before.Iconic && before.Zoomed && !after.Zoomed;

    /// <summary>
    /// True when a non-minimized window moved or resized, or lost its maximized state.
    /// A restore from iconic is expected and is not drift. A few pixels of DWM noise is ignored.
    /// </summary>
    public static bool FrameDrifted(WindowFrame before, WindowFrame after, int slack = 8)
    {
        if (before.Iconic || before.Width <= 0 || before.Height <= 0) return false;
        if (LostMaximized(before, after)) return true;
        if (Math.Abs(before.X - after.X) > slack) return true;
        if (Math.Abs(before.Y - after.Y) > slack) return true;
        if (Math.Abs(before.Width - after.Width) > slack) return true;
        if (Math.Abs(before.Height - after.Height) > slack) return true;
        return false;
    }

    /// <summary>
    /// A move onto another monitor scales with the work area. It must not turn a
    /// large window into a stamp because a placement rect was zero or stale.
    /// </summary>
    public static bool MoveWouldCollapse(int beforeWidth, int beforeHeight, int afterWidth, int afterHeight)
    {
        if (beforeWidth < 160 || beforeHeight < 120) return false;
        if (afterWidth < 80 || afterHeight < 60) return true;
        return afterWidth * 2 < beforeWidth && afterHeight * 2 < beforeHeight;
    }

    /// <summary>
    /// A maximized or minimized window stores its restored size in rcNormalPosition.
    /// When that rect is empty or a few pixels, keep a usable size instead.
    /// </summary>
    public static (int X, int Y, int Width, int Height) CoerceRestore(
        int x, int y, int width, int height, int workWidth, int workHeight)
    {
        if (width >= 80 && height >= 60) return (x, y, width, height);
        var w = workWidth > 0 ? Math.Max(80, workWidth / 2) : Math.Max(80, width);
        var h = workHeight > 0 ? Math.Max(60, workHeight / 2) : Math.Max(60, height);
        return (x, y, w, h);
    }
}
