namespace AltPane.Core.Settings;

/// <summary>
/// Resolves the executable that should be recorded for startup and elevation.
/// A winget portable alias, or any other symlink, is followed to the real file.
/// </summary>
public static class AppPaths
{
    public static string ResolveExecutable(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath)) return "";
        string full;
        try
        {
            full = Path.GetFullPath(processPath);
        }
        catch
        {
            return processPath;
        }

        try
        {
            var link = File.ResolveLinkTarget(full, returnFinalTarget: true);
            if (link is null) return full;
            var target = link.FullName;
            if (!Path.IsPathRooted(target))
            {
                var directory = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(directory))
                    target = Path.GetFullPath(Path.Combine(directory, target));
            }
            return File.Exists(target) ? target : full;
        }
        catch
        {
            return full;
        }
    }
}
