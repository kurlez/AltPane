using System.Globalization;

namespace AltPane.Core.Icons;

/// <summary>
/// System.AppUserModel.RelaunchIconResource is <c>Path,index</c>. A negative index
/// is a resource id. A quoted path may contain commas.
/// </summary>
public static class RelaunchIcon
{
    public readonly record struct Resource(string Path, int Index);

    public static bool TryParse(string? text, out Resource resource)
    {
        resource = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var raw = text.Trim();
        if (raw[0] == '"')
        {
            var end = raw.IndexOf('"', 1);
            if (end <= 1) return false;
            var path = raw[1..end].Trim();
            if (path.Length == 0) return false;
            var index = 0;
            var rest = raw[(end + 1)..].Trim();
            if (rest.StartsWith(','))
                int.TryParse(rest[1..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
            resource = new Resource(path, index);
            return true;
        }

        var comma = raw.LastIndexOf(',');
        if (comma > 0 && int.TryParse(raw[(comma + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            var path = raw[..comma].Trim().Trim('"');
            if (path.Length == 0) return false;
            resource = new Resource(path, parsed);
            return true;
        }

        resource = new Resource(raw.Trim('"'), 0);
        return resource.Path.Length > 0;
    }
}
