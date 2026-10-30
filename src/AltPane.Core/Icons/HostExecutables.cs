namespace AltPane.Core.Icons;

/// <summary>
/// Processes whose windows do not own the icon in the executable.
/// Chromium hosts share one exe across many apps. ApplicationFrameHost
/// and WWAHost only frame a packaged app.
/// </summary>
public static class HostExecutables
{
    public static bool SharesHost(string? processName)
    {
        var name = Stem(processName);
        return name is "chrome" or "msedge" or "brave" or "opera" or "vivaldi"
            or "chromium" or "firefox" or "msedgewebview2";
    }

    public static bool IsFrameHost(string? processName)
    {
        var name = Stem(processName);
        return name is "applicationframehost" or "wwahost";
    }

    public static bool IsFramePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return IsFrameHost(FileStem(path));
    }

    /// <summary>
    /// Last path segment without an extension. Both separators count, so a Windows
    /// path keeps its file name when the check runs off Windows.
    /// </summary>
    public static string FileStem(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var text = path.Trim().TrimEnd('\\', '/');
        var slash = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));
        var name = slash >= 0 ? text[(slash + 1)..] : text;
        var dot = name.LastIndexOf('.');
        if (dot > 0) name = name[..dot];
        return name;
    }

    public static string Stem(string? processName)
    {
        var name = (processName ?? "").Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Length > 4)
            name = name[..^4];
        return name.ToLowerInvariant();
    }
}
