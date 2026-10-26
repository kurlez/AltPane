using System.Runtime.InteropServices;
using AltPane.Core.Preview;

namespace AltPane;

internal static class WindowPlacementStore
{
    public static bool TryGet(nint hwnd, out PlacementSnapshot snapshot)
    {
        snapshot = default;
        if (hwnd == 0) return false;
        var native = new WindowPlacementNative { length = (uint)Marshal.SizeOf<WindowPlacementNative>() };
        if (!NativeMethods.GetWindowPlacement(hwnd, ref native)) return false;
        snapshot = new PlacementSnapshot(
            (int)native.flags,
            (int)native.showCmd,
            native.ptMinPosition.X,
            native.ptMinPosition.Y,
            native.ptMaxPosition.X,
            native.ptMaxPosition.Y,
            native.rcNormalPosition.Left,
            native.rcNormalPosition.Top,
            native.rcNormalPosition.Right,
            native.rcNormalPosition.Bottom);
        return true;
    }

    public static bool TryApply(nint hwnd, PlacementSnapshot snapshot)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd)) return false;
        var native = new WindowPlacementNative
        {
            length = (uint)Marshal.SizeOf<WindowPlacementNative>(),
            flags = (uint)snapshot.Flags,
            showCmd = (uint)snapshot.ShowCmd,
            ptMinPosition = new AltPane.Core.Interop.NativePoint { X = snapshot.MinX, Y = snapshot.MinY },
            ptMaxPosition = new AltPane.Core.Interop.NativePoint { X = snapshot.MaxX, Y = snapshot.MaxY },
            rcNormalPosition = new AltPane.Core.Interop.NativeRect
            {
                Left = snapshot.NormalLeft,
                Top = snapshot.NormalTop,
                Right = snapshot.NormalRight,
                Bottom = snapshot.NormalBottom
            }
        };
        return NativeMethods.SetWindowPlacement(hwnd, ref native);
    }
}
