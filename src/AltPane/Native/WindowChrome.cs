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

        // The destination already has the source aspect, so DWM scales into it
        // instead of padding the thumbnail. Bars around it are the preview well.
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
