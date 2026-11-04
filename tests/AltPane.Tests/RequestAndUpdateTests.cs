using System.IO.Compression;
using System.Net;
using System.Text;
using AltPane.Core.Localization;
using AltPane.Core.Models;
using AltPane.Core.Requests;
using AltPane.Core.Settings;
using AltPane.Core.Updates;
using Xunit;

namespace AltPane.Tests;

/// <summary>Scripted command runner: each call is recorded and answered by the script.</summary>
sealed class FakeCommands : ICommandRunner
{
    public List<(string File, string[] Args)> Calls { get; } = new();
    public List<string> BodyFileContents { get; } = new();
    public Func<string, string[], CommandResult> Script { get; set; } = (_, _) => new CommandResult(0, "", "");

    public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var args = arguments.ToArray();
        Calls.Add((fileName, args));
        var bodyAt = Array.IndexOf(args, "--body-file");
        if (bodyAt >= 0 && File.Exists(args[bodyAt + 1])) BodyFileContents.Add(File.ReadAllText(args[bodyAt + 1]));
        return Task.FromResult(Script(fileName, args));
    }

    public static CommandResult Ok(string output = "") => new(0, output, "");
    public static CommandResult Fail(string error) => new(1, "", error);
}

public class FeatureRequestTests
{
    static readonly RequestContext Context = new("0.4.19", "Microsoft Windows 10.0.22631 (X64)");

    [Fact]
    public void Issue_arguments_keep_user_text_as_single_arguments_and_the_body_in_a_file()
    {
        var title = "Wider \"switcher\" $(rm -rf /) & calc.exe; `whoami` | echo";
        var args = FeatureRequest.CreateIssueArguments(title, "C:\\tmp\\body.md");
        Assert.Equal(new[]
        {
            "issue", "create", "--repo", "kurlez/AltPane", "--title", title, "--body-file", "C:\\tmp\\body.md",
            "--label", "altpane-request", "--assignee", "@me"
        }, args);
        Assert.DoesNotContain("--body", args);
    }

    [Fact]
    public void Label_arguments_create_or_update_the_label()
    {
        var args = FeatureRequest.CreateLabelArguments();
        Assert.Equal(new[] { "label", "create", "altpane-request", "--repo", "kurlez/AltPane" }, args.Take(5));
        Assert.Contains("--force", args);
    }

    [Fact]
    public void Body_carries_the_description_and_a_version_footer()
    {
        var body = FeatureRequest.BuildBody("把切换器做宽一点\n谢谢", Context);
        Assert.StartsWith("把切换器做宽一点\n谢谢\n\n---\n", body);
        Assert.Contains(FeatureRequest.FooterMarker, body);
        Assert.Contains("- AltPane: 0.4.19", body);
        Assert.Contains("- OS: Microsoft Windows 10.0.22631 (X64)", body);
    }

    [Fact]
    public void Normalize_rejects_empty_or_oversized_input_and_collapses_title_whitespace()
    {
        Assert.Throws<ArgumentException>(() => FeatureRequest.Normalize(" ", "x"));
        Assert.Throws<ArgumentException>(() => FeatureRequest.Normalize("t", " "));
        Assert.Throws<ArgumentException>(() => FeatureRequest.Normalize(new string('a', 201), "x"));
        Assert.Throws<ArgumentException>(() => FeatureRequest.Normalize("t", new string('a', 20001)));
        Assert.Equal(("a b", "x\ny"), FeatureRequest.Normalize("  a \n  b ", " x\r\ny "));
    }

    [Fact]
    public void Issue_url_is_read_from_gh_output()
    {
        Assert.Equal("https://github.com/kurlez/AltPane/issues/42",
            FeatureRequest.ParseIssueUrl("Creating issue in kurlez/AltPane\n\nhttps://github.com/kurlez/AltPane/issues/42\n"));
        Assert.Null(FeatureRequest.ParseIssueUrl("nothing"));
        Assert.Null(FeatureRequest.ParseIssueUrl(null));
    }

