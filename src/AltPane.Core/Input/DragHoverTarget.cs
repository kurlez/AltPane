using System.Globalization;

namespace AltPane.Core.Input;

public enum DragHoverKind
{
    None,
    Window,
    Pair,
    Sidebar
}

/// <summary>
/// One switcher entry a file drag is hovering. The id is stable for the
/// dwell timer. A file row, a calculator row, or a launch-only app is
/// <see cref="None"/>: those cannot receive a drop.
/// </summary>
public readonly record struct DragHoverTarget(
    DragHoverKind Kind,
    long Handle,
    long Partner,
    long Focus,
    int SidebarIndex,
    string? SidebarKey)
{
    public static DragHoverTarget None => default;

    public bool IsEmpty => Kind == DragHoverKind.None || string.IsNullOrEmpty(Id);

    public string Id => Kind switch
    {
        DragHoverKind.Window => "w:" + Handle.ToString(CultureInfo.InvariantCulture),
        DragHoverKind.Pair => "p:"
            + Handle.ToString(CultureInfo.InvariantCulture)
            + ":"
            + Partner.ToString(CultureInfo.InvariantCulture),
        DragHoverKind.Sidebar => "s:" + (SidebarKey ?? ""),
        _ => ""
    };

    public static DragHoverTarget Window(long handle) =>
        handle == 0 ? None : new(DragHoverKind.Window, handle, 0, handle, -1, null);

    public static DragHoverTarget Pair(long left, long right, long focus)
    {
        if (left == 0 || right == 0) return None;
        return new(DragHoverKind.Pair, left, right, focus == 0 ? left : focus, -1, null);
    }

    public static DragHoverTarget Sidebar(int index, string? key) =>
        string.IsNullOrEmpty(key) ? None : new(DragHoverKind.Sidebar, 0, 0, 0, index, key);

    /// <summary>
    /// Window rows, side-by-side pairs, and frequent-app slots that already
    /// have a window can be raised. File hits, calculations, and launch-only
    /// app rows cannot: there is nothing to drop into, and launching would
    /// end the drag.
    /// </summary>
    public static bool CanActivate(DragHoverKind kind) =>
        kind is DragHoverKind.Window or DragHoverKind.Pair or DragHoverKind.Sidebar;

    public static bool CanActivateSidebar(bool runningWindow) => runningWindow;
}
