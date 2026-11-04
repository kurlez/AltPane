using AltPane.Core.Models;

namespace AltPane.Core.Switcher;

/// <summary>
/// Decides which palette the switcher window uses. The rest of AltPane keeps
/// following <see cref="AppTheme"/>; only the switcher can be pinned to dark.
/// </summary>
public static class SwitcherAppearance
{
    public static bool IsDark(SwitcherStyle style, bool appThemeIsDark) => style switch
    {
        SwitcherStyle.FollowTheme => appThemeIsDark,
        _ => true
    };

    /// <summary>Name of the palette file under Themes/ for that choice.</summary>
    public static string PaletteName(bool dark) => dark ? "Dark" : "Light";
}
