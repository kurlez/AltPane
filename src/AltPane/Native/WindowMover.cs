using AltPane.Core.Interop;
using AltPane.Core.Layout;
using AltPane.Core.Preview;

namespace AltPane;

/// <summary>
/// Moves a window onto the switcher's monitor before it is activated.
/// A failure (an elevated window, or an exclusive fullscreen game) is reported
/// and the caller still tries to activate.
/// </summary>
internal static class WindowMover
{
    public static bool TryMoveTo(nint hwnd, DisplayBounds target, IReadOnlyList<DisplayBounds> monitors)
    {
        if (hwnd == 0 || monitors.Count == 0 || !NativeMethods.IsWindow(hwnd)) return false;
        try
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return false;
            var width = Math.Max(1, rect.Right - rect.Left);
            var height = Math.Max(1, rect.Bottom - rect.Top);
            var iconic = NativeMethods.IsIconic(hwnd);
            var zoomed = NativeMethods.IsZoomed(hwnd);
            var (originX, originY) = MonitorLayout.WorkspaceOrigin(monitors);
            var restoreLeft = rect.Left;
            var restoreTop = rect.Top;
            var restoreWidth = width;
            var restoreHeight = height;
            var restoreToMax = false;
            if (WindowPlacementStore.TryGet(hwnd, out var placement))
            {
                restoreToMax = PlacementEdits.WasMaximized(placement);
                var corrected = MonitorLayout.PreferIntersecting(
                    monitors,
                    placement.NormalLeft,
                    placement.NormalTop,
                    placement.NormalRight,
                    placement.NormalBottom,
                    originX,
                    originY);
                restoreLeft = corrected.Left;
                restoreTop = corrected.Top;
                restoreWidth = Math.Max(1, corrected.Right - corrected.Left);
                restoreHeight = Math.Max(1, corrected.Bottom - corrected.Top);
            }

            var basisLeft = iconic ? restoreLeft : rect.Left;
            var basisTop = iconic ? restoreTop : rect.Top;
            var basisRight = basisLeft + (iconic ? restoreWidth : width);
            var basisBottom = basisTop + (iconic ? restoreHeight : height);
            var index = MonitorLayout.IndexForRect(monitors, basisLeft, basisTop, basisRight, basisBottom);
            if (index < 0) return false;
            var source = monitors[index];
            var fullscreen = !iconic && MonitorMove.CoversScreen(source, rect.Left, rect.Top, width, height);
            var plan = MonitorMove.Plan(
                source,
                target,
                rect.Left,
                rect.Top,
                width,
                height,
                iconic,
                zoomed,
                fullscreen,
                restoreLeft,
                restoreTop,
                restoreWidth,
                restoreHeight,
                restoreToMax);
            if (!plan.Move) return true;

            if (plan.Maximize || iconic)
                return ApplyPlacement(hwnd, plan, originX, originY, iconic);
            return NativeMethods.SetWindowPos(
                hwnd,
                0,
                plan.X,
                plan.Y,
                plan.Width,
                plan.Height,
                NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
    }

    static bool ApplyPlacement(nint hwnd, MovePlan plan, int originX, int originY, bool iconic)
    {
        if (!WindowPlacementStore.TryGet(hwnd, out var current)) return false;
        var show = plan.Maximize
            ? PlacementEdits.ShowMaximized
            : iconic
                ? NativeMethods.SwRestore
                : PlacementEdits.ShowNoActivate;
        var flags = plan.Maximize ? current.Flags | PlacementEdits.RestoreToMaximized : current.Flags & ~PlacementEdits.RestoreToMaximized;
        var updated = current with
        {
            Flags = flags,
            ShowCmd = show,
            NormalLeft = plan.X - originX,
            NormalTop = plan.Y - originY,
            NormalRight = plan.X - originX + plan.Width,
            NormalBottom = plan.Y - originY + plan.Height
        };
        if (!WindowPlacementStore.TryApply(hwnd, updated)) return false;
        if (plan.Maximize)
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwShowMaximized);
        else if (iconic)
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
        return true;
    }
}
