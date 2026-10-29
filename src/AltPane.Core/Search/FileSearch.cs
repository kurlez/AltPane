using AltPane.Core.Apps;

namespace AltPane.Core.Search;

public sealed record FileHit(string FullPath, string Name, string Directory);

/// <summary>
/// The Everything search string for document results. The process sends this
/// string over the existing Everything IPC; nothing here talks to Everything.
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
        plan = new Plan(query + " " + Sort, all, text);
        return true;
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
