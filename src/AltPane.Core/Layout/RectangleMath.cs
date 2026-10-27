using AltPane.Core.Models;

namespace AltPane.Core.Layout;

public static class RectangleMath
{
    public readonly record struct RectI(int X, int Y, int Width, int Height);

    public static RectI Center(RectI area, int width, int height)
    {
        var w = Math.Min(Math.Max(1, width), Math.Max(1, area.Width));
        var h = Math.Min(Math.Max(1, height), Math.Max(1, area.Height));
        var x = area.X + (area.Width - w) / 2;
        var y = area.Y + (area.Height - h) / 2;
        return new RectI(x, y, w, h);
    }

    public static RectI DipToPhysical(int dipX, int dipY, int dipWidth, int dipHeight, double scaleX, double scaleY)
    {
        scaleX = scaleX <= 0 ? 1 : scaleX;
        scaleY = scaleY <= 0 ? 1 : scaleY;
        return new RectI(
            (int)Math.Round(dipX * scaleX),
            (int)Math.Round(dipY * scaleY),
            Math.Max(1, (int)Math.Round(dipWidth * scaleX)),
            Math.Max(1, (int)Math.Round(dipHeight * scaleY)));
    }
}

public static class SwitcherMetrics
{
    public static (int Width, int Height) GetSize(SwitcherSize size) => size switch
    {
        SwitcherSize.Compact => (420, 480),
        SwitcherSize.Large => (640, 760),
        _ => (520, 620)
    };
}
