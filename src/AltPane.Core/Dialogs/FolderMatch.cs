using AltPane.Core.Search;

namespace AltPane.Core.Dialogs;

public enum FolderOrigin
{
    Explorer,
    Recent,
    Search
}

public readonly record struct FolderSuggestion(string Path, string Name, FolderOrigin Origin);

/// <summary>
/// Folder matching for the dialog panel. Terms are AND. A term hits a case-insensitive
/// substring of the folder name or the full path, or pinyin initials of the name.
/// </summary>
public static class FolderMatch
{
    public static string EverythingQuery(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return "folder:" + text.Trim();
    }

    public static int Score(string? query, string? path)
    {
        var normal = FolderPath.Normalize(path);
        if (normal.Length == 0) return -1;
        if (string.IsNullOrWhiteSpace(query)) return 0;
        var name = FolderPath.Leaf(normal);
        var worst = 0;
        foreach (var term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var score = ScoreTerm(name, normal, term);
            if (score < 0) return -1;
            if (score > worst) worst = score;
        }
        return worst;
    }

    public static bool Matches(string? query, string? path) => Score(query, path) >= 0;

    public static IReadOnlyList<string> SearchIndex(FileIndex index, string? query, IReadOnlyList<string>? excluded, int limit)
    {
        if (index.DirectoryCount == 0 || limit <= 0 || string.IsNullOrWhiteSpace(query))
            return Array.Empty<string>();
        var ranked = new List<(int Score, string Path)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < index.DirectoryCount; i++)
        {
            var path = FolderPath.Normalize(index.Directory(i));
            if (path.Length == 0 || !seen.Add(path)) continue;
            if (FileIndexPaths.IsExcluded(path, excluded)) continue;
            var score = Score(query, path);
            if (score < 0) continue;
            ranked.Add((score, path));
        }

        ranked.Sort(static (a, b) =>
        {
            var byScore = a.Score.CompareTo(b.Score);
            return byScore != 0 ? byScore : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
        });
        if (ranked.Count > limit) ranked.RemoveRange(limit, ranked.Count - limit);
        var result = new List<string>(ranked.Count);
        foreach (var item in ranked) result.Add(item.Path);
        return result;
    }

    public static IReadOnlyList<string> Merge(IReadOnlyList<string>? primary, IReadOnlyList<string>? secondary, int limit)
    {
        var result = new List<string>();
        if (limit <= 0) return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(IReadOnlyList<string>? paths)
        {
            if (paths is null) return;
            foreach (var path in paths)
            {
                if (result.Count >= limit) return;
                var normal = FolderPath.Normalize(path);
                if (normal.Length == 0 || !seen.Add(normal)) continue;
                result.Add(normal);
            }
        }
        Add(primary);
        Add(secondary);
        return result;
    }

    public static IReadOnlyList<FolderSuggestion> Compose(
        string? query,
        IReadOnlyList<string>? explorer,
        IReadOnlyList<string>? recent,
        IReadOnlyList<string>? matches,
        int limit = 16)
    {
        if (limit <= 0) return Array.Empty<FolderSuggestion>();
        var ranked = new List<(int Score, int Origin, string Path)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Consider(IReadOnlyList<string>? paths, FolderOrigin origin)
        {
            if (paths is null) return;
            foreach (var path in paths)
            {
                var normal = FolderPath.Normalize(path);
                if (normal.Length == 0 || !seen.Add(normal)) continue;
                var score = Score(query, normal);
                if (score < 0) continue;
                ranked.Add((score, (int)origin, normal));
            }
        }

        Consider(explorer, FolderOrigin.Explorer);
        Consider(recent, FolderOrigin.Recent);
        if (!string.IsNullOrWhiteSpace(query))
            Consider(matches, FolderOrigin.Search);

        ranked.Sort(static (a, b) =>
        {
            var byScore = a.Score.CompareTo(b.Score);
            if (byScore != 0) return byScore;
            var byOrigin = a.Origin.CompareTo(b.Origin);
            if (byOrigin != 0) return byOrigin;
            return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
        });
        if (ranked.Count > limit) ranked.RemoveRange(limit, ranked.Count - limit);
        var result = new List<FolderSuggestion>(ranked.Count);
        foreach (var item in ranked)
            result.Add(new FolderSuggestion(item.Path, FolderPath.Leaf(item.Path), (FolderOrigin)item.Origin));
        return result;
    }

    static int ScoreTerm(string name, string path, string term)
    {
        if (name.Equals(term, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains(term, StringComparison.OrdinalIgnoreCase)) return 2;
        if (path.Contains(term, StringComparison.OrdinalIgnoreCase)) return 3;
        if (term.Length >= 2 && (Pinyin.InitialsHit(name, term) || Pinyin.SpellHit(name, term))) return 4;
        return -1;
    }
}
