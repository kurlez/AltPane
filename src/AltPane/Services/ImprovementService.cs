using AltPane.Core.Improvements;
using AltPane.Core.Localization;
using AltPane.Core.Models;

namespace AltPane;

/// <summary>
/// Runs improvement requests end to end: a long-lived Codex app-server session edits a private clone,
/// then AltPane pushes a branch, waits for the GitHub build and downloads it. The Codex process starts
/// on the first request, is reused by later ones, and stops after ten idle minutes.
/// </summary>
internal sealed class ImprovementService : IAsyncDisposable
{
    static readonly TimeSpan Idle = TimeSpan.FromMinutes(10);
    readonly Func<AppSettings> _settings;
    readonly ICommandRunner _commands = new ProcessCommandRunner();
    readonly SemaphoreSlim _gate = new(1, 1);
    CodexAppServerClient? _client;
    string _clientKey = "";
    Timer? _idle;

    public ImprovementService(Func<AppSettings> settings) => _settings = settings;

    public static string DownloadDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AltPane", "improve", "build");

    public async Task<PipelineResult> RunAsync(string request, ImprovementContext context, Action<ImprovementEvent> onEvent, CancellationToken cancellationToken)
    {
        ImprovementPrompt.Normalize(request);
        var settings = _settings();
        var loc = Localizer.Current;
        var repository = GitHubBuild.ParseRepository(settings.ImprovementsRemote)
            ?? throw new InvalidOperationException(loc["improve.badRemote"]);
        var executable = CodexLocator.Find(settings.CodexExecutable, Environment.GetEnvironmentVariable("PATH"), windows: true)
            ?? throw new InvalidOperationException(loc["improve.noCodex"]);
        CodexLocator.ValidateExecutable(executable);
        if (!await _gate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
            throw new InvalidOperationException("已有改进请求正在运行。");
        try
        {
            _idle?.Change(Timeout.Infinite, Timeout.Infinite);
            var workspace = new ImprovementWorkspace(_commands, ImprovementWorkspace.DefaultDirectory(), settings.ImprovementsRemote, settings.ImprovementsBaseBranch);
            var pipeline = new ImprovementPipeline(
                workspace,
                new GitHubBuild(_commands, repository),
                DownloadDirectory(),
                (prompt, emit, token) => RunAgentAsync(prompt, emit, token, executable, workspace.Directory, settings.ImprovementsFullAccess, context.Version));
            return await pipeline.RunAsync(request, context, onEvent, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _idle ??= new Timer(_ => _ = StopWhenIdleAsync(), null, Timeout.Infinite, Timeout.Infinite);
            _idle.Change(Idle, Timeout.InfiniteTimeSpan);
            _gate.Release();
        }
    }

    async Task<ImprovementResult> RunAgentAsync(string prompt, Action<ImprovementEvent> onEvent, CancellationToken cancellationToken,
        string executable, string directory, bool fullAccess, string version)
    {
        var key = $"{executable}|{directory}|{fullAccess}";
        if (_client is { IsBroken: true } || (_client is not null && key != _clientKey))
            await StopAsync().ConfigureAwait(false);
        if (_client is null)
        {
            var transport = ProcessRpcTransport.Start(executable, directory);
            _client = new CodexAppServerClient(transport, new ImprovementOptions
            {
                RepositoryPath = directory,
                FullAccess = fullAccess,
                ClientVersion = version
            });
            _clientKey = key;
        }
        try
        {
            return await _client.RunAsync(prompt, onEvent, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (_client is { IsBroken: true }) await StopAsync().ConfigureAwait(false);
        }
    }

    async Task StopWhenIdleAsync()
    {
        if (!await _gate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false)) return;
        try { await StopAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    async Task StopAsync()
    {
        var client = _client;
        _client = null;
        if (client is not null)
        {
            try { await client.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { AppLog.Error(ex); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _idle?.Dispose();
        await StopAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Synchronous shutdown for app exit. Kills the codex app-server process tree right away, without waiting
    /// on async continuations, so it cannot deadlock the UI thread.
    /// </summary>
    public void Shutdown()
    {
        try { _idle?.Dispose(); } catch (ObjectDisposedException) { }
        var client = Interlocked.Exchange(ref _client, null);
        if (client is null) return;
        try { client.Kill(); }
        catch (Exception ex) { AppLog.Error(ex); }
    }
}
