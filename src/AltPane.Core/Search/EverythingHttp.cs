using System.Text;
using System.Text.Json;

namespace AltPane.Core.Search;

/// <summary>
/// Everything's built-in HTTP server. The query string is the documented
/// <c>/?search=…&amp;json=1&amp;path_column=1&amp;count=N</c> form. Parsing stays
/// here so a test can map a JSON body onto file paths without opening a socket.
/// </summary>
public static class EverythingHttp
{
    public const int DefaultPort = 80;
    public const int FallbackPort = 8080;

    public static IReadOnlyList<int> Ports(int primary, bool tryFallback)
    {
        var port = primary is >= 1 and <= 65535 ? primary : DefaultPort;
        if (!tryFallback || port == FallbackPort) return new[] { port };
        return new[] { port, FallbackPort };
    }

    public static string BuildUrl(int port, string search, int count)
    {
        var chosen = port is >= 1 and <= 65535 ? port : DefaultPort;
        var take = Math.Clamp(count, 1, 1000);
        return "http://127.0.0.1:" + chosen
            + "/?search=" + EscapeUtf8(search)
            + "&json=1&path_column=1&count=" + take;
    }

    /// <summary>Percent-encode the search as UTF-8. Chinese must not become %3F.</summary>
    public static string EscapeUtf8(string? search) => Uri.EscapeDataString(search ?? "");

    /// <summary>
    /// Everything's HTTP JSON is UTF-8. A missing charset, or a legacy label such as
    /// us-ascii or iso-8859-1, still decodes as UTF-8 so a Chinese path is not replaced
    /// with '?'. An explicit Unicode charset is honored. A BOM wins over the label.
    /// </summary>
    public static string Decode(byte[]? body, string? charset)
    {
        if (body is null || body.Length == 0) return "";
        if (body.Length >= 3 && body[0] == 0xEF && body[1] == 0xBB && body[2] == 0xBF)
            return Encoding.UTF8.GetString(body, 3, body.Length - 3);
        if (body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE)
            return Encoding.Unicode.GetString(body, 2, body.Length - 2);
        if (body.Length >= 2 && body[0] == 0xFE && body[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(body, 2, body.Length - 2);
        return Select(charset).GetString(body);
    }

    public static Encoding Select(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset)) return Encoding.UTF8;
        var name = charset.Trim().Trim('"');
        if (name.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || name.Equals("utf8", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8;
        if (name.Equals("utf-16", StringComparison.OrdinalIgnoreCase) || name.Equals("utf-16le", StringComparison.OrdinalIgnoreCase))
            return Encoding.Unicode;
        if (name.Equals("utf-16be", StringComparison.OrdinalIgnoreCase))
            return Encoding.BigEndianUnicode;
        return Encoding.UTF8;
    }

    /// <summary>
    /// Paths from an Everything JSON body. An empty <c>results</c> array is a
    /// reply (no matches). Malformed JSON is not a reply.
    /// </summary>
    public static IReadOnlyList<string>? Parse(string? json, int max)
    {
        if (string.IsNullOrWhiteSpace(json) || max <= 0) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!TryProperty(document.RootElement, "results", out var results)
                || results.ValueKind != JsonValueKind.Array)
                return null;
            var list = new List<string>();
            foreach (var item in results.EnumerateArray())
            {
                if (list.Count >= max) break;
                if (item.ValueKind != JsonValueKind.Object) continue;
                var full = FullPath(item);
                if (full.Length > 0) list.Add(full);
            }
            return list;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string FullPath(JsonElement item)
    {
        var name = Text(item, "name") ?? Text(item, "filename") ?? "";
        var directory = Text(item, "path") ?? Text(item, "location") ?? "";
        name = name.Trim().Trim('"').Replace('/', '\\');
        directory = directory.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
        if (name.Length == 0) return directory;
        if (directory.Length == 0) return name;
        if (directory.EndsWith("\\" + name, StringComparison.OrdinalIgnoreCase)
            || directory.Equals(name, StringComparison.OrdinalIgnoreCase))
            return directory;
        return directory + "\\" + name;
    }

    static string? Text(JsonElement item, string name)
    {
        if (!TryProperty(item, name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    static bool TryProperty(JsonElement item, string name, out JsonElement value)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
