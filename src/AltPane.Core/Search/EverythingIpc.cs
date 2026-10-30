using System.Text;

namespace AltPane.Core.Search;

/// <summary>
/// The file list Everything returns in <c>WM_COPYDATA</c>. A buffer that arrives
/// is a reply even when it lists nothing. A buffer that is too short to be a list
/// is not a reply.
/// </summary>
public static class EverythingIpc
{
    public static List<string>? Parse(byte[]? blob, int max)
    {
        if (blob is null || blob.Length < 8 || max <= 0) return null;
        var count = BitConverter.ToInt32(blob, 4);
        if (count == 0) return new List<string>();
        if (count < 0 || count > 10_000) return null;
        return TryItems(blob, 16, max) ?? TryItems(blob, 8, max);
    }

    static List<string>? TryItems(byte[] blob, int header, int max)
    {
        if (blob.Length < header + 12) return null;
        var count = BitConverter.ToInt32(blob, 4);
        if (count < 0 || count > 10_000) return null;
        if (header + count * 12 > blob.Length) return null;
        var list = new List<string>(Math.Min(count, max));
        for (var i = 0; i < count && list.Count < max; i++)
        {
            var at = header + i * 12;
            var nameOffset = BitConverter.ToInt32(blob, at + 4);
            var pathOffset = BitConverter.ToInt32(blob, at + 8);
            if (nameOffset < 0 || pathOffset < 0 || nameOffset >= blob.Length || pathOffset >= blob.Length)
                return null;
            var name = ReadWide(blob, nameOffset);
            var dir = ReadWide(blob, pathOffset);
            if (name.Length == 0) continue;
            list.Add(dir.Length == 0 ? name : dir.TrimEnd('\\') + "\\" + name);
        }
        return list;
    }

    static string ReadWide(byte[] blob, int offset)
    {
        var end = offset;
        while (end + 1 < blob.Length && (blob[end] != 0 || blob[end + 1] != 0))
            end += 2;
        if (end <= offset) return "";
        return Encoding.Unicode.GetString(blob, offset, end - offset);
    }
}
