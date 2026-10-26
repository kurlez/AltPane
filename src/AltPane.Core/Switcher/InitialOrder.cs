using AltPane.Core.Models;
using AltPane.Core.Search;

namespace AltPane.Core.Switcher;

/// <summary>
/// Orders windows by the first letter of the title. Chinese titles use the
/// pinyin initial. Digits and symbols follow A–Z. Ties keep the original order.
/// </summary>
public static class InitialOrder
{
    public readonly record struct Key(int Bucket, char Letter, int Tie);

    public static List<WindowEntry> Sort(IReadOnlyList<WindowEntry> items)
    {
        var ranked = new List<WindowEntry>(items);
        ranked.Sort(static (a, b) => Compare(Of(a), Of(b)));
        return ranked;
    }

    public static Key Of(WindowEntry entry) =>
        Of(entry.Title, string.IsNullOrWhiteSpace(entry.DisplayProcessName) ? entry.ProcessName : entry.DisplayProcessName, entry.OriginalIndex);

    public static Key Of(string? title, string? fallback, int tie)
    {
        var text = string.IsNullOrWhiteSpace(title) ? fallback ?? "" : title!;
        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        if (start >= text.Length) return new Key(2, '\0', tie);
        var ch = text[start];
        if (IsLatin(ch)) return new Key(0, char.ToUpperInvariant(ch), tie);
        if (ch is >= '\u4E00' and <= '\u9FFF')
        {
            var initials = Pinyin.PrimaryInitials(ch.ToString());
            if (initials.Length > 0 && IsLatin(initials[0]))
                return new Key(0, char.ToUpperInvariant(initials[0]), tie);
        }
        return new Key(1, ch, tie);
    }

    public static int Compare(Key left, Key right)
    {
        var bucket = left.Bucket.CompareTo(right.Bucket);
        if (bucket != 0) return bucket;
        var letter = left.Letter.CompareTo(right.Letter);
        if (letter != 0) return letter;
        return left.Tie.CompareTo(right.Tie);
    }

    static bool IsLatin(char ch) => ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
