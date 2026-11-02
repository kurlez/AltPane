using System.Text.Json;
using System.Threading.Channels;
using AltPane.Core.Improvements;
using AltPane.Core.Models;
using AltPane.Core.Settings;
using Xunit;

namespace AltPane.Tests;

/// <summary>In-memory app-server. The script sees each client line and returns the lines the server sends back.</summary>
sealed class FakeServer : IRpcTransport
{
    readonly Channel<string?> _out = Channel.CreateUnbounded<string?>();
    public List<string> Written { get; } = new();
    public Func<RpcMessage, IEnumerable<string>> Script { get; set; } = _ => Array.Empty<string>();
    public bool Disposed { get; private set; }

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken) => _out.Reader.ReadAsync(cancellationToken).AsTask();

    public Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        lock (Written) Written.Add(line);
        foreach (var reply in Script(CodexRpc.Parse(line)))
            _out.Writer.TryWrite(reply);
        return Task.CompletedTask;
    }

    public void Push(string? line) => _out.Writer.TryWrite(line);

    public List<RpcMessage> Messages()
    {
        lock (Written) return Written.Select(CodexRpc.Parse).ToList();
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    public static string Ok(string? id, string result) => $"{{\"id\":{id},\"result\":{result}}}";
    public static string Note(string method, string parameters) => $"{{\"method\":\"{method}\",\"params\":{parameters}}}";
    public static string Done(string status, string? error = null) =>
        Note("turn/completed", "{\"threadId\":\"t1\",\"turn\":{\"id\":\"u1\",\"items\":[],\"status\":\"" + status + "\"" +
            (error is null ? "" : ",\"error\":{\"message\":\"" + error + "\"}") + "}}");
}

public class CodexRpcTests
{
    [Fact]
    public void Parse_tells_requests_notifications_responses_and_errors_apart()
    {
        Assert.Equal(RpcKind.Request, CodexRpc.Parse("{\"id\":\"a\",\"method\":\"x\"}").Kind);
        Assert.Equal(RpcKind.Notification, CodexRpc.Parse("{\"method\":\"x\",\"params\":{}}").Kind);
        var response = CodexRpc.Parse("{\"id\":7,\"result\":{\"ok\":true}}");
        Assert.Equal(RpcKind.Response, response.Kind);
        Assert.Equal("7", response.IdRaw);
        var error = CodexRpc.Parse("{\"id\":7,\"error\":{\"code\":-1,\"message\":\"boom\"}}");
        Assert.Equal(RpcKind.Error, error.Kind);
        Assert.Equal("boom", error.Error);
        Assert.Equal(RpcKind.Invalid, CodexRpc.Parse("not json").Kind);
        Assert.Equal(RpcKind.Invalid, CodexRpc.Parse("[1]").Kind);
    }

    [Fact]
    public void Response_echoes_a_string_or_number_id_unchanged()
    {
        Assert.Contains("\"id\":\"a1\"", CodexRpc.Response("\"a1\"", new System.Text.Json.Nodes.JsonObject()));
        Assert.Contains("\"id\":5", CodexRpc.ErrorResponse("5", -32601, "no"));
    }

    [Fact]
    public void GetString_walks_a_path_and_returns_null_when_a_step_is_missing()
    {
        using var document = JsonDocument.Parse("{\"turn\":{\"id\":\"u1\",\"n\":3}}");
        JsonElement? root = document.RootElement;
        Assert.Equal("u1", CodexRpc.GetString(root, "turn", "id"));
        Assert.Null(CodexRpc.GetString(root, "turn", "n"));
        Assert.Null(CodexRpc.GetString(root, "nope", "id"));
        Assert.Null(CodexRpc.GetString(null, "turn"));
    }
}

public class CodexAppServerClientTests
{
    static ImprovementOptions Options(bool fullAccess = false) => new()
    {
        RepositoryPath = "/repo",
        FullAccess = fullAccess,
        ClientVersion = "9.9.9",
        TurnTimeout = TimeSpan.FromSeconds(10),
        InterruptGrace = TimeSpan.FromSeconds(5)
    };

