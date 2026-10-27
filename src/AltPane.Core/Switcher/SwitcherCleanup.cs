namespace AltPane.Core.Switcher;

public enum CleanupStep
{
    HideSwitcher,
    RestorePreviousForeground
}

/// <summary>
/// What has to happen when the switcher goes away, in order.
/// Every monitor's instance hides. Cancelling then restores the previous foreground window.
/// </summary>
public static class SwitcherCleanup
{
    public static IReadOnlyList<CleanupStep> Steps(bool activateSelection) =>
        activateSelection
            ? new[] { CleanupStep.HideSwitcher }
            : new[] { CleanupStep.HideSwitcher, CleanupStep.RestorePreviousForeground };

    public static bool IsForeignDeactivate(long foreground, long switcher, bool foregroundIsHint, bool holdingActivation)
    {
        if (holdingActivation) return false;
        if (foreground == 0 || foreground == switcher) return false;
        if (foregroundIsHint) return false;
        return true;
    }

    /// <summary>
    /// The first fraction of a second after show can bounce focus back to the
    /// window we replaced. That is not a click. A different window is.
    /// </summary>
    public static bool IgnoreBounce(long foreground, long previousForeground, int ageMilliseconds) =>
        ageMilliseconds >= 0
        && ageMilliseconds < 250
        && previousForeground != 0
        && foreground == previousForeground;

    public static bool ShouldDismiss(
        long foreground,
        long switcher,
        bool foregroundIsHint,
        bool holdingActivation,
        long previousForeground,
        int ageMilliseconds,
        bool foregroundIsIme = false) =>
        !foregroundIsIme
        && IsForeignDeactivate(foreground, switcher, foregroundIsHint, holdingActivation)
        && !IgnoreBounce(foreground, previousForeground, ageMilliseconds);

    /// <summary>
    /// Candidate and composition windows. Focusing one is not a click outside,
    /// and must not clear the open flag while the switcher is still visible.
    /// </summary>
    public static bool IsImeUiClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return false;
        var name = className.Trim();
        if (name.Equals("IME", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Equals("MSCTFIME UI", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Equals("CiceroUIWndFrame", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("Microsoft.IME", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