    [Fact]
    public async Task Submit_checks_sign_in_creates_the_label_then_the_issue_and_deletes_the_body_file()
    {
        var gh = new FakeCommands
        {
            Script = (_, a) => a[0] == "issue" ? FakeCommands.Ok("https://github.com/kurlez/AltPane/issues/7\n") : FakeCommands.Ok()
        };
        var url = await new FeatureRequestSubmitter(gh).SubmitAsync(" 加宽 ", "描述 `x` $(y)", Context, CancellationToken.None);

        Assert.Equal("https://github.com/kurlez/AltPane/issues/7", url);
        Assert.Equal(new[] { "auth", "label", "issue" }, gh.Calls.Select(c => c.Args[0]));
        Assert.All(gh.Calls, c => Assert.Equal("gh", c.File));
        var issue = gh.Calls[2].Args;
        Assert.Equal("加宽", issue[Array.IndexOf(issue, "--title") + 1]);
        var bodyFile = issue[Array.IndexOf(issue, "--body-file") + 1];
        Assert.False(File.Exists(bodyFile));
        Assert.StartsWith("描述 `x` $(y)\n\n---\n", Assert.Single(gh.BodyFileContents));
    }

    [Fact]
    public async Task A_label_that_cannot_be_created_does_not_stop_the_issue()
    {
        var gh = new FakeCommands
        {
            Script = (_, a) => a[0] switch
            {
                "label" => FakeCommands.Fail("HTTP 403: must have admin rights"),
                "issue" => FakeCommands.Ok("https://github.com/kurlez/AltPane/issues/8"),
                _ => FakeCommands.Ok()
            }
        };
        Assert.EndsWith("/issues/8", await new FeatureRequestSubmitter(gh).SubmitAsync("t", "d", Context, CancellationToken.None));
    }

    sealed class MissingGh : ICommandRunner
    {
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken) =>
            throw new CommandNotFoundException(fileName);
    }

    [Fact]
    public async Task Missing_gh_says_how_to_install_it()
    {
        var ex = await Assert.ThrowsAsync<RequestException>(() => new FeatureRequestSubmitter(new MissingGh()).SubmitAsync("t", "d", Context, CancellationToken.None));
        Assert.Equal(RequestFailure.GhMissing, ex.Failure);
        Assert.Contains("未找到 GitHub CLI", ex.Message);
        Assert.Contains("winget install GitHub.cli", ex.Message);
    }

    [Fact]
    public async Task Not_signed_in_says_to_run_gh_auth_login_and_stops_early()
    {
        var gh = new FakeCommands { Script = (_, a) => a[0] == "auth" ? FakeCommands.Fail("You are not logged into any GitHub hosts.") : FakeCommands.Ok() };
        var ex = await Assert.ThrowsAsync<RequestException>(() => new FeatureRequestSubmitter(gh).SubmitAsync("t", "d", Context, CancellationToken.None));
        Assert.Equal(RequestFailure.NotSignedIn, ex.Failure);
        Assert.Contains("gh auth login", ex.Message);
        Assert.Single(gh.Calls);
    }

    [Theory]
    [InlineData("GraphQL: Resource not accessible by integration (addLabelsToLabelable)")]
    [InlineData("HTTP 403: Must have push access to view repository collaborators.")]
    [InlineData("could not assign user: 'someone' not found")]
    public async Task No_permission_is_explained(string error)
    {
        var gh = new FakeCommands { Script = (_, a) => a[0] == "issue" ? FakeCommands.Fail(error) : FakeCommands.Ok() };
        var ex = await Assert.ThrowsAsync<RequestException>(() => new FeatureRequestSubmitter(gh).SubmitAsync("t", "d", Context, CancellationToken.None));
        Assert.Equal(RequestFailure.NoPermission, ex.Failure);
        Assert.Contains("没有在 kurlez/AltPane 创建", ex.Message);
    }

    [Fact]
    public async Task Other_failures_keep_ghs_message()
    {
        var gh = new FakeCommands { Script = (_, a) => a[0] == "issue" ? FakeCommands.Fail("connection reset") : FakeCommands.Ok() };
        var ex = await Assert.ThrowsAsync<RequestException>(() => new FeatureRequestSubmitter(gh).SubmitAsync("t", "d", Context, CancellationToken.None));
        Assert.Equal(RequestFailure.Other, ex.Failure);
        Assert.Contains("connection reset", ex.Message);
    }

    [Fact]
    public void Request_and_update_strings_exist_in_both_languages()
    {
        foreach (var key in new[] { "tray.request", "tray.checkUpdates", "request.submit", "request.open", "update.prompt" })
        {
            Assert.True(StringTables.English.ContainsKey(key), key);
            Assert.True(StringTables.Chinese.ContainsKey(key), key);
        }
        Assert.Equal("提需求…", StringTables.Chinese["tray.request"]);
        Assert.Equal("检查更新", StringTables.Chinese["tray.checkUpdates"]);
        Assert.Equal("提交", StringTables.Chinese["request.submit"]);
        Assert.Equal("打开", StringTables.Chinese["request.open"]);
        Assert.StartsWith("发现新版本 0.4.20，是否安装？", string.Format(StringTables.Chinese["update.prompt"], "0.4.20", "0.4.19", "url"));
        Assert.DoesNotContain(StringTables.English.Keys, k => k.StartsWith("improve.") || k == "tray.improve");
    }
}

