namespace AltPane.Core.Switcher;

/// <summary>
/// Stable jump codes for one switcher open. The first 26 rows are a–z.
/// Later rows use two letters (aa, ab, …) so a single key still means one row.
/// </summary>
public static class RowIndex
{
    public static string Code(int position)
    {
        if (position < 0) return "";
        if (position < 26) return ((char)('a' + position)).ToString();
        var rest = position - 26;
        var hi = rest / 26;
        var lo = rest % 26;
        if (hi < 26)
            return new string(new[] { (char)('a' + hi), (char)('a' + lo) });
        var top = hi / 26;
        hi %= 26;
        return new string(new[] { (char)('a' + (top % 26)), (char)('a' + hi), (char)('a' + lo) });
    }

    public static bool Matches(string? query, string code)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrWhiteSpace(query)) return false;
        var token = query.Trim();
        if (token.IndexOf(' ') >= 0) return false;
        return string.Equals(token, code, StringComparison.OrdinalIgnoreCase);
    }
}
