namespace AltPane.Core.Search;

/// <summary>
/// Folders left out of file search. These are the agent work directories that
/// sit under the user profile, plus <c>node_modules</c>. The same list is added
/// to the Everything query as <c>!path:</c> terms and applied again when rows
/// are built, so a hit still drops out if the engine returns it.
/// Settings can add more folders through <c>EverythingExcludedPaths</c>.
/// </summary>
public static class FileSearchExclusions
{
    public static readonly string[] Terms =
    {
        ".gemini",
        ".claude",
        ".cursor",
        ".codex",
        ".copilot",
        ".continue",
        ".aider*",
        ".ollama",
        ".lmstudio",
        ".windsurf",
        ".codeium",
        ".qwen",
        ".kimi",
        ".trae",
        ".vscode\\extensions",
        "node_modules"
    };

    public static string Append(string query)
    {
        if (string.IsNullOrEmpty(query)) return query;
        var parts = new List<string> { query };
        foreach (var term in Terms)
        {
            var clause = "!path:" + term;
            if (query.Contains(clause, StringComparison.OrdinalIgnoreCase)) continue;
            parts.Add(clause);
        }
        return string.Join(' ', parts);
    }

    public static bool IsExcluded(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var parts = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        foreach (var term in Terms)
        {
            if (Matches(parts, term)) return true;
        }
        return false;
    }

    static bool Matches(string[] parts, string term)
    {
        var chunks = term.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (chunks.Length == 0 || chunks.Length > parts.Length) return false;
        for (var i = 0; i + chunks.Length <= parts.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < chunks.Length; j++)
            {
                if (!Segment(parts[i + j], chunks[j]))
                {
                    matched = false;
                    break;
                }
            }
            if (matched) return true;
        }
        return false;
    }

    static bool Segment(string value, string pattern)
    {
        if (pattern.EndsWith('*') && pattern.Length > 1)
        {
            var prefix = pattern[..^1];
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        return value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
