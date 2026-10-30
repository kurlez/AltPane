namespace AltPane.Core.Dialogs;

public static class FolderPath
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var text = path.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = text[1..^1].Trim();
        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.IsFile)
            text = uri.LocalPath;
        text = text.Replace('/', '\\');
        if (text.Length == 2 && text[1] == ':')
            return text + "\\";
        if (text.Length > 3)
            text = text.TrimEnd('\\');
        if (text.Length == 2 && text[1] == ':')
            return text + "\\";
        return text;
    }

    public static bool Same(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    public static string Leaf(string? path)
    {
        var normal = Normalize(path);
        if (normal.Length == 0) return "";
        var trimmed = normal.TrimEnd('\\');
        var slash = trimmed.LastIndexOf('\\');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    public static bool IsAbsolute(string? path)
    {
        var normal = Normalize(path);
        if (normal.Length >= 3 && char.IsLetter(normal[0]) && normal[1] == ':' && normal[2] == '\\')
            return true;
        return normal.StartsWith(@"\\", StringComparison.Ordinal);
    }

    /// <summary>
    /// The dialog consumed the path we submitted. A full path match counts, and so does
    /// an address bar that changed to the folder's display name.
    /// </summary>
    public static bool Landed(string? before, string? after, string? target)
    {
        if (Same(after, target)) return true;
        var name = Leaf(target);
        if (name.Length == 0 || string.IsNullOrWhiteSpace(after)) return false;
        if (!after.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return false;
        return !string.Equals(before?.Trim(), after.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
