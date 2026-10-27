namespace AltPane.Core.Switcher;

/// <summary>
/// Jump codes taken from an application name. Latin names contribute their letters
/// (Chrome → chrome). Each CJK character contributes its pinyin initial (微信 → wx).
/// The badge is the shortest prefix that no other name shares. Identical names get
/// a numeric suffix (wx1, wx2). Codes stay lowercase.
/// </summary>
public readonly record struct CodedName(string Letters, int Minimum);

public static class NameIndex
{
    /// <summary>
    /// Latin names may shrink to one letter. A name with a CJK character keeps every
    /// pinyin initial, so 微信 is wx rather than w.
    /// </summary>
    public static CodedName Describe(string? name)
    {
        var letters = Letters(name);
        var cjk = false;
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var ch in name)
            {
                if (ch is >= '\u4E00' and <= '\u9FFF')
                {
                    cjk = true;
                    break;
                }
            }
        }

        var minimum = letters.Length == 0 ? 0 : cjk ? letters.Length : 1;
        return new CodedName(letters, minimum);
    }

    public static string Letters(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            if (ch is >= '\u4E00' and <= '\u9FFF')
            {
                var initial = AltPane.Core.Search.Pinyin.PrimaryInitials(ch.ToString());
                if (initial.Length > 0) builder.Append(char.ToLowerInvariant(initial[0]));
                continue;
            }

            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
                builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }

    public static bool Matches(string? query, string code)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrWhiteSpace(query)) return false;
        var token = query.Trim();
        if (token.IndexOf(' ') >= 0) return false;
        return string.Equals(token, code, StringComparison.OrdinalIgnoreCase);
    }

    public static string[] Assign(IReadOnlyList<string> streams, IReadOnlyCollection<string>? reserved = null)
    {
        var coded = new CodedName[streams.Count];
        for (var i = 0; i < streams.Count; i++)
        {
            var text = streams[i] ?? "";
            coded[i] = new CodedName(text, text.Length == 0 ? 0 : 1);
        }
        return Assign(coded, reserved);
    }

    public static string[] Assign(IReadOnlyList<CodedName> streams, IReadOnlyCollection<string>? reserved = null)
    {
        var codes = new string[streams.Count];
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (reserved is not null)
        {
            foreach (var code in reserved)
            {
                if (!string.IsNullOrEmpty(code)) taken.Add(code);
            }
        }

        var pending = new List<int>();
        for (var i = 0; i < streams.Count; i++)
        {
            var code = Shortest(streams[i], streams, i, taken);
            if (code is null)
            {
                pending.Add(i);
                continue;
            }

            codes[i] = code;
            taken.Add(code);
        }

        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var index in pending)
        {
            var stream = streams[index].Letters ?? "";
            if (!groups.TryGetValue(stream, out var list))
            {
                list = new List<int>();
                groups[stream] = list;
            }
            list.Add(index);
        }

        foreach (var pair in groups)
        {
            var n = 1;
            foreach (var index in pair.Value)
            {
                string code;
                do
                {
                    code = pair.Key.Length == 0 ? n.ToString() : pair.Key + n;
                    n++;
                }
                while (taken.Contains(code));
                codes[index] = code;
                taken.Add(code);
            }
        }

        return codes;
    }

    /// <summary>
    /// A code for one new row. Existing badges are left alone.
    /// </summary>
    public static string AssignOne(CodedName stream, IReadOnlyCollection<string> taken)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var text = stream.Letters ?? "";
        var start = text.Length == 0 ? 1 : Math.Clamp(stream.Minimum, 1, text.Length);
        if (text.Length > 0)
        {
            for (var len = start; len <= text.Length; len++)
            {
                var prefix = text[..len];
                if (!used.Contains(prefix)) return prefix;
            }
        }

        var n = 1;
        while (true)
        {
            var code = text.Length == 0 ? n.ToString() : text + n;
            if (!used.Contains(code)) return code;
            n++;
        }
    }

    static string? Shortest(CodedName stream, IReadOnlyList<CodedName> streams, int self, HashSet<string> taken)
    {
        var text = stream.Letters ?? "";
        if (text.Length == 0 || stream.Minimum <= 0) return null;
        var start = Math.Min(stream.Minimum, text.Length);
        for (var len = start; len <= text.Length; len++)
        {
            var prefix = text[..len];
            if (taken.Contains(prefix)) continue;
            var clash = false;
            for (var other = 0; other < streams.Count; other++)
            {
                if (other == self) continue;
                if ((streams[other].Letters ?? "").StartsWith(prefix, StringComparison.Ordinal))
                {
                    clash = true;
                    break;
                }
            }
            if (!clash) return prefix;
        }
        return null;
    }
}
