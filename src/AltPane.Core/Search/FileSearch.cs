using AltPane.Core.Apps;

namespace AltPane.Core.Search;

public sealed record FileHit(string FullPath, string Name, string Directory);

public enum FileSearchHint
{
    None = 0,
    NotRunning,
    IpcFailed,
    Http,
    /// <summary>Rows came from the built-in index because Everything was not reachable.</summary>
    Builtin,
    /// <summary>Rows came from Everything over IPC or the command-line client.</summary>
    Everything
}

/// <summary>
/// The Everything search string for document results, and the row model those
/// paths become. Nothing here opens a socket or a window.
/// </summary>
public static class FileSearch
{
    public const int MaxResults = 20;
    public const string Sort = "sort:run-count";

    public static readonly string[] DefaultDocuments =
    {
        "doc", "docx", "xls", "xlsx", "ppt", "pptx", "pdf", "txt", "md", "csv",
        "rtf", "odt", "ods", "odp", "wps", "et", "dps", "ofd", "epub"
    };

    public static string DefaultList => string.Join(',', DefaultDocuments);

    public readonly record struct Plan(string Query, bool AllTypes, string Text);

    public static bool TryPlan(string? raw, string? documentExtensions, IReadOnlyList<string>? excludedPaths, out Plan plan)
    {
        plan = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var text = raw.TrimStart();
        var all = false;
        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            all = true;
            text = text[5..].Trim();
        }
        else if (text.Length >= 2 && (text[0] == 'f' || text[0] == 'F') && char.IsWhiteSpace(text[1]))
        {
            all = true;
            text = text[2..].Trim();
        }
        else
            text = text.Trim();
        if (text.Length == 0) return false;
        var extensions = all ? null : Split(documentExtensions);
        var query = EverythingIndex.Build(text, extensions, excludedPaths, allowAnyExtension: all);
        if (query is null) return false;
        plan = new Plan(FileSearchExclusions.Append(query) + " " + Sort, all, text);
        return true;
    }

    public static bool IsExcluded(string? path) => FileSearchExclusions.IsExcluded(path);

    public static IReadOnlyList<FileHit> ToHits(IEnumerable<string>? paths)
    {
        var list = new List<FileHit>();
        if (paths is null) return list;
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw) || IsExcluded(raw)) continue;
            var full = raw.Trim().Trim('"').Replace('/', '\\');
            var name = FileName(full);
            if (name.Length == 0) continue;
            list.Add(new FileHit(full, name, DirectoryOf(full)));
            if (list.Count >= MaxResults) break;
        }
        return list;
    }

    static string FileName(string path)
    {
        var cut = path.LastIndexOf('\\');
        return cut >= 0 ? path[(cut + 1)..] : path;
    }

    static string DirectoryOf(string path)
    {
        var cut = path.LastIndexOf('\\');
        return cut > 0 ? path[..cut] : "";
    }

    public static IReadOnlyList<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DefaultDocuments;
        var list = new List<string>();
        foreach (var part in text.Split(new[] { ',', ';', ' ', '\r', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var ext = part.Trim().TrimStart('.').ToLowerInvariant();
            if (ext.Length == 0 || list.Contains(ext)) continue;
            list.Add(ext);
        }
        return list.Count == 0 ? DefaultDocuments : list;
    }
}
