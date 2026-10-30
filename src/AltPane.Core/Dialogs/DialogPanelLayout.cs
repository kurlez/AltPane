using AltPane.Core.Layout;
using AltPane.Core.Models;

namespace AltPane.Core.Dialogs;

/// <summary>
/// Docks the quick-jump panel to a file dialog in physical pixels, inside the
/// monitor work area. The panel flips to the opposite edge when it would not fit.
/// </summary>
public static class DialogPanelLayout
{
    public readonly record struct Slot(int X, int Y, int Width, int Height, bool Flipped);

    public static Slot Dock(RectangleMath.RectI dialog, RectangleMath.RectI work, FileDialogPanelEdge edge, int thickness, int gap)
    {
        thickness = Math.Max(1, thickness);
        gap = Math.Max(0, gap);
        var area = work.Width <= 0 || work.Height <= 0
            ? new RectangleMath.RectI(dialog.X, dialog.Y, Math.Max(dialog.Width, thickness), Math.Max(dialog.Height, thickness))
            : work;
        return edge == FileDialogPanelEdge.Right
            ? DockSide(dialog, area, thickness, gap, horizontal: true)
            : DockSide(dialog, area, thickness, gap, horizontal: false);
    }

    static Slot DockSide(RectangleMath.RectI dialog, RectangleMath.RectI work, int thickness, int gap, bool horizontal)
    {
        if (!horizontal)
        {
            var width = ClampSize(dialog.Width, work.Width);
            var height = Math.Min(thickness, Math.Max(1, work.Height));
            var x = Clamp(dialog.X, work.X, work.X + work.Width - width);
            var below = dialog.Y + Math.Max(0, dialog.Height) + gap;
            var above = dialog.Y - gap - height;
            var roomBelow = below + height <= work.Y + work.Height;
            var roomAbove = above >= work.Y;
            var flipped = !roomBelow && roomAbove;
            var y = flipped ? above : below;
            y = Clamp(y, work.Y, work.Y + work.Height - height);
            return new Slot(x, y, width, height, flipped);
        }

        var panelHeight = ClampSize(dialog.Height, work.Height);
        var panelWidth = Math.Min(thickness, Math.Max(1, work.Width));
        var ySide = Clamp(dialog.Y, work.Y, work.Y + work.Height - panelHeight);
        var right = dialog.X + Math.Max(0, dialog.Width) + gap;
        var left = dialog.X - gap - panelWidth;
        var roomRight = right + panelWidth <= work.X + work.Width;
        var roomLeft = left >= work.X;
        var flippedSide = !roomRight && roomLeft;
        var xSide = flippedSide ? left : right;
        xSide = Clamp(xSide, work.X, work.X + work.Width - panelWidth);
        return new Slot(xSide, ySide, panelWidth, panelHeight, flippedSide);
    }

    static int ClampSize(int preferred, int limit)
    {
        var cap = Math.Max(1, limit);
        var size = preferred <= 0 ? cap : preferred;
        return Math.Min(size, cap);
    }

    static int Clamp(int value, int min, int max)
    {
        if (max < min) return min;
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
