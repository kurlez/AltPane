using AltPane.Core.Interop;
using AltPane.Core.Layout;
using AltPane.Core.Localization;
using AltPane.Core.Models;

namespace AltPane;

internal readonly record struct MonitorHit(int Number, int Width, int Height, bool Primary, string FriendlyName);

/// <summary>
/// Places a window on a monitor. Visible windows use MonitorFromWindow.
/// Minimized windows use the restore rectangle with MonitorFromRect.
/// Both fall back to the largest overlap when the API returns no monitor.
/// </summary>
internal static class MonitorLocator
{
    public static MonitorHit Locate(nint hwnd, IReadOnlyList<LiveMonitor> monitors)
    {
        if (hwnd == 0 || monitors.Count == 0) return default;
        var bounds = new DisplayBounds[monitors.Count];
        for (var i = 0; i < monitors.Count; i++) bounds[i] = monitors[i].Bounds;

        nint handle = 0;
        var haveRect = false;
        var left = 0;
        var top = 0;
        var right = 0;
        var bottom = 0;
        try
        {
            if (NativeMethods.IsIconic(hwnd) && WindowPlacementStore.TryGet(hwnd, out var snapshot))
            {
                left = snapshot.NormalLeft;
                top = snapshot.NormalTop;
                right = snapshot.NormalRight;
                bottom = snapshot.NormalBottom;
                if (right > left && bottom > top)
                {
                    var origin = MonitorLayout.WorkspaceOrigin(bounds);
                    (left, top, right, bottom) = MonitorLayout.PreferIntersecting(bounds, left, top, right, bottom, origin.X, origin.Y);
                    var rect = new NativeRect { Left = left, Top = top, Right = right, Bottom = bottom };
                    handle = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MonitorDefaultToNull);
                    haveRect = true;
                }
            }
            else if (!NativeMethods.IsIconic(hwnd))
            {
                handle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNull);
                if (NativeMethods.GetWindowRect(hwnd, out var rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
                {
                    left = rect.Left;
                    top = rect.Top;
                    right = rect.Right;
                    bottom = rect.Bottom;
                    haveRect = true;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }

        var index = IndexOf(monitors, handle);
        if (index < 0 && haveRect)
            index = MonitorLayout.IndexForRect(bounds, left, top, right, bottom);
        if (index < 0 || index >= bounds.Length) return default;
        var monitor = bounds[index];
        var (width, height) = MonitorLayout.Resolution(monitor);
        return new MonitorHit(monitor.Number, width, height, monitor.Primary, monitor.FriendlyName ?? "");
    }

    public static void CopyTo(WindowEntry entry, MonitorHit hit)
    {
        entry.MonitorNumber = hit.Number;
        entry.MonitorWidth = hit.Width;
        entry.MonitorHeight = hit.Height;
        entry.MonitorPrimary = hit.Primary;
        entry.MonitorFriendlyName = hit.FriendlyName ?? "";
    }

    static int IndexOf(IReadOnlyList<LiveMonitor> monitors, nint handle)
    {
        if (handle == 0) return -1;
        for (var i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].Handle == handle) return i;
        }
        return -1;
    }
}

internal static class MonitorChrome
{
    public static System.Windows.Media.SolidColorBrush Brush(int number, byte alpha)
    {
        var color = MonitorLayout.Color(number);
        var brush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public static string Format(int number, int width, int height, bool primary, string? friendly)
    {
        if (number <= 0) return "";
        var loc = Localizer.Current;
        var suffix = MonitorLayout.DetailSuffix(primary, friendly, loc["monitor.primary"]);
        return suffix is null
            ? loc.Format("monitor.detail", number, width, height)
            : loc.Format("monitor.detailNamed", number, width, height, suffix);
    }
}
