using AltPane.Core.Interop;
using AltPane.Core.Layout;
using AltPane.Core.Models;
using AltPane.Core.Switcher;

namespace AltPane;

/// <summary>
/// Reads <c>DWMWA_EXTENDED_FRAME_BOUNDS</c> so snapped windows are measured by
/// the visible frame, not the invisible resize border, and adds one extra row
/// for each adjacent pair. Coordinates stay in physical pixels, matching the
/// monitor work area, so DPI does not have to be applied again.
/// </summary>
internal static class TileFrames
{
    public static void AppendPairs(List<WindowEntry> entries, Func<long, int> usageRank)
    {
        if (entries.Count < 2) return;
        IReadOnlyList<LiveMonitor> monitors;
        try
        {
            monitors = MonitorUtil.Live();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return;
        }

        var tiles = new List<TileWindow>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.IsTilePair || entry.IsIconic || entry.Handle <= 0 || entry.MonitorNumber <= 0)
                continue;
            if (!TryExtendedFrame((nint)entry.Handle, out var frame))
                continue;
            var work = WorkArea(monitors, entry.MonitorNumber);
            if (work.Width <= 0 || work.Height <= 0) continue;
            tiles.Add(new TileWindow(
                entry.Handle,
                entry.MonitorNumber,
                new TileRect(frame.Left, frame.Top, frame.Right, frame.Bottom),
                new TileRect(work.X, work.Y, work.X + work.Width, work.Y + work.Height),
                Minimized: false,
                Visible: true,
                Cloaked: false,
                UsageRank: usageRank(entry.Handle)));
        }

        var pairs = TilePairing.Find(tiles);
        if (pairs.Count == 0) return;
        var combined = TilePairListing.Append(entries, pairs);
        entries.Clear();
        entries.AddRange(combined);
        AppLog.Info($"Tiled pairs: {pairs.Count}.");
    }

    static DisplayBounds WorkArea(IReadOnlyList<LiveMonitor> monitors, int number)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.Bounds.Number == number) return monitor.Bounds;
        }
        return default;
    }

    static bool TryExtendedFrame(nint hwnd, out NativeRect rect)
    {
        rect = default;
        if (hwnd == 0) return false;
        try
        {
            var hr = NativeMethods.DwmGetWindowAttributeRect(
                hwnd,
                NativeMethods.DwmwaExtendedFrameBounds,
                out rect,
                System.Runtime.InteropServices.Marshal.SizeOf<NativeRect>());
            if (hr == 0 && rect.Right > rect.Left && rect.Bottom > rect.Top)
                return true;
            return NativeMethods.GetWindowRect(hwnd, out rect) && rect.Right > rect.Left && rect.Bottom > rect.Top;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
    }
}