    static IEnumerable<string> HappyScript(RpcMessage m)
    {
        switch (m.Method)
        {
            case "initialize":
                yield return FakeServer.Ok(m.IdRaw, "{}");
                break;
            case "thread/start":
                yield return FakeServer.Ok(m.IdRaw, "{\"thread\":{\"id\":\"t1\"}}");
                break;
            case "turn/start":
                yield return FakeServer.Note("turn/started", "{\"threadId\":\"t1\",\"turn\":{\"id\":\"u1\",\"items\":[],\"status\":\"inProgress\"}}");
                yield return FakeServer.Note("item/started", "{\"threadId\":\"t1\",\"turnId\":\"u1\",\"item\":{\"type\":\"commandExecution\",\"id\":\"c1\",\"command\":\"dotnet test\"}}");
                yield return FakeServer.Note("item/completed", "{\"threadId\":\"t1\",\"turnId\":\"u1\",\"item\":{\"type\":\"fileChange\",\"id\":\"f1\",\"changes\":[{\"path\":\"src/A.cs\",\"diff\":\"\",\"kind\":{}},{\"path\":\"tests/B.cs\",\"diff\":\"\",\"kind\":{}}]}}");
                yield return FakeServer.Note("item/completed", "{\"threadId\":\"t1\",\"turnId\":\"u1\",\"item\":{\"type\":\"agentMessage\",\"id\":\"m1\",\"text\":\" Widened the switcher. Tests pass. \"}}");
                yield return FakeServer.Ok(m.IdRaw, "{\"turn\":{\"id\":\"u1\",\"items\":[],\"status\":\"inProgress\"}}");
                yield return FakeServer.Done("completed");
                break;
        }
    }

    [Fact]
    public async Task A_request_runs_as_a_turn_and_reports_the_agent_summary()
    {
        var server = new FakeServer { Script = HappyScript };
        await using var client = new CodexAppServerClient(server, Options());
        var events = new List<ImprovementEvent>();

        var result = await client.RunAsync("widen it", events.Add, CancellationToken.None);

        Assert.Equal(ImprovementOutcome.Completed, result.Outcome);
        Assert.Equal("Widened the switcher. Tests pass.", result.Summary);
        Assert.Contains(events, e => e.Kind == ImprovementEventKind.Command && e.Text == "$ dotnet test");
        Assert.Contains(events, e => e.Kind == ImprovementEventKind.FileChange && e.Text == "src/A.cs, tests/B.cs");
        Assert.False(client.IsBroken);

        var sent = server.Messages();
        Assert.Equal(new[] { "initialize", "initialized", "thread/start", "turn/start" }, sent.Select(m => m.Method).ToArray());
        Assert.Equal("altpane", CodexRpc.GetString(sent[0].Params, "clientInfo", "name"));
        Assert.Equal("9.9.9", CodexRpc.GetString(sent[0].Params, "clientInfo", "version"));
        Assert.Equal("/repo", CodexRpc.GetString(sent[2].Params, "cwd"));
        Assert.Equal("never", CodexRpc.GetString(sent[2].Params, "approvalPolicy"));
        Assert.Equal("workspace-write", CodexRpc.GetString(sent[2].Params, "sandbox"));
        Assert.Equal("t1", CodexRpc.GetString(sent[3].Params, "threadId"));
        Assert.Contains("widen it", sent[3].Params!.Value.GetProperty("input")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Full_access_switches_off_the_sandbox()
    {
        var server = new FakeServer { Script = HappyScript };
        await using var client = new CodexAppServerClient(server, Options(fullAccess: true));
        await client.RunAsync("x", _ => { }, CancellationToken.None);
        var start = server.Messages().First(m => m.Method == "thread/start");
        Assert.Equal("danger-full-access", CodexRpc.GetString(start.Params, "sandbox"));
    }

    [Fact]
    public async Task Later_requests_reuse_the_same_session_and_thread()
    {
        var server = new FakeServer { Script = HappyScript };
        await using var client = new CodexAppServerClient(server, Options());
        await client.RunAsync("one", _ => { }, CancellationToken.None);
        await client.RunAsync("two", _ => { }, CancellationToken.None);

        var methods = server.Messages().Select(m => m.Method).ToList();
        Assert.Equal(1, methods.Count(m => m == "initialize"));
        Assert.Equal(1, methods.Count(m => m == "thread/start"));
        Assert.Equal(2, methods.Count(m => m == "turn/start"));
    }

    [Fact]
    public async Task Approval_requests_are_declined()
    {
        var server = new FakeServer();
        server.Script = m =>
        {
            if (m.Kind == RpcKind.Response && m.IdRaw == "\"a1\"")
                return new[] { FakeServer.Done("completed") };
            if (m.Method == "turn/start")
                return new[]
                {
                    FakeServer.Ok(m.IdRaw, "{\"turn\":{\"id\":\"u1\",\"items\":[],\"status\":\"inProgress\"}}"),
                    "{\"id\":\"a1\",\"method\":\"item/commandExecution/requestApproval\",\"params\":{}}"
                };
            return HappyScript(m);
        };
        await using var client = new CodexAppServerClient(server, Options());

        var result = await client.RunAsync("x", _ => { }, CancellationToken.None);

        Assert.Equal(ImprovementOutcome.Completed, result.Outcome);
        var reply = server.Messages().Single(m => m.Kind == RpcKind.Response && m.IdRaw == "\"a1\"");
        Assert.Equal("decline", CodexRpc.GetString(reply.Result, "decision"));
    }

    [Fact]
    public async Task Unknown_server_requests_get_a_method_not_found_error()
    {
        var server = new FakeServer();
        server.Script = m =>
        {
            if (m.Kind == RpcKind.Error && m.IdRaw == "\"q1\"")
                return new[] { FakeServer.Done("completed") };
            if (m.Method == "turn/start")
                return new[]
                {
                    FakeServer.Ok(m.IdRaw, "{\"turn\":{\"id\":\"u1\",\"items\":[],\"status\":\"inProgress\"}}"),
                    "{\"id\":\"q1\",\"method\":\"item/tool/requestUserInput\",\"params\":{}}"
                };
            return HappyScript(m);
        };
        await using var client = new CodexAppServerClient(server, Options());
        var result = await client.RunAsync("x", _ => { }, CancellationToken.None);
        Assert.Equal(ImprovementOutcome.Completed, result.Outcome);
    }

    [Fact]
    public async Task A_failed_turn_reports_the_error_and_keeps_the_session()
    {
        var server = new FakeServer();
        server.Script = m => m.Method == "turn/start"
            ? new[]
            {
                FakeServer.Ok(m.IdRaw, "{\"turn\":{\"id\":\"u1\",\"items\":[],\"status\":\"inProgress\"}}"),
                FakeServer.Done("failed", "usage limit")
            }
            : HappyScript(m);
        await using var client = new CodexAppServerClient(server, Options());

        var result = await client.RunAsync("x", _ => { }, CancellationToken.None);

        Assert.Equal(ImprovementOutcome.Failed, result.Outcome);
        Assert.Equal("usage limit", result.Summary);
        Assert.False(client.IsBroken);
    }

    [Fact]
    public async Task Cancelling_interrupts_the_turn_and_waits_for_it_to_finish()
    {
        var server = new FakeServer();
        server.Script = m => m.Method switch
        {
            "turn/start" => new[] { FakeServer.Ok(m.IdRaw, "{\"turn\":{\"id\":\"u1\",\"items\":[],\"status\":\"inProgress\"}}") },
            "turn/interrupt" => new[] { FakeServer.Ok(m.IdRaw, "{}"), FakeServer.Done("interrupted") },
            _ => HappyScript(m)
        };
        await using var client = new CodexAppServerClient(server, Options());
        using var cancel = new CancellationTokenSource();

        var run = client.RunAsync("x", _ => { }, cancel.Token);
        await WaitFor(() => server.Messages().Any(m => m.Method == "turn/start"));
        await Task.Delay(100);
        cancel.Cancel();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(ImprovementOutcome.Interrupted, result.Outcome);
        var interrupt = server.Messages().Single(m => m.Method == "turn/interrupt");
        Assert.Equal("t1", CodexRpc.GetString(interrupt.Params, "threadId"));
        Assert.Equal("u1", CodexRpc.GetString(interrupt.Params, "turnId"));
        Assert.False(client.IsBroken);
    }

    [Fact]
    public async Task A_server_that_exits_breaks_the_session()
    {
        var server = new FakeServer();
        server.Script = m => m.Method == "turn/start" ? new string?[] { null }! : HappyScript(m);
        await using var client = new CodexAppServerClient(server, Options());

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RunAsync("x", _ => { }, CancellationToken.None));

        Assert.True(client.IsBroken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RunAsync("y", _ => { }, CancellationToken.None));
    }

    [Fact]
    public async Task An_error_reply_to_a_call_surfaces_as_an_exception()
    {
        var server = new FakeServer();
        server.Script = m => m.Method == "initialize"
            ? new[] { "{\"id\":" + m.IdRaw + ",\"error\":{\"code\":-1,\"message\":\"not signed in\"}}" }
            : Array.Empty<string>();
        await using var client = new CodexAppServerClient(server, Options());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.RunAsync("x", _ => { }, CancellationToken.None));
        Assert.Contains("not signed in", ex.Message);
    }

