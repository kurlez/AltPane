using System.Text.Json;
using AltPane.Core.Models;

namespace AltPane.Core.Settings;

/// <summary>
/// Brings an older settings file up to <see cref="AppSettings.CurrentSchema"/>.
/// A v0.1.0 file wrote <c>captureWinTab: false</c> because that was the implicit default,
/// and it did not contain <c>stickySwitcher</c>. Those files receive the v0.2 defaults.
/// A file that already contains <c>stickySwitcher</c> was saved by v0.2 or later, so an
/// explicit off stays off. Hotkey, theme, language, exclusions, and the other choices
/// that existed in v0.1 are never rewritten.
/// Schema 5 removes showPreview, previewMinimized, and showOtherMonitorHints.
/// The deserializer ignores those keys. Saving writes the file without them.
/// Schema 6 moves the initial-letter sort (the previous default, including files
/// that schema 4 forced onto it) to stable grouped. Recent, CPU, and memory stay.
/// Schema 7 keeps that mode and changes what it does: focus time, with empty pin
/// and demote lists and a 10 minute threshold when those keys are missing. A stored
/// stable-grouped value is the same mode. An explicit recent, app name, CPU, or
/// memory choice is not rewritten.
/// Schema 8 adds the frequent-apps sidebar. It is shown, with 8 slots, and the
/// sidebar pin and hide lists are empty when those keys are missing. An explicit
/// choice saved at schema 8 is left as it was.
/// </summary>
public static class SettingsMigration
{
    public static bool Apply(JsonElement root, AppSettings settings)
    {
        var stored = 1;
        if (root.TryGetProperty("version", out var versionNode) && versionNode.TryGetInt32(out var parsed) && parsed > 0)
            stored = parsed;

        var changed = false;
        var preV2 = stored < 2 && !Has(root, "stickySwitcher");
        if (preV2)
        {
            settings.CaptureWinTab = true;
            settings.StickySwitcher = true;
            settings.ShowLoad = true;
            settings.ConfirmEndTask = true;
            settings.EndTaskKillsTree = true;
            settings.LaunchApps = true;
            settings.IndexStartMenu = true;
            settings.IndexStoreApps = true;
            settings.IndexDesktop = true;
            settings.ShowMonitorBadges = true;
            changed = true;
        }

        if (stored < 4)
        {
            settings.SortMode = LoadSortMode.Initial;
            changed = true;
        }

        if (stored < 6 && settings.SortMode == LoadSortMode.Initial)
        {
            settings.SortMode = LoadSortMode.FocusTime;
            changed = true;
        }

        if (stored < 8)
        {
            if (!Has(root, "showFrequentSidebar"))
                settings.ShowFrequentSidebar = true;
            if (!Has(root, "frequentSidebarCount") || settings.FrequentSidebarCount is < 6 or > 8)
                settings.FrequentSidebarCount = 8;
            settings.SidebarPinnedApps ??= new List<string>();
            settings.SidebarHiddenApps ??= new List<string>();
            changed = true;
        }

        if (stored < AppSettings.CurrentSchema)
            changed = true;
        return changed;
    }

    static bool Has(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
