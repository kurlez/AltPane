using System.Globalization;

namespace AltPane.Core.Icons;

/// <summary>
/// Cache identity for a shell icon. Ordinary documents share one icon per extension.
/// Executables, shortcuts, icons, and internet shortcuts each have their own, so they
/// are keyed by the full path. A non-zero icon index is part of that path key.
/// </summary>
public static class IconCacheKey
{
    public static string For(string? path, int iconIndex = 0)
    {
        path = (path ?? "").Trim();
        if (IsPathSpecific(path, iconIndex))
        {
            if (iconIndex == 0) return "path:" + path;
            return "path:" + path + "\n" + iconIndex.ToString(CultureInfo.InvariantCulture);
        }

        var ext = Path.GetExtension(path);
        if (ext.Length > 0) ext = ext.ToLowerInvariant();
        return "ext:" + ext;
    }

    public static bool IsPathSpecific(string? path, int iconIndex)
    {
        if (iconIndex != 0) return true;
        if (string.IsNullOrWhiteSpace(path)) return true;
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.IndexOf('!') >= 0) return true;
        var ext = Path.GetExtension(path);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".url", StringComparison.OrdinalIgnoreCase);
    }

    public static bool SamePath(string cacheKey, string path)
    {
        if (string.IsNullOrEmpty(cacheKey) || string.IsNullOrEmpty(path)) return false;
        var marker = "path:" + path;
        if (cacheKey.Equals(marker, StringComparison.OrdinalIgnoreCase)) return true;
        return cacheKey.StartsWith(marker + "\n", StringComparison.OrdinalIgnoreCase);
    }
}
