using System.Globalization;

namespace AltPane.Core.Versioning;

/// <summary>
/// Major.Minor.Patch display and ordering. The patch may be two digits: 0.4.10
/// sorts after 0.4.9, and the assembly version is 0.4.10.0. A lexical sort of the
/// same strings would put 0.4.10 first.
/// </summary>
public static class ProductVersion
{
    public const string Fallback = "0.4.16";

    public static string Display(Version? version, string fallback = Fallback)
    {
        if (version is null) return fallback;
        if (version.Build < 0) return version.ToString();
        return string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}");
    }

    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var body = text.Trim();
        if (body.Length > 1 && (body[0] == 'v' || body[0] == 'V'))
            body = body[1..];
        var dash = body.IndexOf('-');
        if (dash >= 0) body = body[..dash];
        var parts = body.Split('.');
        if (parts.Length < 3 || parts.Length > 4) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)) return false;
        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch)) return false;
        var revision = 0;
        if (parts.Length == 4 && !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out revision))
            return false;
        version = new Version(major, minor, patch, revision);
        return true;
    }

    public static int Compare(string? left, string? right)
    {
        var okLeft = TryParse(left, out var a);
        var okRight = TryParse(right, out var b);
        if (okLeft && okRight) return a.CompareTo(b);
        return string.Compare(left, right, StringComparison.Ordinal);
    }

    public static string AssemblyVersion(string? text)
    {
        if (!TryParse(text, out var version)) return "0.0.0.0";
        return string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");
    }
}
