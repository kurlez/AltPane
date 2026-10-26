using System.Text;

namespace AltPane.Core.Apps;

/// <summary>
/// Reads the fields AltPane needs from a Shell Link (.lnk) blob. The layout follows
/// MS-SHLLINK so the indexer can be tested without Windows or IShellLink.
/// </summary>
public static class ShellLinkReader
{
    const uint HasLinkTargetIdList = 0x00000001;
    const uint HasLinkInfo = 0x00000002;
    const uint HasName = 0x00000004;
    const uint HasRelativePath = 0x00000008;
    const uint HasWorkingDir = 0x00000010;
    const uint HasArguments = 0x00000020;
    const uint HasIconLocation = 0x00000040;
    const uint IsUnicode = 0x00000080;
    const uint EnvironmentVariableSignature = 0xA0000001;
    const int HeaderSize = 0x4C;

    public readonly record struct LinkData(
        string? Name,
        string? RelativePath,
        string? WorkingDirectory,
        string? Arguments,
        string? IconLocation,
        int IconIndex,
        string? LocalBasePath,
        string? CommonPathSuffix,
        string? EnvironmentTarget);

    public static bool TryRead(ReadOnlySpan<byte> data, out LinkData link)
    {
        link = default;
        try
        {
            if (data.Length < HeaderSize) return false;
            var headerSize = BitConverter.ToUInt32(data[..4]);
            if (headerSize < HeaderSize || headerSize > data.Length) return false;
            var flags = BitConverter.ToUInt32(data.Slice(0x14, 4));
            var iconIndex = BitConverter.ToInt32(data.Slice(0x38, 4));
            var offset = (int)headerSize;

            if ((flags & HasLinkTargetIdList) != 0)
            {
                if (offset + 2 > data.Length) return false;
                var idListSize = BitConverter.ToUInt16(data.Slice(offset, 2));
                offset += 2;
                if (offset + idListSize > data.Length) return false;
                offset += idListSize;
            }

            string? local = null;
            string? suffix = null;
            if ((flags & HasLinkInfo) != 0)
            {
                if (!TryReadLinkInfo(data, ref offset, out local, out suffix)) return false;
            }

            var unicode = (flags & IsUnicode) != 0;
            string? name = null, relative = null, working = null, arguments = null, icon = null;
            if ((flags & HasName) != 0 && !TryReadString(data, ref offset, unicode, out name)) return false;
            if ((flags & HasRelativePath) != 0 && !TryReadString(data, ref offset, unicode, out relative)) return false;
            if ((flags & HasWorkingDir) != 0 && !TryReadString(data, ref offset, unicode, out working)) return false;
            if ((flags & HasArguments) != 0 && !TryReadString(data, ref offset, unicode, out arguments)) return false;
            if ((flags & HasIconLocation) != 0 && !TryReadString(data, ref offset, unicode, out icon)) return false;

            var environment = ReadEnvironmentTarget(data, offset);
            link = new LinkData(name, relative, working, arguments, icon, iconIndex, local, suffix, environment);
            return true;
        }
        catch (Exception)
        {
            link = default;
            return false;
        }
    }

    public static string? ResolveTarget(in LinkData link)
    {
        var local = Join(link.LocalBasePath, link.CommonPathSuffix);
        if (IsAbsolute(local)) return local;
        if (IsAbsolute(link.EnvironmentTarget)) return link.EnvironmentTarget;
        if (IsAbsolute(link.RelativePath)) return link.RelativePath;
        return local ?? link.EnvironmentTarget ?? link.RelativePath;
    }

