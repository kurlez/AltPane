using System.Globalization;

namespace AltPane.Core.Icons;

public static class IconColor
{
    public static bool IsTransparent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        return text.Trim().Equals("transparent", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParse(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (IsTransparent(text)) return false;
        var hex = text!.Trim();
        if (hex[0] == '#') hex = hex[1..];
        if (hex.Length == 8) hex = hex[2..];
        if (hex.Length != 6) return false;
        if (!byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)) return false;
        if (!byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)) return false;
        if (!byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b)) return false;
        return true;
    }
}
