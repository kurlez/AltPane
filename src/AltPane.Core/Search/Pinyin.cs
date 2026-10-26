using System.Reflection;
using System.Text;

namespace AltPane.Core.Search;

/// <summary>
/// Mandarin readings for CJK unified ideographs. The table is derived from
/// mozillazg/pinyin-data (MIT). The first reading is the common one; alternates
/// are kept so names such as 音乐 still match yinyue. ü is stored as v.
/// </summary>
public static class Pinyin
{
    const int BlockStart = 0x4E00;
    const int BlockEnd = 0x9FFF;
    static readonly string[] Syllables;
    static readonly ushort[] Index;

    static Pinyin()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AltPane.Core.Search.pinyin.bin");
        if (stream is null)
        {
            Syllables = Array.Empty<string>();
            Index = Array.Empty<ushort>();
            return;
        }

        using (stream)
        using (var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false))
        {
            var magic = reader.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != (byte)'P' || magic[1] != (byte)'Y')
                throw new InvalidDataException("Pinyin table is not PY01.");
            var count = reader.ReadUInt16();
            Syllables = new string[count];
            for (var i = 0; i < count; i++)
            {
                var length = reader.ReadByte();
                var bytes = reader.ReadBytes(length);
                Syllables[i] = Encoding.ASCII.GetString(bytes);
            }

            var chars = BlockEnd - BlockStart + 1;
            Index = new ushort[chars];
            for (var i = 0; i < chars; i++)
                Index[i] = reader.ReadUInt16();
        }
    }

    public static bool Matches(string? text, string query) =>
        SpellHit(text, query) || InitialsHit(text, query);

    public static bool SpellHit(string? text, string query)
    {
        if (string.IsNullOrEmpty(text) || query.Length < 2) return false;
        var needle = query.Trim().ToLowerInvariant();
        if (needle.Length < 2) return false;
        foreach (var variant in SpellVariants(text, 16))
        {
            if (variant.Contains(needle, StringComparison.Ordinal)) return true;
            // ü is stored as v, and some IMEs emit u instead.
            var asU = variant.Replace('v', 'u');
            if (!string.Equals(asU, variant, StringComparison.Ordinal) && asU.Contains(needle, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public static bool InitialsHit(string? text, string query)
    {
        if (string.IsNullOrEmpty(text) || query.Length < 2) return false;
        var groups = InitialGroups(text);
        if (query.Length > groups.Count) return false;
        for (var i = 0; i < query.Length; i++)
        {
            var wanted = char.ToLowerInvariant(query[i]);
            if (!GroupHas(groups[i], wanted)) return false;
        }
        return true;
    }

    public static string PrimarySpell(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var builder = new StringBuilder(text.Length * 2);
        foreach (var variant in SpellVariants(text, 1))
            return variant;
        return builder.ToString();
    }

    public static string PrimaryInitials(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var groups = InitialGroups(text);
        var builder = new StringBuilder(groups.Count);
        foreach (var group in groups)
        {
            if (group.Length > 0) builder.Append(group[0]);
        }
        return builder.ToString();
    }

    static bool GroupHas(string group, char wanted) =>
        group.IndexOf(wanted) >= 0
        || (wanted == 'v' && group.IndexOf('u') >= 0)
        || (wanted == 'u' && group.IndexOf('v') >= 0);

    static List<string> InitialGroups(string text)
    {
        var groups = new List<string>();
        var inWord = false;
        foreach (var ch in text)
        {
            if (IsIdeograph(ch))
            {
                inWord = false;
                var letters = "";
                foreach (var reading in Readings(ch))
                {
                    if (reading.Length == 0) continue;
                    var initial = reading[0];
                    if (letters.IndexOf(initial) < 0) letters += initial;
                }
                if (letters.Length > 0) groups.Add(letters);
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                if (!inWord)
                {
                    groups.Add(char.ToLowerInvariant(ch).ToString());
                    inWord = true;
                }
                continue;
            }

            inWord = false;
        }
        return groups;
    }

    static List<string> SpellVariants(string text, int cap)
    {
        var acc = new List<string> { "" };
        foreach (var ch in text)
        {
            IReadOnlyList<string> pieces;
            if (IsIdeograph(ch))
            {
                var readings = Readings(ch);
                if (readings.Count == 0) continue;
                pieces = readings;
            }
            else if (char.IsLetterOrDigit(ch))
            {
                pieces = new[] { char.ToLowerInvariant(ch).ToString() };
            }
            else
            {
                continue;
            }

            if (pieces.Count == 1)
            {
                for (var i = 0; i < acc.Count; i++)
                    acc[i] += pieces[0];
                continue;
            }

            var next = new List<string>(Math.Min(cap, acc.Count * pieces.Count));
            foreach (var prefix in acc)
            {
                foreach (var piece in pieces)
                {
                    next.Add(prefix + piece);
                    if (next.Count >= cap) break;
                }
                if (next.Count >= cap) break;
            }
            acc = next;
        }
        return acc;
    }

    static IReadOnlyList<string> Readings(char ch)
    {
        if (Index.Length == 0 || ch < BlockStart || ch > BlockEnd) return Array.Empty<string>();
        var idx = Index[ch - BlockStart];
        if (idx == 0 || idx > Syllables.Length) return Array.Empty<string>();
        return Syllables[idx - 1].Split(',');
    }

    static bool IsIdeograph(char ch) => ch >= BlockStart && ch <= BlockEnd;
}
