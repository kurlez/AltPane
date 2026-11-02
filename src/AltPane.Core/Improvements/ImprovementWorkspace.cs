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
        await GitAsync(Directory, cancellationToken, "push", "-u", "origin", branch);
        return sha;
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
