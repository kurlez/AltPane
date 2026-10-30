using System.Runtime.InteropServices;
using AltPane.Core.Input;
using AltPane.Core.Interop;
using AltPane.Core.Layout;
using AltPane.Core.Models;

namespace AltPane;

internal readonly record struct MonitorWorkArea(int X, int Y, int Width, int Height, uint DpiX, uint DpiY);

internal readonly record struct LiveMonitor(nint Handle, DisplayBounds Bounds);

internal static class MonitorUtil
{
    public static MonitorWorkArea GetWorkArea(SwitcherPlacement placement, nint activeWindow)
    {
        nint monitor = 0;
        if (placement == SwitcherPlacement.ActiveWindow && activeWindow != 0)
            monitor = NativeMethods.MonitorFromWindow(activeWindow, NativeMethods.MonitorDefaultToNearest);
        if (monitor == 0 && NativeMethods.GetCursorPos(out var point))
            monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
        if (monitor == 0)
            monitor = NativeMethods.MonitorFromPoint(new NativePoint(), NativeMethods.MonitorDefaultToNearest);

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return new MonitorWorkArea(0, 0, 1280, 800, 96, 96);

        uint dpiX = 96, dpiY = 96;
        try
        {
            if (NativeMethods.GetDpiForMonitor(monitor, 0, out var x, out var y) == 0)
            {
                if (x != 0) dpiX = x;
                if (y != 0) dpiY = y;
            }
        }
        catch
        {
            // GetDpiForMonitor is missing on very old builds. 96 keeps the window on screen.
        }

        return new MonitorWorkArea(
            info.rcWork.Left,
            info.rcWork.Top,
            Math.Max(1, info.rcWork.Right - info.rcWork.Left),
            Math.Max(1, info.rcWork.Bottom - info.rcWork.Top),
            dpiX,
            dpiY);
    }

    public static IReadOnlyList<DisplayBounds> All()
    {
        var live = Live();
        var bounds = new DisplayBounds[live.Count];
        for (var i = 0; i < live.Count; i++) bounds[i] = live[i].Bounds;
        return bounds;
    }

    public static IReadOnlyList<LiveMonitor> Live()
    {
        var found = new List<LiveMonitor>();
        NativeMethods.MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
            if (!NativeMethods.GetMonitorInfoEx(monitor, ref info)) return true;
            var dpiX = 96;
            var dpiY = 96;
            try
            {
                if (NativeMethods.GetDpiForMonitor(monitor, 0, out var x, out var y) == 0)
                {
                    if (x != 0) dpiX = (int)x;
                    if (y != 0) dpiY = (int)y;
                }
            }
            catch
            {
                // GetDpiForMonitor is missing on very old builds.
            }

            var rawName = info.szDevice ?? "";
            var name = rawName;
            const string prefix = @"\\.\";
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                name = name[prefix.Length..];
            found.Add(new LiveMonitor(monitor, new DisplayBounds(
                info.rcWork.Left,
                info.rcWork.Top,
                Math.Max(1, info.rcWork.Right - info.rcWork.Left),
                Math.Max(1, info.rcWork.Bottom - info.rcWork.Top),
                dpiX,
                dpiY,
                0,
                name,
                Primary: (info.dwFlags & NativeMethods.MonitorInfoPrimary) != 0,
                ScreenX: info.rcMonitor.Left,
                ScreenY: info.rcMonitor.Top,
                ScreenWidth: Math.Max(0, info.rcMonitor.Right - info.rcMonitor.Left),
                ScreenHeight: Math.Max(0, info.rcMonitor.Bottom - info.rcMonitor.Top),
                FriendlyName: ReadFriendlyName(rawName))));
            return true;
        };
        NativeMethods.EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);
        found.Sort(static (a, b) => MonitorLayout.Compare(a.Bounds, b.Bounds));
        var bounds = new List<DisplayBounds>(found.Count);
        foreach (var item in found) bounds.Add(item.Bounds);
        MonitorLayout.AssignNumbers(bounds);
        for (var i = 0; i < found.Count; i++)
            found[i] = found[i] with { Bounds = bounds[i] };
        return found;
    }

    static string ReadFriendlyName(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return "";
        var full = deviceName.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
            ? deviceName
            : @"\\.\" + deviceName;
        try
        {
            for (uint i = 0; i < 8; i++)
            {
                var device = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
                if (!NativeMethods.EnumDisplayDevices(full, i, ref device, 0)) break;
                if (!string.IsNullOrWhiteSpace(device.DeviceString))
                    return device.DeviceString.Trim();
            }
        }
        catch
        {
            // EnumDisplayDevices is absent on very old builds. The number still shows.
        }
        return "";
    }
}

