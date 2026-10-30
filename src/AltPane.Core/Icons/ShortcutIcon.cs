using AltPane.Core.Apps;

namespace AltPane.Core.Icons;

/// <summary>
/// The shortcut AltPane should use when the process image has no usable icon.
/// A custom icon location wins over the .lnk file itself.
/// </summary>
public static class ShortcutIcon
{
    public readonly record struct Location(string Path, int Index);

    public static Location? Match(string? exePath, IEnumerable<AppEntry>? apps)
    {
        if (string.IsNullOrWhiteSpace(exePath) || apps is null) return null;
        Location? lnk = null;
        foreach (var app in apps)
        {
            if (app.Kind != AppKind.Shortcut) continue;
            var same = string.Equals(app.ResolvedPath, exePath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(app.LaunchPath, exePath, StringComparison.OrdinalIgnoreCase);
            if (!same) continue;
            if (!string.IsNullOrWhiteSpace(app.IconLocation)
                && !string.Equals(app.IconLocation, exePath, StringComparison.OrdinalIgnoreCase))
                return new Location(app.IconLocation, app.IconIndex);
            if (lnk is null && !string.IsNullOrWhiteSpace(app.LaunchPath))
                lnk = new Location(app.LaunchPath, 0);
        }

        return lnk;
    }
}
