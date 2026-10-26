namespace AltPane.Core.Search;

public static class SearchMatcher
{
    public const int NoMatch = -1;

    public static int Score(string? query, string? title, string? process)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;
        var worst = 0;
        var any = false;
        foreach (var token in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var tokenScore = ScoreToken(token, title, process);
            if (tokenScore < 0) return NoMatch;
            any = true;
            if (tokenScore > worst) worst = tokenScore;
        }
        return any ? worst : 0;
    }

    public static bool Matches(string? query, string? title, string? process) =>
        Score(query, title, process) >= 0;

    public static List<T> Rank<T>(
        IReadOnlyList<T> items,
        string? query,
        Func<T, string?> title,
        Func<T, string?> process,
        Func<T, int> order)
    {
        var ranked = new List<(T Item, int Score, int Order)>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var score = Score(query, title(item), process(item));
            if (score < 0) continue;
            ranked.Add((item, score, order(item)));
        }

        ranked.Sort(static (a, b) =>
        {
            var byScore = a.Score.CompareTo(b.Score);
            return byScore != 0 ? byScore : a.Order.CompareTo(b.Order);
        });

        var result = new List<T>(ranked.Count);
        foreach (var entry in ranked) result.Add(entry.Item);
        return result;
    }

    public static bool IsAcronym(string? text, string query)
    {
        if (string.IsNullOrEmpty(text) || query.Length < 2) return false;
        var index = 0;
        var atWordStart = true;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (atWordStart)
                {
                    if (index < query.Length && char.ToUpperInvariant(ch) == char.ToUpperInvariant(query[index]))
                        index++;
                    atWordStart = false;
                }
            }
            else
            {
                atWordStart = true;
            }
        }
        return index == query.Length;
    }

    public static bool IsDenseSubsequence(string? text, string query)
    {
        if (string.IsNullOrEmpty(text) || query.Length < 2) return false;
        var start = -1;
        var last = -1;
        var queryIndex = 0;
        var index = 0;
        foreach (var ch in text)
        {
            if (queryIndex < query.Length && char.ToUpperInvariant(ch) == char.ToUpperInvariant(query[queryIndex]))
            {
                if (queryIndex == 0) start = index;
                last = index;
                queryIndex++;
            }
            index++;
        }
        if (queryIndex != query.Length || start < 0) return false;
        return last - start + 1 <= query.Length * 2;
    }

    static int ScoreToken(string token, string? title, string? process)
    {
        if (Contains(title, token)) return 0;
        if (Contains(process, token)) return 1;
        if (token.Length >= 2 && (IsAcronym(title, token) || IsDenseSubsequence(title, token))) return 2;
        if (token.Length >= 2 && (IsAcronym(process, token) || IsDenseSubsequence(process, token))) return 3;
        return NoMatch;
    }

    static bool Contains(string? text, string token) =>
        !string.IsNullOrEmpty(text) && text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