    static bool TryReadLinkInfo(ReadOnlySpan<byte> data, ref int offset, out string? local, out string? suffix)
    {
        local = null;
        suffix = null;
        if (offset + 0x1C > data.Length) return false;
        var start = offset;
        var infoSize = BitConverter.ToUInt32(data.Slice(offset, 4));
        if (infoSize < 0x1C || start + infoSize > data.Length) return false;
        var headerSize = BitConverter.ToUInt32(data.Slice(offset + 4, 4));
        var localOffset = BitConverter.ToUInt32(data.Slice(offset + 16, 4));
        var suffixOffset = BitConverter.ToUInt32(data.Slice(offset + 24, 4));
        uint localUnicode = 0, suffixUnicode = 0;
        if (headerSize >= 0x24 && offset + 0x24 <= data.Length)
        {
            localUnicode = BitConverter.ToUInt32(data.Slice(offset + 28, 4));
            suffixUnicode = BitConverter.ToUInt32(data.Slice(offset + 32, 4));
        }

        var info = data.Slice(start, (int)infoSize);
        local = ReadZ(info, localUnicode, unicode: true) ?? ReadZ(info, localOffset, unicode: false);
        suffix = ReadZ(info, suffixUnicode, unicode: true) ?? ReadZ(info, suffixOffset, unicode: false);
        offset = start + (int)infoSize;
        return true;
    }

    static string? ReadZ(ReadOnlySpan<byte> info, uint fieldOffset, bool unicode)
    {
        if (fieldOffset == 0 || fieldOffset >= info.Length) return null;
        if (!unicode)
        {
            var end = (int)fieldOffset;
            while (end < info.Length && info[end] != 0) end++;
            if (end == fieldOffset) return "";
            return Encoding.ASCII.GetString(info.Slice((int)fieldOffset, end - (int)fieldOffset));
        }

        var chars = new StringBuilder();
        for (var i = (int)fieldOffset; i + 1 < info.Length; i += 2)
        {
            var unit = BitConverter.ToChar(info.Slice(i, 2));
            if (unit == '\0') break;
            chars.Append(unit);
        }
        return chars.Length == 0 ? "" : chars.ToString();
    }

    static bool TryReadString(ReadOnlySpan<byte> data, ref int offset, bool unicode, out string? value)
    {
        value = null;
        if (offset + 2 > data.Length) return false;
        var count = BitConverter.ToUInt16(data.Slice(offset, 2));
        offset += 2;
        var bytes = unicode ? count * 2 : count;
        if (offset + bytes > data.Length) return false;
        if (count == 0)
        {
            value = "";
            return true;
        }

        var slice = data.Slice(offset, bytes);
        offset += bytes;
        value = (unicode ? Encoding.Unicode.GetString(slice) : Encoding.ASCII.GetString(slice)).TrimEnd('\0');
        return true;
    }

    static string? ReadEnvironmentTarget(ReadOnlySpan<byte> data, int offset)
    {
        while (offset + 8 <= data.Length)
        {
            var blockSize = BitConverter.ToUInt32(data.Slice(offset, 4));
            if (blockSize < 4) break;
            if (offset + blockSize > data.Length) break;
            var signature = BitConverter.ToUInt32(data.Slice(offset + 4, 4));
            if (signature == EnvironmentVariableSignature && blockSize >= 0x314)
            {
                var unicodeStart = offset + 8 + 260;
                if (unicodeStart + 2 <= offset + blockSize)
                {
                    var chars = new StringBuilder();
                    for (var i = unicodeStart; i + 1 < offset + blockSize && i + 1 < unicodeStart + 520; i += 2)
                    {
                        var unit = BitConverter.ToChar(data.Slice(i, 2));
                        if (unit == '\0') break;
                        chars.Append(unit);
                    }
                    if (chars.Length > 0) return chars.ToString();
                }
            }
            offset += (int)blockSize;
        }
        return null;
    }

    static string? Join(string? local, string? suffix)
    {
        if (string.IsNullOrEmpty(local)) return string.IsNullOrEmpty(suffix) ? null : suffix;
        if (string.IsNullOrEmpty(suffix)) return local;
        if (local.EndsWith('\\') || local.EndsWith('/')) return local + suffix;
        return local + "\\" + suffix;
    }

    static bool IsAbsolute(string? path) =>
        !string.IsNullOrWhiteSpace(path) && (path.Contains(':') || path.StartsWith("\\\\", StringComparison.Ordinal));
}
