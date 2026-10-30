using System.Text;
using AltPane.Core.Localization;
using AltPane.Core.Models;
using AltPane.Core.Search;
using AltPane.Core.Settings;
using Xunit;

namespace AltPane.Tests;

public class EverythingSearchTests
{
    [Fact]
    public void The_http_url_uses_the_documented_query_and_the_fallback_port()
    {
        var url = EverythingHttp.BuildUrl(80, "report !path:.cursor", 20);
        Assert.Equal(
            "http://127.0.0.1:80/?search=report%20%21path%3A.cursor&json=1&path_column=1&count=20",
            url);
        Assert.StartsWith("http://127.0.0.1:80/?", EverythingHttp.BuildUrl(0, "a", 1));
        Assert.StartsWith("http://127.0.0.1:8080/?", EverythingHttp.BuildUrl(8080, "a", 5));
        Assert.Equal(new[] { 80, 8080 }, EverythingHttp.Ports(80, true));
        Assert.Equal(new[] { 8080 }, EverythingHttp.Ports(8080, true));
        Assert.Equal(new[] { 80 }, EverythingHttp.Ports(80, false));
        Assert.Equal(new[] { 80, 8080 }, EverythingHttp.Ports(-1, true));
    }

    [Fact]
    public void Http_json_maps_onto_full_paths_and_an_empty_list_is_still_a_reply()
    {
        var json = """
            {"totalResults":2,"results":[
              {"type":"file","name":"a.pdf","path":"C:\\Docs"},
              {"type":"file","name":"b.txt","path":"D:\\Work\\b.txt"}
            ]}
            """;
        var paths = EverythingHttp.Parse(json, 10);
        Assert.NotNull(paths);
        Assert.Equal(@"C:\Docs\a.pdf", paths![0]);
        Assert.Equal(@"D:\Work\b.txt", paths[1]);
        Assert.Single(EverythingHttp.Parse(json, 1)!);
        Assert.Empty(EverythingHttp.Parse("""{"results":[]}""", 5)!);
        Assert.Null(EverythingHttp.Parse("[]", 5));
        Assert.Null(EverythingHttp.Parse("{", 5));
        Assert.Null(EverythingHttp.Parse("""{"totalResults":0}""", 5));
    }

    [Fact]
    public void A_zero_count_ipc_buffer_is_a_reply_and_a_short_buffer_is_not()
    {
        Assert.Empty(EverythingIpc.Parse(new byte[12], 10)!);
        Assert.Null(EverythingIpc.Parse(new byte[4], 10));
        Assert.Null(EverythingIpc.Parse(null, 10));

        var name = Encoding.Unicode.GetBytes("a.pdf\0");
        var directory = Encoding.Unicode.GetBytes("C:\\Docs\0");
        var blob = new byte[28 + name.Length + directory.Length];
        BitConverter.GetBytes(1).CopyTo(blob, 0);
        BitConverter.GetBytes(1).CopyTo(blob, 4);
        BitConverter.GetBytes(-1).CopyTo(blob, 20);
        BitConverter.GetBytes(-1).CopyTo(blob, 24);
        var nameAt = 28;
        var directoryAt = 28 + name.Length;
        BitConverter.GetBytes(nameAt).CopyTo(blob, 12);
        BitConverter.GetBytes(directoryAt).CopyTo(blob, 16);
        name.CopyTo(blob, nameAt);
        directory.CopyTo(blob, directoryAt);
        var parsed = EverythingIpc.Parse(blob, 10);
        Assert.Equal(@"C:\Docs\a.pdf", Assert.Single(parsed!));
    }