internal static class ModifierRelease
{
    public const nuint Magic = 0xA17A4E01;

    public static void Release(ModifierKeys trigger)
    {
        var vk = trigger switch
        {
            ModifierKeys.Alt => VirtualKeys.Menu,
            ModifierKeys.Control => VirtualKeys.Control,
            ModifierKeys.Shift => VirtualKeys.Shift,
            ModifierKeys.Win => VirtualKeys.LWin,
            _ => 0
        };
        if (vk == 0) return;
        // A swallowed Win chord still has Win logically down. An unassigned key
        // while it is down keeps Start from treating the following key-up as a tap.
        // Both Win keys are released so a right-Win chord cannot stay stuck.
        if (trigger == ModifierKeys.Win)
            MarkWinChordUsed();
        Tap(VirtualKeys.Control, up: false);
        if (trigger == ModifierKeys.Win)
        {
            Tap(VirtualKeys.LWin, up: true);
            Tap(VirtualKeys.RWin, up: true);
        }
        else
        {
            Tap(vk, up: true);
        }
        Tap(VirtualKeys.Control, up: true);
    }

    public static void MarkWinChordUsed()
    {
        Tap(VirtualKeys.Unused, up: false);
        Tap(VirtualKeys.Unused, up: true);
    }

    /// <summary>
    /// A lone Win key-up would open Start. Inject VK_E8 while Win is still down,
    /// then release both Win keys. The hook swallows the physical key-up.
    /// </summary>
    public static void SuppressStart()
    {
        MarkWinChordUsed();
        Tap(VirtualKeys.LWin, up: true);
        Tap(VirtualKeys.RWin, up: true);
    }

    static void Tap(int vk, bool up)
    {
        var input = new NativeInput
        {
            Type = 1,
            Data = new NativeInputUnion
            {
                Keyboard = new NativeKeyboardInput
                {
                    VirtualKey = (ushort)vk,
                    Flags = up ? 0x0002u : 0u,
                    ExtraInfo = Magic
                }
            }
        };
        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeInput>());
    }
}

internal static class WindowChrome
{
    public static void ApplyToolWindow(nint hwnd)
    {
        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        var tool = ex | 0x80 | 0x00000008;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, tool);
        NativeMethods.SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x0020 | 0x0001 | 0x0002 | 0x0004 | 0x0020);
    }

    public static void ApplyNoActivate(nint hwnd, bool dark)
    {
        if (hwnd == 0) return;
        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        // TOOLWINDOW | TOPMOST | NOACTIVATE. Clicks still arrive, but the window
        // never becomes the foreground window, so the switcher keeps the keyboard.
        var style = ex | 0x80 | 0x00000008 | 0x08000000;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, style);
        NativeMethods.SetWindowPos(hwnd, new nint(-1), 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0020);
        ApplyBackdrop(hwnd, dark);
    }

    public static void ApplyBackdrop(nint hwnd, bool dark)
    {
        if (hwnd == 0) return;

        // Win11 clips the window to a rounded rectangle. Win10 ignores this
        // attribute and keeps square corners, which is the fallback: a square
        // 1px border, not a region that would cut the shadow off.
        var corner = WindowFrame.CornerPreferenceRound;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaWindowCornerPreference, ref corner, sizeof(int));

        var darkMode = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        // Attribute 19 is the same flag on builds from before it moved to 20.
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkModeLegacy, ref darkMode, sizeof(int));

        // Mica and acrylic need a transparent client. Where composition is off
        // that transparency is black, including a band around the frame.
        var backdrop = WindowFrame.BackdropNone;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaSystemBackdropType, ref backdrop, sizeof(int));

        var border = WindowFrame.BorderColor(dark);
        NativeMethods.DwmSetWindowAttribute(hwnd, WindowFrame.BorderColorAttribute, ref border, sizeof(int));
        var caption = WindowFrame.CaptionColor(dark);
        NativeMethods.DwmSetWindowAttribute(hwnd, WindowFrame.CaptionColorAttribute, ref caption, sizeof(int));

        var shadow = WindowFrame.ShadowMargins;
        var margins = new Margins
        {
            Left = shadow.Left,
            Right = shadow.Right,
            Top = shadow.Top,
            Bottom = shadow.Bottom
        };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }
}