public class LegacySettingsTests
{
    [Fact]
    public void Codex_settings_from_0_4_18_load_without_error_and_are_dropped_on_save()
    {
        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = SettingsStoreJson(AppSettings.CreateDefault())
            .TrimEnd().TrimEnd('}')
            + ",\n  \"improvementsRemote\": \"https://github.com/me/AltPane.git\",\n  \"improvementsBaseBranch\": \"dev\",\n  \"codexExecutable\": \"C:\\\\codex.exe\",\n  \"improvementsFullAccess\": true\n}";
        File.WriteAllText(path, json);

        var loaded = SettingsStore.Load(path);
        Assert.False(loaded.RecoveredFromError);
        SettingsStore.Save(path, loaded.Settings);
        var saved = File.ReadAllText(path);
        Assert.DoesNotContain("improvements", saved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codex", saved, StringComparison.OrdinalIgnoreCase);
    }

    static string SettingsStoreJson(AppSettings settings)
    {
        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "s.json");
        SettingsStore.Save(path, settings);
        return File.ReadAllText(path);
    }
}

public class UpdateCheckTests
{
    const string Base = "https://github.com/kurlez/AltPane/releases/download/";

    static string ReleaseJson(string tag, bool withSha = true, bool prerelease = false, string host = Base)
    {
        var version = tag.TrimStart('v');
        var assets = $"{{\"name\":\"AltPane-{version}-win-x64.zip\",\"browser_download_url\":\"{host}{tag}/AltPane-{version}-win-x64.zip\"}}";
        if (withSha) assets += $",{{\"name\":\"AltPane-{version}-win-x64.zip.sha256\",\"browser_download_url\":\"{host}{tag}/AltPane-{version}-win-x64.zip.sha256\"}}";
        return $"{{\"tag_name\":\"{tag}\",\"html_url\":\"https://github.com/kurlez/AltPane/releases/tag/{tag}\",\"draft\":false,\"prerelease\":{(prerelease ? "true" : "false")},\"assets\":[{assets}]}}";
    }

    [Fact]
    public void Latest_release_is_parsed_with_its_zip_and_checksum()
    {
        var release = UpdateCheck.ParseRelease(ReleaseJson("v0.4.20"))!;
        Assert.Equal("0.4.20", release.Version);
        Assert.Equal("AltPane-0.4.20-win-x64.zip", release.ZipName);
        Assert.Equal(Base + "v0.4.20/AltPane-0.4.20-win-x64.zip", release.ZipUrl);
        Assert.Equal(Base + "v0.4.20/AltPane-0.4.20-win-x64.zip.sha256", release.Sha256Url);
        Assert.Null(UpdateCheck.ParseRelease(ReleaseJson("v0.4.20", withSha: false))!.Sha256Url);
    }

    [Fact]
    public void Prereleases_junk_and_foreign_download_hosts_are_ignored()
    {
        Assert.Null(UpdateCheck.ParseRelease(ReleaseJson("v0.4.20", prerelease: true)));
        Assert.Null(UpdateCheck.ParseRelease(ReleaseJson("v0.4.20", host: "https://evil.example/")));
        Assert.Null(UpdateCheck.ParseRelease(ReleaseJson("nightly")));
        Assert.Null(UpdateCheck.ParseRelease("not json"));
    }

    [Fact]
    public void Only_a_higher_version_is_newer()
    {
        var release = UpdateCheck.ParseRelease(ReleaseJson("v0.4.20"))!;
        Assert.True(UpdateCheck.IsNewer("0.4.19", release));
        Assert.True(UpdateCheck.IsNewer("0.4.9", release));
        Assert.False(UpdateCheck.IsNewer("0.4.20", release));
        Assert.False(UpdateCheck.IsNewer("0.5.0", release));
    }

    [Fact]
    public void Sha256_file_is_parsed_in_sha256sum_format()
    {
        var hash = new string('a', 64);
        Assert.Equal(hash, UpdateCheck.ParseSha256File(hash.ToUpperInvariant() + "  AltPane-0.4.20-win-x64.zip", "AltPane-0.4.20-win-x64.zip"));
        Assert.Equal(hash, UpdateCheck.ParseSha256File(hash));
        Assert.Null(UpdateCheck.ParseSha256File(hash + "  other.zip", "AltPane-0.4.20-win-x64.zip"));
        Assert.Null(UpdateCheck.ParseSha256File("xyz  AltPane.zip"));
        Assert.Null(UpdateCheck.ParseSha256File(""));
    }

