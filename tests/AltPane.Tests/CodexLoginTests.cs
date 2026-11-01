using AltPane.Core.AI;
using Xunit;

namespace AltPane.Tests;

public class CodexLoginTests
{
    [Fact]
    public void Interactive_login_uses_official_flow_without_intercepting_credentials()
    {
        var info = CodexLogin.CreateStartInfo("C:\\Program Files\\Codex\\codex.exe", interactive: true);
        Assert.Equal(new[] { "login" }, info.ArgumentList);
        Assert.True(info.UseShellExecute);
        Assert.False(info.CreateNoWindow);
        Assert.False(info.RedirectStandardInput);
        Assert.False(info.RedirectStandardOutput);
        Assert.False(info.RedirectStandardError);
        Assert.Throws<ArgumentException>(() => CodexLogin.CreateStartInfo("codex.cmd", interactive: true));
    }

    [Fact]
    public void Status_check_runs_without_opening_a_login_window()
    {
        var info = CodexLogin.CreateStartInfo("codex.exe", interactive: false);
        Assert.Equal(new[] { "login", "status" }, info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public async Task Status_uses_exit_code_rather_than_output_or_account_details(int exitCode, bool expected)
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "altpane-login-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(path, $"#!/bin/sh\n[ \"$1\" = login ] && [ \"$2\" = status ] || exit 99\necho account-information >&2\nexit {exitCode}\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Assert.Equal(expected, await new CodexLogin().CheckAsync(path, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Cancelled_login_does_not_launch_an_executable()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CodexLogin().LoginAsync("missing-codex", cancelled.Token));
    }

    [Fact]
    public async Task Missing_cli_provides_installation_guidance()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CodexLogin().CheckAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), CancellationToken.None));
        Assert.Contains("codex.exe", error.Message);
    }
}
