using AltPane.Core.Layout;
using AltPane.Core.Models;

namespace AltPane.Core.Dialogs;

/// <summary>
/// Places the compact file-dialog search bar in physical pixels. The bar is a
/// single line on the dialog's bottom edge, left or right, outside the dialog
/// when the work area has room so it does not cover the filename box or the
/// buttons. The folder list opens upward from that bar.
/// </summary>
public static class DialogPanelLayout
{
    public const int BarWidthDip = 280;
    public const int BarHeightDip = 32;
    public const int RowHeightDip = 30;
    public const int MaxVisibleRows = 8;
    public const int EmptyListDip = 36;
    public const int CornerInsetDip = 8;
    /// <summary>Kept clear of the Open/Cancel row when the bar has to sit inside the dialog.</summary>
    public const int ButtonReserveDip = 148;

    public readonly record struct Slot(int X, int Y, int Width, int Height, bool Flipped);

    public static Slot Dock(
        RectangleMath.RectI dialog,
        RectangleMath.RectI work,
        FileDialogPanelEdge edge,
        int barWidth,
        int barHeight,
        int gap,
        int inset,
        int buttonReserve)
    {
        barWidth = Math.Max(1, barWidth);
        barHeight = Math.Max(1, barHeight);
        gap = Math.Max(0, gap);
        inset = Math.Max(0, inset);
        buttonReserve = Math.Max(0, buttonReserve);
        var area = work.Width <= 0 || work.Height <= 0
            ? new RectangleMath.RectI(dialog.X, dialog.Y, Math.Max(dialog.Width, barWidth), Math.Max(dialog.Height, barHeight))
            : work;

        var dialogWidth = Math.Max(0, dialog.Width);
        var span = dialogWidth - inset * 2;
        var width = span >= 80 ? Math.Min(barWidth, span) : Math.Min(barWidth, Math.Max(1, dialogWidth));
        width = Math.Min(width, Math.Max(1, area.Width));
        var edgeInset = span >= 80 ? inset : 0;
        var rightAligned = edge == FileDialogPanelEdge.Right;
        var x = rightAligned
            ? dialog.X + dialogWidth - edgeInset - width
            : dialog.X + edgeInset;

        var dialogBottom = dialog.Y + Math.Max(0, dialog.Height);
        var below = dialogBottom + gap;
        var roomBelow = below + barHeight <= area.Y + area.Height;
        var flipped = !roomBelow;
        var y = flipped ? dialogBottom - edgeInset - barHeight : below;
        if (flipped && y < dialog.Y) y = dialog.Y;
        if (flipped && rightAligned && buttonReserve > 0)
        {
            var room = dialogWidth - edgeInset * 2 - buttonReserve;
            if (room >= 80)
            {
                width = Math.Min(width, room);
                var shifted = dialog.X + dialogWidth - edgeInset - buttonReserve - width;
                if (shifted >= dialog.X + edgeInset) x = shifted;
            }
        }

        x = Clamp(x, area.X, area.X + area.Width - width);
        y = Clamp(y, area.Y, area.Y + area.Height - barHeight);
        return new Slot(x, y, width, barHeight, flipped);
    }

    /// <summary>
    /// Grows the window upward from the bar so the search line stays put and the
    /// list hangs above it. The list shrinks when the work area cannot fit it.
    /// </summary>
    public static Slot ExpandUp(Slot bar, RectangleMath.RectI work, int listHeight)
    {
        listHeight = Math.Max(0, listHeight);
        var bottom = bar.Y + bar.Height;
        var height = bar.Height + listHeight;
        var y = bottom - height;
        var areaTop = work.Height <= 0 ? y : work.Y;
        var areaBottom = work.Height <= 0 ? bottom : work.Y + work.Height;
        if (y < areaTop) y = areaTop;
        if (bottom > areaBottom) bottom = areaBottom;
        if (bottom < y + bar.Height) bottom = y + bar.Height;
        height = bottom - y;
        return new Slot(bar.X, y, bar.Width, height, bar.Flipped);
    }

    static int Clamp(int value, int min, int max)
    {
        if (max < min) return min;
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
