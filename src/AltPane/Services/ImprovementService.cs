using AltPane.Core.Improvements;
using AltPane.Core.Models;

namespace AltPane;

/// <summary>
/// Owns the long-lived Codex app-server session for improvement requests. The process starts on the
/// first request, is reused by later ones, and is stopped after ten idle minutes so a tray app costs nothing at rest.
/// </summary>
internal sealed class ImprovementService : IAsyncDisposable
{
    static readonly TimeSpan Idle = TimeSpan.FromMinutes(10);
    readonly Func<AppSettings> _settings;
    readonly SemaphoreSlim _gate = new(1, 1);
    CodexAppServerClient? _client;
    string _clientKey = "";
    Timer? _idle;

    public ImprovementService(Func<AppSettings> settings) => _settings = settings;

    public string? ResolveRepository()
    {
        var configured = _settings().ImprovementsRepository;
        if (CodexLocator.IsRepository(configured)) return configured;
        return CodexLocator.FindRepository(AppContext.BaseDirectory);
    }

    public async Task<ImprovementResult> RunAsync(string request, ImprovementContext context, Action<ImprovementEvent> onEvent, CancellationToken cancellationToken)
    {
        var prompt = ImprovementPrompt.Build(request, context);
        var settings = _settings();
        var repository = ResolveRepository() ?? throw new InvalidOperationException(Core.Localization.Localizer.Current["improve.noRepo"]);
        var executable = CodexLocator.Find(settings.CodexExecutable, Environment.GetEnvironmentVariable("PATH"), windows: true)
            ?? throw new InvalidOperationException(Core.Localization.Localizer.Current["improve.noCodex"]);
        CodexLocator.ValidateExecutable(executable);
        if (!await _gate.WaitAsync(0, CancellationToken.None))
            throw new InvalidOperationException("已有改进请求正在运行。");
        try
        {
            _idle?.Change(Timeout.Infinite, Timeout.Infinite);
            var key = $"{executable}|{repository}|{settings.ImprovementsFullAccess}";
            if (_client is { IsBroken: true } || (_client is not null && key != _clientKey))
                await StopAsync();
            if (_client is null)
            {
                var transport = ProcessRpcTransport.Start(executable, repository);
                _client = new CodexAppServerClient(transport, new ImprovementOptions
                {
                    RepositoryPath = repository,
                    FullAccess = settings.ImprovementsFullAccess,
                    ClientVersion = context.Version
                });
                _clientKey = key;
            }
            try
            {
                return await _client.RunAsync(prompt, onEvent, cancellationToken);
            }
            finally
            {
                if (_client is { IsBroken: true }) await StopAsync();
                _idle ??= new Timer(_ => _ = StopWhenIdleAsync(), null, Timeout.Infinite, Timeout.Infinite);
                _idle.Change(Idle, Timeout.InfiniteTimeSpan);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task StopWhenIdleAsync()
    {
        if (!await _gate.WaitAsync(0, CancellationToken.None)) return;
        try { await StopAsync(); }
        finally { _gate.Release(); }
    }

    async Task StopAsync()
    {
        var client = _client;
        _client = null;
        if (client is not null)
        {
            try { await client.DisposeAsync(); }
            catch (Exception ex) { AppLog.Error(ex); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _idle?.Dispose();
        await StopAsync();
    }
}
