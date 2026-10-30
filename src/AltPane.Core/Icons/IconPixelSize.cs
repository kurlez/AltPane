namespace AltPane.Core.Icons;

/// <summary>
/// Device pixels to request so a row icon stays sharp. The list draws about 22 DIP,
/// so 32 CSS pixels times the monitor scale is the floor. The jumbo system image
/// list (256) is used when that floor is above the 48px extra-large list.
/// </summary>
public static class IconPixelSize
{
    public const int Minimum = 32;
    public const int Jumbo = 256;

    public static int ForDpi(int dpi)
    {
        if (dpi < 96) dpi = 96;
        var scaled = (int)Math.Ceiling(Minimum * (dpi / 96d));
        return Clamp(scaled);
    }

    public static int ForScale(double scale)
    {
        if (scale < 1) scale = 1;
        if (double.IsNaN(scale) || double.IsInfinity(scale)) scale = 1;
        return Clamp((int)Math.Ceiling(Minimum * scale));
    }

    public static bool UseJumbo(int edge) => edge > 48;

    static int Clamp(int size)
    {
        if (size < Minimum) return Minimum;
        if (size > Jumbo) return Jumbo;
        return size;
    }
}
