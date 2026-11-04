using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AltPane.Core.Improvements;

public enum ImprovementEventKind { Status, AgentText, Command, FileChange }

public sealed record ImprovementEvent(ImprovementEventKind Kind, string Text);

public enum ImprovementOutcome { Completed, Failed, Interrupted }

public sealed record ImprovementResult(ImprovementOutcome Outcome, string Summary);

public sealed class ImprovementOptions
{
    public string RepositoryPath { get; init; } = "";
    /// <summary>Off: Codex may write only inside the repository. On: no sandbox, so builds can restore packages.</summary>
    public bool FullAccess { get; init; }
    public string ClientVersion { get; init; } = "0";
    public TimeSpan TurnTimeout { get; init; } = TimeSpan.FromMinutes(30);
    /// <summary>How long a cancelled turn gets to report "interrupted" before the session is abandoned.</summary>
    public TimeSpan InterruptGrace { get; init; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Drives one long-lived <c>codex app-server</c>: initialize once, keep a single thread, and run each
/// request as a turn on it, so later requests reuse the agent's context and the process start-up cost.
/// Approval prompts are always declined; the thread runs with approval policy "never" inside a sandbox.
/// After a timeout, a protocol failure or a lost process the client is <see cref="IsBroken"/> and must be replaced.
/// </summary>
public sealed class CodexAppServerClient : IAsyncDisposable
{
    readonly IRpcTransport _transport;
    readonly ImprovementOptions _options;
    readonly SemaphoreSlim _turnGate = new(1, 1);
    long _nextId;
    bool _initialized;
    string? _threadId;
    string? _turnId;
    ImprovementResult? _result;
    string _lastAgentText = "";
    Action<ImprovementEvent> _emit = _ => { };

    public CodexAppServerClient(IRpcTransport transport, ImprovementOptions options)
    {
        _transport = transport;
        _options = options;
    }

    public bool IsBroken { get; private set; }

    public async Task<ImprovementResult> RunAsync(string prompt, Action<ImprovementEvent> onEvent, CancellationToken cancellationToken)
    {
        if (IsBroken) throw new InvalidOperationException("Codex 会话已失效，请重试。");
        if (!await _turnGate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
            throw new InvalidOperationException("已有改进请求正在运行。");
        using var hard = new CancellationTokenSource(_options.TurnTimeout);
        using var registration = cancellationToken.Register(() =>
        {
            hard.CancelAfter(_options.InterruptGrace);
            _ = InterruptAsync();
        });
        try
        {
            _result = null;
            _lastAgentText = "";
            _turnId = null;
            _emit = onEvent;
            await EnsureThreadAsync(hard.Token).ConfigureAwait(false);
            var input = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = prompt,
                ["text_elements"] = new JsonArray()
            });
            var response = await CallAsync("turn/start", new JsonObject { ["threadId"] = _threadId, ["input"] = input }, hard.Token).ConfigureAwait(false);
            _turnId ??= CodexRpc.GetString(response, "turn", "id");
            if (cancellationToken.IsCancellationRequested) await InterruptAsync().ConfigureAwait(false);
            while (_result is null)
            {
                var line = await _transport.ReadLineAsync(hard.Token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Codex 意外退出。");
                await HandleAsync(CodexRpc.Parse(line), hard.Token).ConfigureAwait(false);
            }
            return _result;
        }
        catch (OperationCanceledException)
        {
            IsBroken = true;
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw new TimeoutException("Codex 处理超时，已停止。请重试或缩小需求。");
        }
        catch
        {
            IsBroken = true;
            throw;
        }
        finally
        {
            _emit = _ => { };
            _turnGate.Release();
        }
    }

    async Task EnsureThreadAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await CallAsync("initialize", new JsonObject
            {
                ["clientInfo"] = new JsonObject { ["name"] = "altpane", ["title"] = "AltPane", ["version"] = _options.ClientVersion },
                ["capabilities"] = new JsonObject { ["experimentalApi"] = false }
            }, cancellationToken).ConfigureAwait(false);
            await _transport.WriteLineAsync(CodexRpc.Notification("initialized"), cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }

        if (_threadId is null)
        {
            var response = await CallAsync("thread/start", new JsonObject
            {
                ["cwd"] = _options.RepositoryPath,
                ["approvalPolicy"] = "never",
                ["sandbox"] = _options.FullAccess ? "danger-full-access" : "workspace-write"
            }, cancellationToken).ConfigureAwait(false);
            _threadId = CodexRpc.GetString(response, "thread", "id")
                ?? throw new InvalidOperationException("Codex 没有返回会话 ID。");
        }
    }

    async Task<JsonElement?> CallAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        await _transport.WriteLineAsync(CodexRpc.Request(id, method, parameters), cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var line = await _transport.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Codex 意外退出。");
            var message = CodexRpc.Parse(line);
            if (message.Kind is RpcKind.Response or RpcKind.Error && SameId(message.IdRaw, id))
            {
                if (message.Kind == RpcKind.Error) throw new InvalidOperationException("Codex：" + message.Error);
                return message.Result;
            }
            await HandleAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    static bool SameId(string? raw, long id) =>
        raw is not null && raw.Trim('"') == id.ToString(CultureInfo.InvariantCulture);

    async Task InterruptAsync()
    {
        var thread = _threadId;
        var turn = _turnId;
        if (thread is null || turn is null) return;
        try
        {
            var id = Interlocked.Increment(ref _nextId);
            await _transport.WriteLineAsync(
                CodexRpc.Request(id, "turn/interrupt", new JsonObject { ["threadId"] = thread, ["turnId"] = turn }),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The process is gone; the read loop reports it.
        }
    }

    async Task HandleAsync(RpcMessage message, CancellationToken cancellationToken)
    {
        switch (message.Kind)
        {
            case RpcKind.Request:
                await AnswerServerRequestAsync(message, cancellationToken).ConfigureAwait(false);
                break;
            case RpcKind.Notification:
                OnNotification(message);
                break;
        }
    }

    async Task AnswerServerRequestAsync(RpcMessage request, CancellationToken cancellationToken)
    {
        if (request.IdRaw is null) return;
        string reply;
        if (request.Method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval")
        {
            _emit(new ImprovementEvent(ImprovementEventKind.Status, "已拒绝一项需要审批的操作。"));
            reply = CodexRpc.Response(request.IdRaw, new JsonObject { ["decision"] = "decline" });
        }
        else
        {
            reply = CodexRpc.ErrorResponse(request.IdRaw, -32601, "AltPane does not handle " + request.Method);
        }
        await _transport.WriteLineAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    void OnNotification(RpcMessage message)
    {
        var thread = CodexRpc.GetString(message.Params, "threadId");
        if (thread is not null && _threadId is not null && thread != _threadId) return;
        switch (message.Method)
        {
            case "turn/started":
                _turnId ??= CodexRpc.GetString(message.Params, "turn", "id");
                _emit(new ImprovementEvent(ImprovementEventKind.Status, "Codex 开始处理…"));
                break;
            case "item/started":
                if (CodexRpc.GetString(message.Params, "item", "type") == "commandExecution")
                    _emit(new ImprovementEvent(ImprovementEventKind.Command, "$ " + CodexRpc.GetString(message.Params, "item", "command")));
                break;
            case "item/completed":
                OnItemCompleted(message.Params);
                break;
            case "turn/completed":
                OnTurnCompleted(message.Params);
                break;
            case "error":
                var text = CodexRpc.GetString(message.Params, "error", "message");
                if (!string.IsNullOrWhiteSpace(text))
                    _emit(new ImprovementEvent(ImprovementEventKind.Status, text));
                break;
            case "warning":
            case "guardianWarning":
                Warn(CodexRpc.GetString(message.Params, "message"));
                break;
            case "configWarning":
                Warn(CodexRpc.GetString(message.Params, "summary"));
                break;
            case "windowsSandbox/setupCompleted":
                if (message.Params is { ValueKind: JsonValueKind.Object } setup
                    && setup.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                    Warn("Windows 沙箱初始化失败：" + (CodexRpc.GetString(message.Params, "error") ?? "未知原因")
                        + "。可在 settings.json 中将 improvementsFullAccess 设为 true 以关闭沙箱。");
                break;
        }
    }

    void OnItemCompleted(JsonElement? parameters)
    {
        switch (CodexRpc.GetString(parameters, "item", "type"))
        {
            case "agentMessage":
                var text = CodexRpc.GetString(parameters, "item", "text");
                if (string.IsNullOrWhiteSpace(text)) break;
                _lastAgentText = text.Trim();
                _emit(new ImprovementEvent(ImprovementEventKind.AgentText, _lastAgentText));
                break;
            case "commandExecution":
                var failure = DescribeFailedCommand(parameters);
                if (failure is not null)
                    _emit(new ImprovementEvent(ImprovementEventKind.Command, failure));
                break;
            case "fileChange":
                var paths = ChangedPaths(parameters);
                if (paths.Count > 0)
                    _emit(new ImprovementEvent(ImprovementEventKind.FileChange, string.Join(", ", paths)));
                break;
        }
    }

    void Warn(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            _emit(new ImprovementEvent(ImprovementEventKind.Status, "⚠ " + text.Trim()));
    }

    /// <summary>One line for a command that failed, was declined or exited non-zero, with the tail of its output. Null when it succeeded.</summary>
    internal static string? DescribeFailedCommand(JsonElement? parameters)
    {
        if (parameters is null || parameters.Value.ValueKind != JsonValueKind.Object
            || !parameters.Value.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
            return null;
        var status = item.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString() : null;
        int? exitCode = item.TryGetProperty("exitCode", out var exitElement) && exitElement.ValueKind == JsonValueKind.Number
            ? exitElement.GetInt32() : null;
        var failed = status is "failed" or "declined" || exitCode is not null and not 0;
        if (!failed) return null;
        var command = item.TryGetProperty("command", out var commandElement) && commandElement.ValueKind == JsonValueKind.String
            ? commandElement.GetString() : "";
        var output = item.TryGetProperty("aggregatedOutput", out var outputElement) && outputElement.ValueKind == JsonValueKind.String
            ? (outputElement.GetString() ?? "").Trim() : "";
        if (output.Length > 400) output = "…" + output[^400..];
        var reason = status == "declined" ? "被拒绝" : exitCode is { } code ? $"退出码 {code}" : "失败";
        return $"✗ {command}（{reason}）" + (output.Length > 0 ? Environment.NewLine + output : "");
    }

    static List<string> ChangedPaths(JsonElement? parameters)
    {
        var paths = new List<string>();
        if (parameters is null) return paths;
        if (parameters.Value.ValueKind != JsonValueKind.Object
            || !parameters.Value.TryGetProperty("item", out var item)
            || item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("changes", out var changes)
            || changes.ValueKind != JsonValueKind.Array)
            return paths;
        foreach (var change in changes.EnumerateArray())
        {
            if (change.ValueKind == JsonValueKind.Object
                && change.TryGetProperty("path", out var path)
                && path.ValueKind == JsonValueKind.String
                && path.GetString() is { Length: > 0 } value)
                paths.Add(value);
        }
        return paths;
    }

    void OnTurnCompleted(JsonElement? parameters)
    {
        var id = CodexRpc.GetString(parameters, "turn", "id");
        if (id is not null && _turnId is not null && id != _turnId) return;
        var error = CodexRpc.GetString(parameters, "turn", "error", "message");
        _result = CodexRpc.GetString(parameters, "turn", "status") switch
        {
            "completed" => new ImprovementResult(ImprovementOutcome.Completed, _lastAgentText),
            "interrupted" => new ImprovementResult(ImprovementOutcome.Interrupted, "已取消。"),
            _ => new ImprovementResult(ImprovementOutcome.Failed, string.IsNullOrWhiteSpace(error) ? _lastAgentText : error)
        };
    }

    /// <summary>Kills the app-server immediately, without awaiting anything.</summary>
    public void Kill() => _transport.Kill();

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}
