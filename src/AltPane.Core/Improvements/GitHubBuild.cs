using System.Text.Json;

namespace AltPane.Core.Improvements;

public sealed record BuildRun(long Id, string HeadSha, string Status, string? Conclusion, string Url)
{
    public bool Completed => Status == "completed";
    public bool Succeeded => Completed && Conclusion == "success";
}

/// <summary>Follows the GitHub Actions build of a pushed commit and downloads its executable, using the gh CLI's sign-in.</summary>
public sealed class GitHubBuild
{
    public const string Workflow = "build.yml";
    public const string ArtifactName = "AltPane-win-x64";
    readonly ICommandRunner _runner;
    readonly string _repository;

    public GitHubBuild(ICommandRunner runner, string repository)
    {
        _runner = runner;
        _repository = repository;
    }

    /// <summary>"owner/name" from an https or ssh GitHub remote. Null when it is not a GitHub remote.</summary>
    public static string? ParseRepository(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote)) return null;
        var text = remote.Trim();
        foreach (var prefix in new[] { "https://github.com/", "http://github.com/", "git@github.com:", "ssh://git@github.com/" })
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var path = text[prefix.Length..].Trim('/');
            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
            var parts = path.Split('/');
            return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0 ? path : null;
        }
        return null;
    }

    public async Task EnsureSignedInAsync(CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync("gh", new[] { "auth", "status" }, null, cancellationToken);
        if (!result.Ok)
            throw new InvalidOperationException("GitHub CLI 未登录。请先安装 gh 并执行 gh auth login。");
    }

    public static List<BuildRun> ParseRuns(string json)
    {
        var runs = new List<BuildRun>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return runs;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("databaseId", out var id) || id.ValueKind != JsonValueKind.Number)
                    continue;
                runs.Add(new BuildRun(
                    id.GetInt64(),
                    Text(item, "headSha") ?? "",
                    Text(item, "status") ?? "",
                    Text(item, "conclusion"),
                    Text(item, "url") ?? ""));
            }
        }
        catch (JsonException)
        {
            // Treated as no runs yet.
        }
        return runs;
    }

    static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public async Task<BuildRun?> FindRunAsync(string branch, string sha, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync("gh", new[]
        {
            "run", "list", "--repo", _repository, "--workflow", Workflow, "--branch", branch,
            "--limit", "10", "--json", "databaseId,headSha,status,conclusion,url"
        }, null, cancellationToken);
        if (!result.Ok) throw new InvalidOperationException("读取 GitHub 构建状态失败：" + result.Error.Trim());
        return ParseRuns(result.Output).FirstOrDefault(r => string.Equals(r.HeadSha, sha, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Waits for the run of this commit to finish. Throws <see cref="TimeoutException"/> when it takes too long.</summary>
    public async Task<BuildRun> WaitAsync(string branch, string sha, Action<string> log, TimeSpan poll, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var last = "";
        while (true)
        {
            var run = await FindRunAsync(branch, sha, cancellationToken);
            var message = run is null ? "等待 GitHub Actions 启动…" : run.Completed ? "构建已结束：" + run.Conclusion : "GitHub 正在构建（" + run.Status + "）…";
            if (message != last) { log(message); last = message; }
            if (run is { Completed: true }) return run;
            if (DateTimeOffset.UtcNow - started > timeout)
                throw new TimeoutException("GitHub 构建超过时限，已停止等待。");
            await Task.Delay(poll, cancellationToken);
        }
    }

    /// <summary>Downloads the build's executable into an empty folder and returns its path.</summary>
    public async Task<string> DownloadAsync(long runId, string directory, CancellationToken cancellationToken)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Directory.CreateDirectory(directory);
        var result = await _runner.RunAsync("gh", new[]
        {
            "run", "download", runId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repo", _repository, "--name", ArtifactName, "--dir", directory
        }, null, cancellationToken);
        if (!result.Ok) throw new InvalidOperationException("下载构建产物失败：" + result.Error.Trim());
        var exe = Path.Combine(directory, "AltPane.exe");
        if (!File.Exists(exe)) throw new InvalidOperationException("构建产物中没有 AltPane.exe。");
        return exe;
    }
}
