using System.Text;

namespace AltPane.Core.Improvements;

/// <summary>
/// A private clone AltPane keeps for improvement requests, so the user's own checkout is never touched.
/// The agent only edits files here. AltPane itself runs git: it resets to the base branch, makes a branch
/// per request, then commits and pushes, which GitHub Actions turns into a build.
/// </summary>
public sealed class ImprovementWorkspace
{
    public const string CommitFooter = "Requested from AltPane's Improve window.";
    readonly ICommandRunner _runner;
    readonly string _remote;
    readonly string _baseBranch;

    public ImprovementWorkspace(ICommandRunner runner, string directory, string remote, string baseBranch)
    {
        _runner = runner;
        Directory = directory;
        _remote = remote;
        _baseBranch = baseBranch;
    }

    public string Directory { get; }

    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AltPane", "improve", "repo");

    /// <summary>Clones on first use, then discards leftovers and checks out a fresh copy of the base branch.</summary>
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (!System.IO.Directory.Exists(Path.Combine(Directory, ".git")))
        {
            var parent = Path.GetDirectoryName(Directory);
            if (!string.IsNullOrEmpty(parent)) System.IO.Directory.CreateDirectory(parent);
            await GitAsync(parent, cancellationToken, "clone", _remote, Directory);
        }
        else
        {
            await GitAsync(Directory, cancellationToken, "reset", "--hard");
            await GitAsync(Directory, cancellationToken, "clean", "-fd");
            // improvementsRemote may have changed (for example to a fork) since the clone was made.
            await GitAsync(Directory, cancellationToken, "remote", "set-url", "origin", _remote);
            await GitAsync(Directory, cancellationToken, "fetch", "origin");
        }
        await GitAsync(Directory, cancellationToken, "checkout", "-B", _baseBranch, "origin/" + _baseBranch);
    }

    public async Task<string> CreateBranchAsync(string request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var name = BranchName(request, now);
        await GitAsync(Directory, cancellationToken, "checkout", "-b", name);
        return name;
    }

    /// <summary>Commits and pushes what the agent changed. Returns the commit id, or null when nothing changed.</summary>
    public async Task<string?> CommitAndPushAsync(string branch, string request, CancellationToken cancellationToken)
    {
        await GitAsync(Directory, cancellationToken, "add", "-A");
        var status = await GitAsync(Directory, cancellationToken, "status", "--porcelain");
        if (string.IsNullOrWhiteSpace(status)) return null;
        var configured = await _runner.RunAsync("git", new[] { "config", "user.email" }, Directory, cancellationToken);
        var commit = new List<string>();
        if (string.IsNullOrWhiteSpace(configured.Output))
            commit.AddRange(new[] { "-c", "user.name=AltPane", "-c", "user.email=altpane@users.noreply.github.com" });
        commit.AddRange(new[] { "commit", "-m", CommitMessage(request) });
        await GitAsync(Directory, cancellationToken, commit.ToArray());
        var sha = (await GitAsync(Directory, cancellationToken, "rev-parse", "HEAD")).Trim();
        var push = await _runner.RunAsync("git", new[] { "push", "-u", "origin", branch }, Directory, cancellationToken);
        if (!push.Ok)
        {
            var detail = (string.IsNullOrWhiteSpace(push.Error) ? push.Output : push.Error).Trim();
            if (detail.Length > 500) detail = detail[..500];
            if (IsPermissionError(detail))
                throw new InvalidOperationException(
                    $"没有推送到 {_remote} 的权限。请在 GitHub 上 fork AltPane，然后把 settings.json 中的 improvementsRemote 改成你的 fork 地址（例如 https://github.com/<你的用户名>/AltPane.git），并确认 gh auth login 使用的账号可以推送。\n{detail}");
            throw new InvalidOperationException($"git push 失败：{detail}");
        }
        return sha;
    }

    /// <summary>True when git's push error means the signed-in account may not write to the remote.</summary>
    public static bool IsPermissionError(string message)
    {
        foreach (var marker in new[] { "403", "Permission to", "permission denied", "Permission denied", "not allowed to push", "Authentication failed", "could not read Username", "Write access to repository not granted" })
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    async Task<string> GitAsync(string? directory, CancellationToken cancellationToken, params string[] arguments)
    {
        var result = await _runner.RunAsync("git", arguments, directory, cancellationToken);
        if (result.Ok) return result.Output;
        var detail = (string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim();
        if (detail.Length > 500) detail = detail[..500];
        var verb = arguments.SkipWhile(a => a == "-c" || a.StartsWith("user.", StringComparison.Ordinal)).FirstOrDefault() ?? "";
        throw new InvalidOperationException($"git {verb} 失败：{detail}");
    }

    public static string BranchName(string request, DateTimeOffset now) =>
        "improve/" + now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Slug(request);

    /// <summary>Lowercase ASCII words from the request, at most 30 characters; "change" when there are none (for example Chinese text).</summary>
    public static string Slug(string request)
    {
        var builder = new StringBuilder();
        foreach (var c in request)
        {
            if (builder.Length >= 30) break;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') builder.Append(c);
            else if (c is >= 'A' and <= 'Z') builder.Append(char.ToLowerInvariant(c));
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "change" : slug;
    }

    public static string CommitMessage(string request)
    {
        var text = request.Trim();
        var first = text.Split('\n')[0].Trim();
        if (first.Length > 60) first = first[..60].TrimEnd() + "…";
        return $"Improve: {first}\n\n{text}\n\n{CommitFooter}";
    }
}
