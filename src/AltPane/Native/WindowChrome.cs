using System.Runtime.InteropServices;
using AltPane.Core.Input;
using AltPane.Core.Interop;
using AltPane.Core.Layout;
using AltPane.Core.Models;

namespace AltPane;

internal readonly record struct MonitorWorkArea(int X, int Y, int Width, int Height, uint DpiX, uint DpiY);

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
        // Ctrl held across the trigger key-up suppresses the menu bar / Start menu.
        Tap(VirtualKeys.Control, up: false);
        Tap(vk, up: true);
        Tap(VirtualKeys.Control, up: true);
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

    public static void ApplyBackdrop(nint hwnd, bool dark)
    {
        var corner = NativeMethods.DwmwcpRound;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaWindowCornerPreference, ref corner, sizeof(int));
        var darkMode = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        var backdrop = NativeMethods.DwmsbtTransientWindow;
        var hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        if (hr != 0)
        {
            backdrop = NativeMethods.DwmsbtMainWindow;
            hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        }
        if (hr != 0)
            ApplyLegacyAcrylic(hwnd, dark);

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    static void ApplyLegacyAcrylic(nint hwnd, bool dark)
    {
        var accent = new AccentPolicy
        {
            AccentState = 4,
            AccentFlags = 2,
            GradientColor = dark ? 0xCC221C1Cu : 0xD8F5F2F2u
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = 19,
                Data = ptr,
                SizeOfData = size
            };
            NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}

internal sealed class ThumbnailController
{
    nint _thumb;
    nint _source;

    public void Clear()
    {
        if (_thumb == 0) return;
        NativeMethods.DwmUnregisterThumbnail(_thumb);
        _thumb = 0;
        _source = 0;
    }

    public int Show(nint destination, nint source, RectangleMath.RectI bounds)
    {
        if (destination == 0 || source == 0)
        {
            Clear();
            return -1;
        }

        if (_thumb == 0 || _source != source)
        {
            Clear();
            var registered = NativeMethods.DwmRegisterThumbnail(destination, source, out _thumb);
            if (registered != 0 || _thumb == 0)
            {
                _thumb = 0;
                _source = 0;
                return registered;
            }
            _source = source;
        }

        var fitted = bounds;
        if (NativeMethods.DwmQueryThumbnailSourceSize(_thumb, out var size) == 0 && size.Cx > 0 && size.Cy > 0)
            fitted = RectangleMath.FitContain(bounds, size.Cx, size.Cy);

        var props = new DwmThumbnailProperties
        {
            dwFlags = DwmThumbnailFlags.RectDestination | DwmThumbnailFlags.Visible | DwmThumbnailFlags.Opacity | DwmThumbnailFlags.SourceClientAreaOnly,
            rcDestination = new NativeRect
            {
                Left = fitted.X,
                Top = fitted.Y,
                Right = fitted.X + fitted.Width,
                Bottom = fitted.Y + fitted.Height
            },
            opacity = 255,
            fVisible = 1,
            fSourceClientAreaOnly = 0
        };
        return NativeMethods.DwmUpdateThumbnailProperties(_thumb, ref props);
    }
}
