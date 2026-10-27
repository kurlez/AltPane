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
