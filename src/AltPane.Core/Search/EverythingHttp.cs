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
            + "/?search=" + Uri.EscapeDataString(search ?? "")
            + "&json=1&path_column=1&count=" + take;
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