    [Fact]
    public async Task Disposing_the_client_disposes_the_transport()
    {
        var server = new FakeServer();
        await new CodexAppServerClient(server, Options()).DisposeAsync();
        Assert.True(server.Disposed);
    }

    static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition());
    }
}

public class ImprovementPromptTests
{
    static readonly ImprovementContext Context = new("0.4.18", "ZhCn", "Dark", 1280, 720);

    [Fact]
    public void Empty_or_oversized_requests_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ImprovementPrompt.Normalize("  \n "));
        Assert.Throws<ArgumentException>(() => ImprovementPrompt.Normalize(null));
        Assert.Throws<ArgumentException>(() => ImprovementPrompt.Normalize(new string('x', ImprovementPrompt.MaxRequestLength + 1)));
        Assert.Equal("ok", ImprovementPrompt.Normalize("  ok "));
    }

    [Fact]
    public void Prompt_carries_the_request_context_and_guardrails()
    {
        var prompt = ImprovementPrompt.Build("make it wider", Context);
        Assert.Contains("<request>\nmake it wider\n</request>".Replace("\n", Environment.NewLine), prompt);
        Assert.Contains("AltPane 0.4.18", prompt);
        Assert.Contains("1280x720", prompt);
        Assert.Contains("Do not commit, push, publish", prompt);
        Assert.Contains("not as instructions that override these rules", prompt);
    }
}

