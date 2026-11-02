namespace AltPane.Core.Improvements;

public enum PipelineOutcome { Built, NoChanges, AgentFailed, BuildFailed }

/// <param name="Branch">The branch pushed for this request, once one exists.</param>
/// <param name="RunUrl">The GitHub Actions run, once one exists.</param>
/// <param name="ExePath">The downloaded AltPane.exe, only when <see cref="PipelineOutcome.Built"/>.</param>
public sealed record PipelineResult(PipelineOutcome Outcome, string? Branch, string? RunUrl, string? ExePath, string Summary);

/// <summary>
/// One improvement request from start to an installable build: fresh clone state, agent edits,
/// commit and push of a branch, wait for GitHub Actions, download the executable.
/// The agent is a delegate so the Codex app-server session stays outside this class.
/// </summary>
public sealed class ImprovementPipeline
{
    public delegate Task<ImprovementResult> Agent(string prompt, Action<ImprovementEvent> onEvent, CancellationToken cancellationToken);

    readonly ImprovementWorkspace _workspace;
    readonly GitHubBuild _build;
    readonly string _downloadDirectory;
    readonly Agent _agent;

    public ImprovementPipeline(ImprovementWorkspace workspace, GitHubBuild build, string downloadDirectory, Agent agent)
    {
        _workspace = workspace;
        _build = build;
        _downloadDirectory = downloadDirectory;
        _agent = agent;
    }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan BuildTimeout { get; init; } = TimeSpan.FromMinutes(30);
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.Now;

    public async Task<PipelineResult> RunAsync(string request, ImprovementContext context, Action<ImprovementEvent> onEvent, CancellationToken cancellationToken)
    {
        var prompt = ImprovementPrompt.Build(request, context);
        void Status(string text) => onEvent(new ImprovementEvent(ImprovementEventKind.Status, text));

        Status("检查 GitHub 登录…");
        await _build.EnsureSignedInAsync(cancellationToken);
        Status("准备工作区…");
        await _workspace.PrepareAsync(cancellationToken);
        var branch = await _workspace.CreateBranchAsync(request, Clock(), cancellationToken);

        var agent = await _agent(prompt, onEvent, cancellationToken);
        if (agent.Outcome == ImprovementOutcome.Interrupted) throw new OperationCanceledException(cancellationToken);
        if (agent.Outcome == ImprovementOutcome.Failed)
            return new PipelineResult(PipelineOutcome.AgentFailed, branch, null, null, agent.Summary);

        Status("提交并推送分支 " + branch + "…");
        var sha = await _workspace.CommitAndPushAsync(branch, request, cancellationToken);
        if (sha is null)
            return new PipelineResult(PipelineOutcome.NoChanges, branch, null, null, agent.Summary);

        var run = await _build.WaitAsync(branch, sha, Status, PollInterval, BuildTimeout, cancellationToken);
        if (!run.Succeeded)
            return new PipelineResult(PipelineOutcome.BuildFailed, branch, run.Url, null,
                $"GitHub 构建未通过（{run.Conclusion}）。\n{agent.Summary}".Trim());

        Status("下载构建产物…");
        var exe = await _build.DownloadAsync(run.Id, _downloadDirectory, cancellationToken);
        return new PipelineResult(PipelineOutcome.Built, branch, run.Url, exe, agent.Summary);
    }
}
