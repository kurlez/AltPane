namespace AltPane.Core.Interop;

/// <summary>
/// Borderless-window frame that DWM can round and shadow without painting black.
/// A margin of -1 extends the glass sheet over the whole client area. With
/// <c>AllowsTransparency</c> off, any pixel the app leaves transparent is black,
/// which is what the rounded corners and the frame edge were showing.
/// </summary>
public static class WindowFrame
{
    public const int BorderColorAttribute = 34;
    public const int CaptionColorAttribute = 35;
    public const int CornerPreferenceRound = 2;
    public const int BackdropNone = 1;

    public readonly record struct FrameMargins(int Left, int Right, int Top, int Bottom);

    /// <summary>
    /// One physical pixel on the bottom edge is what DWM needs to attach a shadow.
    /// The client paints over that pixel, so it is not a visible band.
    /// </summary>
    public static FrameMargins ShadowMargins { get; } = new(0, 0, 0, 1);

    public static bool IsSheetOfGlass(FrameMargins margins) =>
        margins.Left < 0 || margins.Right < 0 || margins.Top < 0 || margins.Bottom < 0;

    public static string WindowBackgroundHex(bool dark) => dark ? "#FF1B1C22" : "#FFF7F7F8";

    public static string EdgeHex(bool dark) => dark ? "#FFB9C6FF" : "#FF1E3AAD";

    /// <summary>Inner hairline. The opposite luminance of the accent, so the ring reads on light and dark desktops.</summary>
    public static string EdgeInnerHex(bool dark) => dark ? "#FF101114" : "#FFFFFFFF";

    public static string PreviewWellHex(bool dark) => dark ? "#FF121318" : "#FFE6E7EC";

    /// <summary>COLORREF, 0x00BBGGRR, for DWMWA_BORDER_COLOR.</summary>
    public static int BorderColor(bool dark) => ColorRef(EdgeHex(dark));

    /// <summary>COLORREF for DWMWA_CAPTION_COLOR. Matches the opaque window fill.</summary>
    public static int CaptionColor(bool dark) => ColorRef(WindowBackgroundHex(dark));

    public static int ColorRef(string rgbHex)
    {
        var hex = rgbHex.TrimStart('#');
        if (hex.Length == 8) hex = hex[2..];
        var packed = Convert.ToInt32(hex, 16);
        var r = (packed >> 16) & 0xFF;
        var g = (packed >> 8) & 0xFF;
        var b = packed & 0xFF;
        return (b << 16) | (g << 8) | r;
    }
}