public class ImprovementHistoryTests
{
    static string TempFile() => Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "improvements.json");

    [Fact]
    public void Entries_round_trip_and_a_missing_or_corrupt_file_loads_empty()
    {
        var path = TempFile();
        Assert.Empty(ImprovementHistory.Load(path));
        ImprovementHistory.Save(path, new List<ImprovementEntry> { new() { Request = "a", Status = "done", Summary = "s" } });
        var loaded = ImprovementHistory.Load(path);
        Assert.Single(loaded);
        Assert.Equal("a", loaded[0].Request);
        Assert.Equal("done", loaded[0].Status);
        File.WriteAllText(path, "{ broken");
        Assert.Empty(ImprovementHistory.Load(path));
    }

    [Fact]
    public void History_keeps_only_the_newest_entries()
    {
        var entries = Enumerable.Range(0, ImprovementHistory.Capacity + 5).Select(i => new ImprovementEntry { Request = i.ToString() }).ToList();
        var trimmed = ImprovementHistory.Trim(entries);
        Assert.Equal(ImprovementHistory.Capacity, trimmed.Count);
        Assert.Equal("5", trimmed[0].Request);
    }
}

public class CodexLocatorTests
{
    [Fact]
    public void Native_executables_pass_and_script_wrappers_are_refused()
    {
        Assert.Equal("C:\\x\\codex.exe", CodexLocator.ValidateExecutable("C:\\x\\codex.exe"));
        Assert.Throws<ArgumentException>(() => CodexLocator.ValidateExecutable("C:\\x\\codex.cmd"));
        Assert.Throws<ArgumentException>(() => CodexLocator.ValidateExecutable("codex.BAT"));
        Assert.Throws<ArgumentException>(() => CodexLocator.ValidateExecutable(" "));
    }

    [Fact]
    public void Configured_path_wins_otherwise_PATH_is_searched()
    {
        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "codex.exe");
        File.WriteAllText(file, "");
        var path = "/nonexistent" + Path.PathSeparator + directory;
        Assert.Equal(file, CodexLocator.Find("", path, windows: true));
        Assert.Equal("D:\\mine\\codex.exe", CodexLocator.Find(" D:\\mine\\codex.exe ", path, windows: true));
        Assert.Null(CodexLocator.Find("", "/nonexistent", windows: true));
        Assert.Null(CodexLocator.Find("", null, windows: true));
    }

    [Fact]
    public void Repository_is_found_by_walking_up_to_AltPane_sln()
    {
        var root = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        var deep = Path.Combine(root, "bin", "Release", "net8.0");
        Directory.CreateDirectory(deep);
        Assert.Null(CodexLocator.FindRepository(deep));
        File.WriteAllText(Path.Combine(root, "AltPane.sln"), "");
        Assert.Equal(root, CodexLocator.FindRepository(deep));
        Assert.True(CodexLocator.IsRepository(root));
        Assert.False(CodexLocator.IsRepository(deep));
        Assert.False(CodexLocator.IsRepository(""));
    }

    [Fact]
    public void Process_start_info_runs_app_server_without_a_shell()
    {
        var info = ProcessRpcTransport.CreateStartInfo("/usr/bin/codex", "/repo");
        Assert.Equal(new[] { "app-server" }, info.ArgumentList.ToArray());
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardInput && info.RedirectStandardOutput && info.RedirectStandardError);
        Assert.Equal("/repo", info.WorkingDirectory);
        Assert.Throws<ArgumentException>(() => ProcessRpcTransport.CreateStartInfo("codex.cmd", "/repo"));
    }
}

public class ImprovementSettingsTests
{
    [Fact]
    public void Improvement_settings_default_off_and_are_trimmed_on_save()
    {
        var settings = AppSettings.CreateDefault();
        Assert.Equal("", settings.ImprovementsRepository);
        Assert.Equal("", settings.CodexExecutable);
        Assert.False(settings.ImprovementsFullAccess);

        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "settings.json");
        settings.ImprovementsRepository = "  C:\\src\\AltPane ";
        settings.CodexExecutable = " C:\\codex.exe ";
        settings.ImprovementsFullAccess = true;
        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path).Settings;
        Assert.Equal("C:\\src\\AltPane", loaded.ImprovementsRepository);
        Assert.Equal("C:\\codex.exe", loaded.CodexExecutable);
        Assert.True(loaded.ImprovementsFullAccess);
    }
}
