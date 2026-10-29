namespace AltPane.Core.Search;

public static class PathText
{
    public static string DirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        return Path.GetDirectoryName(path.Trim().TrimEnd('\\', '/')) ?? "";
    }

    public static string DirectoryName(string? path)
    {
        var directory = DirectoryPath(path);
        if (directory.Length == 0) return "";
        return Path.GetFileName(directory.TrimEnd('\\', '/'));
    }
}
