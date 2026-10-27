using AltPane.Core.Models;

namespace AltPane.Core.Switcher;

/// <summary>
/// Orders windows by the first letter of the application name. That is the same
/// name the row badge is built from: the display process name, then the process
/// name, then the title. Chinese names use the pinyin initial. Digits and symbols
/// follow A–Z. Ties keep the original order.
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

    public static string AppName(WindowEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.DisplayProcessName)) return entry.DisplayProcessName;
        if (!string.IsNullOrWhiteSpace(entry.ProcessName)) return entry.ProcessName;
        return entry.Title ?? "";
    }

    public static Key Of(WindowEntry entry) => OfName(AppName(entry), entry.OriginalIndex);

    public static Key Of(string? title, string? fallback, int tie) =>
        OfName(string.IsNullOrWhiteSpace(title) ? fallback : title, tie);

    public static Key OfName(string? name, int tie)
    {
        var letters = NameIndex.Letters(name);
        if (letters.Length == 0)
            return new Key(string.IsNullOrWhiteSpace(name) ? 2 : 1, '\0', tie);
        var ch = letters[0];
        if (ch is >= 'a' and <= 'z') return new Key(0, char.ToUpperInvariant(ch), tie);
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
}
