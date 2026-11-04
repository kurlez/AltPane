using System.Text;
using System.Text.RegularExpressions;

namespace AltPane.Core.Requests;

public enum RequestFailure { None, GhMissing, NotSignedIn, NoPermission, Other }

public sealed class RequestException : Exception
{
    public RequestException(RequestFailure failure, string message) : base(message) => Failure = failure;
    public RequestFailure Failure { get; }
}

/// <summary>Version and OS lines appended to every request so the change can be matched to a build.</summary>
public sealed record RequestContext(string AppVersion, string OsVersion);

/// <summary>
/// A feature request filed as a GitHub issue with the gh CLI. The issue is labeled and assigned to the
/// signed-in account, which is what the maintainer's automation listens for.
/// </summary>
public static class FeatureRequest
{
    public const string Repository = "kurlez/AltPane";
    public const string Label = "altpane-request";
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 20000;
    public const string FooterMarker = "<!-- altpane-request -->";

    public static (string Title, string Description) Normalize(string? title, string? description)
    {
        var t = Regex.Replace((title ?? "").Trim(), @"\s+", " ");
        var d = (description ?? "").Replace("\r\n", "\n").Trim();
        if (t.Length == 0) throw new ArgumentException("请填写标题。");
        if (t.Length > MaxTitleLength) throw new ArgumentException($"标题过长，请限制在 {MaxTitleLength} 个字符以内。");
        if (d.Length == 0) throw new ArgumentException("请描述想要的改动。");
        if (d.Length > MaxDescriptionLength) throw new ArgumentException($"描述过长，请限制在 {MaxDescriptionLength} 个字符以内。");
        return (t, d);
    }

    public static string BuildBody(string description, RequestContext context)
    {
        var builder = new StringBuilder();
        builder.Append(description.Trim()).Append("\n\n---\n");
        builder.Append(FooterMarker).Append('\n');
        builder.Append("- AltPane: ").Append(context.AppVersion).Append('\n');
        builder.Append("- OS: ").Append(context.OsVersion).Append('\n');
        builder.Append("- 来源：AltPane 托盘菜单「提需求…」\n");
        return builder.ToString();
    }

    /// <summary>Arguments for gh issue create. The body travels in a file, never on the command line.</summary>
    public static IReadOnlyList<string> CreateIssueArguments(string title, string bodyFile) => new[]
    {
        "issue", "create",
        "--repo", Repository,
        "--title", title,
        "--body-file", bodyFile,
        "--label", Label,
        "--assignee", "@me"
    };

    public static IReadOnlyList<string> CreateLabelArguments() => new[]
    {
        "label", "create", Label,
        "--repo", Repository,
        "--color", "1D76DB",
        "--description", "Requested from AltPane",
        "--force"
    };

    /// <summary>The issue URL gh prints on success (the last github.com issue link in its output).</summary>
    public static string? ParseIssueUrl(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var matches = Regex.Matches(output, @"https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/issues/\d+");
        return matches.Count == 0 ? null : matches[^1].Value;
    }

    public static bool IsPermissionError(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        foreach (var marker in new[]
        {
            "HTTP 403", "HTTP 404", "403 Forbidden", "Resource not accessible", "must have", "does not have the correct permissions",
            "not have permission", "permission denied", "Could not resolve to a Repository", "not authorized", "could not assign"
        })
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsAuthError(string? message)
    {
        if (string.IsNullOrEmpty(message)) return false;
        foreach (var marker in new[] { "gh auth login", "not logged in", "HTTP 401", "Bad credentials", "authentication required" })
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static string Message(RequestFailure failure, string? detail = null)
    {
        var text = failure switch
        {
            RequestFailure.GhMissing => "未找到 GitHub CLI（gh）。请先安装：winget install GitHub.cli，然后重新打开 AltPane。",
            RequestFailure.NotSignedIn => "GitHub CLI 尚未登录。请在终端中执行 gh auth login，登录后再提交。",
            RequestFailure.NoPermission => $"当前 GitHub 账号没有在 {Repository} 创建带标签和指派人的 issue 的权限。请确认 gh auth login 使用的是有权限的账号（例如仓库所有者或协作者）。",
            _ => "提交失败。"
        };
        detail = detail?.Trim();
        if (!string.IsNullOrEmpty(detail))
        {
            if (detail.Length > 400) detail = detail[..400];
            text += "\n" + detail;
        }
        return text;
    }
}

/// <summary>Files a request with gh: check gh and its sign-in, make sure the label exists, then create the issue.</summary>
public sealed class FeatureRequestSubmitter
{
    readonly ICommandRunner _runner;
    readonly string _gh;

    public FeatureRequestSubmitter(ICommandRunner runner, string gh = "gh")
    {
        _runner = runner;
        _gh = gh;
    }

    /// <summary>Returns the new issue URL. Throws <see cref="RequestException"/> with a Chinese message on failure.</summary>
    public async Task<string> SubmitAsync(string title, string description, RequestContext context, CancellationToken cancellationToken)
    {
        var (t, d) = FeatureRequest.Normalize(title, description);
        CommandResult auth;
        try
        {
            auth = await _runner.RunAsync(_gh, new[] { "auth", "status", "--hostname", "github.com" }, null, cancellationToken).ConfigureAwait(false);
        }
        catch (CommandNotFoundException)
        {
            throw new RequestException(RequestFailure.GhMissing, FeatureRequest.Message(RequestFailure.GhMissing));
        }
        if (!auth.Ok)
            throw new RequestException(RequestFailure.NotSignedIn, FeatureRequest.Message(RequestFailure.NotSignedIn));

        // The label may already exist, or this account may not be allowed to create it; issue create reports the real problem.
        try { await _runner.RunAsync(_gh, FeatureRequest.CreateLabelArguments(), null, cancellationToken).ConfigureAwait(false); }
        catch (CommandNotFoundException) { }

        var bodyFile = Path.Combine(Path.GetTempPath(), "altpane-request-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            await File.WriteAllTextAsync(bodyFile, FeatureRequest.BuildBody(d, context), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            var result = await _runner.RunAsync(_gh, FeatureRequest.CreateIssueArguments(t, bodyFile), null, cancellationToken).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            if (!result.Ok)
            {
                if (FeatureRequest.IsAuthError(detail))
                    throw new RequestException(RequestFailure.NotSignedIn, FeatureRequest.Message(RequestFailure.NotSignedIn, detail));
                if (FeatureRequest.IsPermissionError(detail))
                    throw new RequestException(RequestFailure.NoPermission, FeatureRequest.Message(RequestFailure.NoPermission, detail));
                throw new RequestException(RequestFailure.Other, FeatureRequest.Message(RequestFailure.Other, detail));
            }
            return FeatureRequest.ParseIssueUrl(result.Output) ?? FeatureRequest.ParseIssueUrl(result.Error)
                ?? throw new RequestException(RequestFailure.Other, FeatureRequest.Message(RequestFailure.Other, "gh 没有返回 issue 链接。" + detail));
        }
        catch (CommandNotFoundException)
        {
            throw new RequestException(RequestFailure.GhMissing, FeatureRequest.Message(RequestFailure.GhMissing));
        }
        finally
        {
            try { File.Delete(bodyFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
