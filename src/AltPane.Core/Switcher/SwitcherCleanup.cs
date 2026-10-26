namespace AltPane.Core.Switcher;

public enum CleanupStep
{
    UnregisterThumbnail,
    RedrawThumbnailSource,
    ReturnBorrowedPreview,
    ReleaseBorrowedForActivate,
    HideSwitcher,
    RestorePreviousForeground
}

/// <summary>
/// What has to happen when the switcher goes away, in order.
/// A thumbnail that is still registered while focus moves is what turns the
/// source window black. Borrowed minimized windows go back before the host hides.
/// </summary>
public static class SwitcherCleanup
{
    public static IReadOnlyList<CleanupStep> Steps(bool activateSelection) =>
        activateSelection
            ? new[]
            {
                CleanupStep.UnregisterThumbnail,
                CleanupStep.RedrawThumbnailSource,
                CleanupStep.ReleaseBorrowedForActivate,
                CleanupStep.HideSwitcher
            }
            : new[]
            {
                CleanupStep.UnregisterThumbnail,
                CleanupStep.RedrawThumbnailSource,
                CleanupStep.ReturnBorrowedPreview,
                CleanupStep.HideSwitcher,
                CleanupStep.RestorePreviousForeground
            };

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
        int ageMilliseconds) =>
        IsForeignDeactivate(foreground, switcher, foregroundIsHint, holdingActivation)
        && !IgnoreBounce(foreground, previousForeground, ageMilliseconds);
}
