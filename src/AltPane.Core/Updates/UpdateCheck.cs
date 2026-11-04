using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using AltPane.Core.Versioning;

namespace AltPane.Core.Updates;

/// <summary>The latest GitHub release and the two assets an update needs.</summary>
public sealed record ReleaseInfo(string Version, string Tag, string HtmlUrl, string ZipName, string ZipUrl, string? Sha256Url);

/// <summary>Finds, downloads and verifies a newer AltPane release from GitHub.</summary>
public static class UpdateCheck
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/kurlez/AltPane/releases/latest";
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>Parses releases/latest. Null for drafts, prereleases, unparsable tags, or a release without the zip.</summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
            var tag = Text(root, "tag_name");
            if (!ProductVersion.TryParse(tag, out var version)) return null;
            var display = ProductVersion.Display(version);
            var zipName = $"AltPane-{display}-win-x64.zip";
            string? zipUrl = null, shaUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = Text(asset, "name");
                    var url = Text(asset, "browser_download_url");
                    if (name is null || url is null || !IsGitHubDownload(url)) continue;
                    if (string.Equals(name, zipName, StringComparison.OrdinalIgnoreCase)) zipUrl = url;
                    else if (string.Equals(name, zipName + ".sha256", StringComparison.OrdinalIgnoreCase)) shaUrl = url;
                }
            }
            if (zipUrl is null) return null;
            return new ReleaseInfo(display, tag!, Text(root, "html_url") ?? "", zipName, zipUrl, shaUrl);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static bool IsGitHubDownload(string url) =>
        url.StartsWith("https://github.com/kurlez/AltPane/releases/download/", StringComparison.OrdinalIgnoreCase);

    static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public static bool IsNewer(string current, ReleaseInfo release) =>
        ProductVersion.TryParse(current, out _) && ProductVersion.Compare(release.Version, current) > 0;

    /// <summary>The hash from a "hash  filename" line (sha256sum / release.yml format). Null when there is none.</summary>
    public static string? ParseSha256File(string? text, string? expectedName = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var raw in text.Split('\n'))
        {
            var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var hash = parts[0].ToLowerInvariant();
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) continue;
            if (expectedName is not null && parts.Length > 1 && !string.Equals(parts[1].TrimStart('*'), expectedName, StringComparison.OrdinalIgnoreCase))
                continue;
            return hash;
        }
        return null;
    }

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Throws (and deletes the file) unless its SHA256 equals <paramref name="expected"/>.</summary>
    public static void Verify(string path, string? expected)
    {
        if (string.IsNullOrEmpty(expected) || expected.Length != 64)
        {
            TryDelete(path);
            throw new InvalidOperationException("发布中没有有效的 SHA256 校验值，已停止更新。");
        }
        var actual = Sha256Of(path);
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return;
        TryDelete(path);
        throw new InvalidOperationException($"SHA256 校验失败（期望 {expected}，实际 {actual}），已删除下载的文件，不会安装。");
    }

    /// <summary>Extracts AltPane.exe from the release zip into <paramref name="directory"/> and returns its path.</summary>
    public static string ExtractExecutable(string zipPath, string directory)
    {
        Directory.CreateDirectory(directory);
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, "AltPane.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("更新包中没有 AltPane.exe。");
        var target = Path.Combine(directory, "AltPane.exe");
        entry.ExtractToFile(target, overwrite: true);
        return target;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>HTTP side of the update: read releases/latest, download the zip and its checksum, verify, extract.</summary>
public sealed class UpdateClient
{
    readonly HttpClient _http;

    public UpdateClient(HttpClient http, string userAgentVersion)
    {
        _http = http;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AltPane", userAgentVersion));
    }

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateCheck.LatestReleaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return UpdateCheck.ParseRelease(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Downloads and verifies the release, then returns the extracted AltPane.exe.</summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, string directory, CancellationToken cancellationToken)
    {
        if (release.Sha256Url is null)
            throw new InvalidOperationException("该版本没有 .sha256 校验文件，已停止更新。");
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Directory.CreateDirectory(directory);
        var shaText = await _http.GetStringAsync(release.Sha256Url, cancellationToken).ConfigureAwait(false);
        var expected = UpdateCheck.ParseSha256File(shaText, release.ZipName);
        var zip = Path.Combine(directory, release.ZipName);
        using (var response = await _http.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(zip);
            await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }
        UpdateCheck.Verify(zip, expected);
        return UpdateCheck.ExtractExecutable(zip, Path.Combine(directory, "extract"));
    }
}