    static string TempFile(string content)
    {
        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "a.zip");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Verify_accepts_a_matching_hash_and_deletes_on_mismatch_or_missing_hash()
    {
        var good = TempFile("hello");
        UpdateCheck.Verify(good, UpdateCheck.Sha256Of(good).ToUpperInvariant());
        Assert.True(File.Exists(good));

        var bad = TempFile("tampered");
        var ex = Assert.Throws<InvalidOperationException>(() => UpdateCheck.Verify(bad, UpdateCheck.Sha256Of(good)));
        Assert.Contains("SHA256 校验失败", ex.Message);
        Assert.False(File.Exists(bad));

        var none = TempFile("x");
        Assert.Throws<InvalidOperationException>(() => UpdateCheck.Verify(none, null));
        Assert.False(File.Exists(none));
    }

    static byte[] ZipWithExe(string content)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("AltPane.exe").Open());
            writer.Write(content);
        }
        return memory.ToArray();
    }

    sealed class Handler : HttpMessageHandler
    {
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Contains("AltPane", request.Headers.UserAgent.ToString());
            return Task.FromResult(Routes.TryGetValue(request.RequestUri!.ToString(), out var route) ? route() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    static (UpdateClient Client, Handler Handler, ReleaseInfo Release, byte[] Zip) Setup()
    {
        var handler = new Handler();
        var zip = ZipWithExe("new exe");
        var json = ReleaseJson("v0.4.20");
        var release = UpdateCheck.ParseRelease(json)!;
        handler.Routes[UpdateCheck.LatestReleaseUrl] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        handler.Routes[release.ZipUrl] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        return (new UpdateClient(new HttpClient(handler), "0.4.19"), handler, release, zip);
    }

    static string Hex(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public async Task Download_verifies_the_zip_and_extracts_the_exe()
    {
        var (client, handler, release, zip) = Setup();
        handler.Routes[release.Sha256Url!] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{Hex(zip)}  {release.ZipName}") };
        Assert.Equal("0.4.20", (await client.GetLatestAsync(CancellationToken.None))!.Version);

        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        var exe = await client.DownloadAsync(release, directory, CancellationToken.None);
        Assert.Equal("new exe", File.ReadAllText(exe));
    }

    [Fact]
    public async Task A_checksum_mismatch_stops_the_update()
    {
        var (client, handler, release, _) = Setup();
        handler.Routes[release.Sha256Url!] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{new string('0', 64)}  {release.ZipName}") };
        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadAsync(release, directory, CancellationToken.None));
        Assert.Contains("SHA256", ex.Message);
        Assert.False(File.Exists(Path.Combine(directory, "extract", "AltPane.exe")));
        Assert.False(File.Exists(Path.Combine(directory, release.ZipName)));
    }

    [Fact]
    public async Task A_release_without_a_checksum_is_not_installed()
    {
        var (client, _, _, _) = Setup();
        var release = UpdateCheck.ParseRelease(ReleaseJson("v0.4.20", withSha: false))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadAsync(release, Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N")), CancellationToken.None));
    }
}

public class SelfUpdateTests
{
    [Fact]
    public void Script_waits_for_the_process_swaps_the_file_keeps_a_backup_restores_on_failure_and_restarts()
    {
        var script = SelfUpdate.BuildScript(4242, "C:\\dl\\AltPane.exe", "C:\\tools\\AltPane.exe", "C:\\tmp\\err.txt");
        Assert.Contains("Wait-Process -Id 4242", script);
        Assert.Contains("$src = 'C:\\dl\\AltPane.exe'", script);
        Assert.Contains("$dst = 'C:\\tools\\AltPane.exe'", script);
        Assert.Contains("$old = $dst + '.old'", script);
        Assert.Contains("Copy-Item -LiteralPath $old -Destination $dst -Force", script);
        Assert.Contains("Set-Content -LiteralPath $err", script);
        Assert.EndsWith("Start-Process -FilePath $dst" + Environment.NewLine, script);
    }

    [Fact]
    public void Quote_doubles_single_quotes_so_a_path_cannot_break_out_of_the_string()
    {
        Assert.Equal("'C:\\a''b'", SelfUpdate.Quote("C:\\a'b"));
        Assert.Equal("'x''; Remove-Item -Recurse C:\\ ; ''y'", SelfUpdate.Quote("x'; Remove-Item -Recurse C:\\ ; 'y"));
    }
}