    [Fact]
    public void File_search_always_excludes_agent_directories_and_keeps_user_paths()
    {
        Assert.True(FileSearch.TryPlan("report", null, new[] { @"D:\Secret" }, out var plan));
        foreach (var term in FileSearchExclusions.Terms)
            Assert.Contains("!path:" + term, plan.Query, StringComparison.Ordinal);
        Assert.Contains("!path:\"D:\\Secret\"", plan.Query, StringComparison.Ordinal);
        Assert.EndsWith(FileSearch.Sort, plan.Query);
        Assert.DoesNotContain("!path:\".aider*\"", plan.Query, StringComparison.Ordinal);
        Assert.Equal(1, Count(plan.Query, "!path:node_modules"));

        Assert.True(FileSearch.IsExcluded(@"C:\Users\a\.cursor\projects\x.md"));
        Assert.True(FileSearch.IsExcluded(@"C:\Users\a\.claude\notes.md"));
        Assert.True(FileSearch.IsExcluded(@"C:\Users\a\.gemini\tmp\a.md"));
        Assert.True(FileSearch.IsExcluded(@"C:\Users\a\.aider-chat\x"));
        Assert.True(FileSearch.IsExcluded(@"C:\Users\a\.vscode\extensions\ms-python\a.txt"));
        Assert.False(FileSearch.IsExcluded(@"C:\Users\a\.vscode\argv.json"));
        Assert.False(FileSearch.IsExcluded(@"C:\Users\a\Documents\report.pdf"));
        Assert.True(FileSearch.IsExcluded(@"D:\repo\app\node_modules\pkg\index.js"));

        var hits = FileSearch.ToHits(new[]
        {
            @"C:\Users\a\.codex\session.md",
            @"C:\Users\a\Documents\report.pdf"
        });
        var hit = Assert.Single(hits);
        Assert.Equal("report.pdf", hit.Name);
        Assert.Equal(@"C:\Users\a\Documents", hit.Directory);
        Assert.Equal(@"C:\Users\a\Documents\report.pdf", hit.FullPath);
    }

    [Fact]
    public void Ipc_falls_through_the_tray_window_and_then_http_when_the_class_is_missing()
    {
        var seen = new List<string>();
        var taskbar = EverythingQuery.Run(
            running: true,
            available: link => link == EverythingLink.IpcTaskbar,
            send: (link, port) =>
            {
                seen.Add(EverythingQuery.Name(link, port));
                return link == EverythingLink.IpcTaskbar ? new[] { @"C:\Docs\a.pdf" } : null;
            },
            httpPort: 80,
            tryPort8080: true);
        Assert.Equal(EverythingLink.IpcTaskbar, taskbar.Link);
        Assert.Equal(EverythingReach.Connected, taskbar.Reach);
        Assert.Equal(new[] { "ipc-taskbar" }, seen);

        seen.Clear();
        var main = EverythingQuery.Run(
            running: true,
            available: link => link is EverythingLink.IpcTaskbar or EverythingLink.IpcWindow,
            send: (link, port) =>
            {
                seen.Add(EverythingQuery.Name(link, port));
                return link == EverythingLink.IpcWindow ? new[] { @"C:\Docs\b.pdf" } : null;
            },
            httpPort: 80,
            tryPort8080: true);
        Assert.Equal(new[] { "ipc-taskbar", "ipc-window" }, seen);
        Assert.Equal(EverythingLink.IpcWindow, main.Link);
        Assert.Equal(EverythingReach.Connected, main.Reach);
    }

    [Fact]
    public void A_missing_reply_tries_http_80_and_then_8080_and_an_empty_reply_stops()
    {
        var seen = new List<string>();
        var failed = EverythingQuery.Run(
            running: true,
            available: _ => true,
            send: (link, port) =>
            {
                seen.Add(EverythingQuery.Name(link, port));
                return null;
            },
            httpPort: 80,
            tryPort8080: true);
        Assert.Equal(EverythingReach.IpcFailed, failed.Reach);
        Assert.Equal(EverythingLink.None, failed.Link);
        Assert.Equal(new[] { "sdk", "ipc", "ipc-taskbar", "ipc-window", "es", "http:80", "http:8080" }, seen);

        seen.Clear();
        var empty = EverythingQuery.Run(
            running: true,
            available: link => link == EverythingLink.Ipc,
            send: (link, port) =>
            {
                seen.Add(EverythingQuery.Name(link, port));
                return Array.Empty<string>();
            },
            httpPort: 80,
            tryPort8080: true);
        Assert.Equal(EverythingReach.Connected, empty.Reach);
        Assert.Empty(empty.Paths);
        Assert.Equal(new[] { "ipc" }, seen);

        seen.Clear();
        var http = EverythingQuery.Run(
            running: true,
            available: _ => false,
            send: (link, port) =>
            {
                seen.Add(EverythingQuery.Name(link, port));
                return port == 8080 ? new[] { @"D:\Work\b.txt" } : null;
            },
            httpPort: 80,
            tryPort8080: true);
        Assert.Equal(new[] { "http:80", "http:8080" }, seen);
        Assert.Equal(EverythingReach.ConnectedHttp, http.Reach);
        Assert.Equal(8080, http.Port);
        Assert.Equal(@"D:\Work\b.txt", Assert.Single(http.Paths));

        seen.Clear();
        EverythingQuery.Run(true, _ => false, (link, port) =>
        {
            seen.Add(EverythingQuery.Name(link, port));
            return null;
        }, 80, false);
        Assert.Equal(new[] { "http:80" }, seen);
    }

