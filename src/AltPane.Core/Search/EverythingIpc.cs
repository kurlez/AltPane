using System.Text;

namespace AltPane.Core.Search;

/// <summary>
/// Everything IPC uses the Unicode <c>EVERYTHING_IPC_QUERYW</c> /
/// <c>EVERYTHING_IPC_LISTW</c> structures only. The ANSI query
/// (<c>EVERYTHING_IPC_COPYDATAQUERYA</c>, value 1) is never sent.
/// A list is a reply only when it is a <c>LISTW</c> buffer. The query we send
/// is not a list, and a short or echoed buffer is not a reply.
/// </summary>
public static class EverythingIpc
{
    public const int QueryA = 1;
    public const int QueryW = 2;
    public const int ListHeaderBytes = 28;
    public const int ItemBytes = 12;

    /// <summary>
    /// dwData Everything must use when it posts the result list. Zero would
    /// collide with unrelated <c>WM_COPYDATA</c> traffic.
    /// </summary>
    public const int ReplyMessage = 0x41504E31;

    public static byte[] BuildQuery(uint replyWindow, string? search, int max)
    {
        var chars = Encoding.Unicode.GetBytes((search ?? "") + "\0");
        var payload = new byte[20 + chars.Length];
        BitConverter.GetBytes(replyWindow).CopyTo(payload, 0);
        BitConverter.GetBytes(ReplyMessage).CopyTo(payload, 4);
        BitConverter.GetBytes(0).CopyTo(payload, 8);
        BitConverter.GetBytes(0).CopyTo(payload, 12);
        BitConverter.GetBytes(max < 0 ? 0 : max).CopyTo(payload, 16);
        chars.CopyTo(payload, 20);
        return payload;
    }

    /// <summary>
    /// Paths from an <c>EVERYTHING_IPC_LISTW</c> buffer. <c>numitems == 0</c> is a
    /// reply. Anything that is not that header is not a reply.
    /// </summary>
    public static List<string>? Parse(byte[]? blob, int max)
    {
        if (blob is null || blob.Length < ListHeaderBytes || max <= 0) return null;
        var numFolders = BitConverter.ToInt32(blob, 12);
        var numFiles = BitConverter.ToInt32(blob, 16);
        var numItems = BitConverter.ToInt32(blob, 20);
        if (numItems < 0 || numItems > 10_000) return null;
        if (numFolders < 0 || numFiles < 0 || numFolders > numItems || numFiles > numItems) return null;
        if (numFolders + numFiles != numItems) return null;
        if (numItems == 0) return new List<string>();
        if (ListHeaderBytes + numItems * ItemBytes > blob.Length) return null;

        var list = new List<string>(Math.Min(numItems, max));
        for (var i = 0; i < numItems && list.Count < max; i++)
        {
            var at = ListHeaderBytes + i * ItemBytes;
            var nameOffset = BitConverter.ToInt32(blob, at + 4);
            var pathOffset = BitConverter.ToInt32(blob, at + 8);
            if (nameOffset < 0 || pathOffset < 0 || nameOffset >= blob.Length || pathOffset >= blob.Length)
                return null;
            var name = ReadWide(blob, nameOffset);
            var dir = ReadWide(blob, pathOffset);
            if (name.Length == 0 && dir.Length == 0) continue;
            if (name.Length == 0)
            {
                list.Add(dir);
                continue;
            }
            list.Add(dir.Length == 0 ? name : dir.TrimEnd('\\') + "\\" + name);
        }
        return list;
    }

    public static byte[] BuildList(IReadOnlyList<(string Directory, string Name)> items)
    {
        var encoded = new List<(byte[] Directory, byte[] Name)>(items.Count);
        var textBytes = 0;
        foreach (var item in items)
        {
            var directory = Encoding.Unicode.GetBytes((item.Directory ?? "") + "\0");
            var name = Encoding.Unicode.GetBytes((item.Name ?? "") + "\0");
            encoded.Add((directory, name));
            textBytes += directory.Length + name.Length;
        }

        var blob = new byte[ListHeaderBytes + items.Count * ItemBytes + textBytes];
        var folders = 0;
        var files = items.Count;
        BitConverter.GetBytes(folders).CopyTo(blob, 0);
        BitConverter.GetBytes(files).CopyTo(blob, 4);
        BitConverter.GetBytes(files).CopyTo(blob, 8);
        BitConverter.GetBytes(folders).CopyTo(blob, 12);
        BitConverter.GetBytes(files).CopyTo(blob, 16);
        BitConverter.GetBytes(items.Count).CopyTo(blob, 20);
        var cursor = ListHeaderBytes + items.Count * ItemBytes;
        for (var i = 0; i < encoded.Count; i++)
        {
            var at = ListHeaderBytes + i * ItemBytes;
            var nameAt = cursor;
            encoded[i].Name.CopyTo(blob, cursor);
            cursor += encoded[i].Name.Length;
            var dirAt = cursor;
            encoded[i].Directory.CopyTo(blob, cursor);
            cursor += encoded[i].Directory.Length;
            BitConverter.GetBytes(nameAt).CopyTo(blob, at + 4);
            BitConverter.GetBytes(dirAt).CopyTo(blob, at + 8);
        }
        return blob;
    }

    static string ReadWide(byte[] blob, int offset)
    {
        var end = offset;
        while (end + 1 < blob.Length && (blob[end] != 0 || blob[end + 1] != 0))
            end += 2;
        if ((end - offset) % 2 != 0) end--;
        if (end <= offset) return "";
        return Encoding.Unicode.GetString(blob, offset, end - offset);
    }
}