    [Fact]
    public void A_remembered_http_port_is_tried_before_ipc_and_is_not_repeated()
    {
        var seen = new List<string>();
        var hit = EverythingQuery.Run(
            running: true,
            available: link => link == EverythingLink.Ipc,
            send: (link, port) =>
            {
                seen.Add(EverythingQuery.Name(link, port));
                return link == EverythingLink.Http ? new[] { @"C:\a.txt" } : null;
            },
            httpPort: 80,
            tryPort8080: true,
            prefer: EverythingLink.Http,
            preferPort: 80);
        Assert.Equal(new[] { "http:80" }, seen);
        Assert.Equal(EverythingReach.ConnectedHttp, hit.Reach);

        seen.Clear();
        var missed = EverythingQuery.Run(
            running: true,
            available: link => link == EverythingLink.IpcTaskbar,
            send: (link, port) =>
            {
                seen.Add(EverythingQuery.Name(link, port));
                return null;
            },
            httpPort: 80,
            tryPort8080: true,
            prefer: EverythingLink.Http,
            preferPort: 80);
        Assert.Equal(EverythingReach.IpcFailed, missed.Reach);
        Assert.Equal(new[] { "http:80", "ipc-taskbar", "http:8080" }, seen);
    }

    [Fact]
    public void Nothing_is_contacted_when_everything_is_not_running()
    {
        var called = false;
        var result = EverythingQuery.Run(
            running: false,
            available: _ => false,
            send: (_, _) =>
            {
                called = true;
                return new[] { "x" };
            },
            httpPort: 80,
            tryPort8080: true);
        Assert.False(called);
        Assert.Equal(EverythingReach.NotRunning, result.Reach);
        Assert.Empty(result.Attempted);
    }

    [Fact]
    public void The_chinese_status_text_names_each_connection_state()
    {
        Assert.Equal("Everything 未运行", StringTables.Chinese["file.notRunning"]);
        Assert.Equal("Everything 未运行", StringTables.Chinese["settings.everythingNotRunning"]);
        Assert.Equal("Everything 在运行但 IPC 连接失败", StringTables.Chinese["file.ipcFailed"]);
        Assert.Contains("HTTP 服务器", StringTables.Chinese["file.ipcFailedHint"], StringComparison.Ordinal);
        Assert.Contains("Everything 在运行但 IPC 连接失败", StringTables.Chinese["settings.everythingIpcFailed"], StringComparison.Ordinal);
        Assert.Contains("HTTP 服务器", StringTables.Chinese["settings.everythingIpcFailed"], StringComparison.Ordinal);
        Assert.Equal("已通过 HTTP 连接", StringTables.Chinese["file.http"]);
        Assert.Equal("已通过 HTTP 连接", StringTables.Chinese["settings.everythingHttp"]);
    }

    [Fact]
    public void The_http_port_defaults_to_80_and_round_trips()
    {
        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, """{ "version": 7, "useEverything": true }""");
        var missing = SettingsStore.Load(path);
        Assert.Equal(80, missing.Settings.EverythingHttpPort);
        Assert.True(missing.Settings.EverythingHttpFallback);

        var settings = AppSettings.CreateDefault();
        settings.EverythingHttpPort = 8080;
        settings.EverythingHttpFallback = false;
        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);
        Assert.Equal(8080, loaded.Settings.EverythingHttpPort);
        Assert.False(loaded.Settings.EverythingHttpFallback);

        settings.EverythingHttpPort = 0;
        SettingsStore.Save(path, settings);
        Assert.Equal(80, SettingsStore.Load(path).Settings.EverythingHttpPort);
    }

    static int Count(string text, string token)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(token, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += token.Length;
        }
        return count;
    }
}
