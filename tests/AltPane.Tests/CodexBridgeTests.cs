using AltPane.Core.AI;
using Xunit;

namespace AltPane.Tests;

public class CodexBridgeTests
{
    [Fact]
    public void Prompt_preserves_text_and_distinguishes_translation_from_formatting()
    {
        const string text = "原文\n  `hello` $HOME \"test\"";
        var translation = CodexBridge.BuildPrompt(text, TextAction.Translate, "English");
        Assert.Contains("into English", translation);
        Assert.EndsWith(text, translation);
        var format = CodexBridge.BuildPrompt(text, TextAction.Format, "English");
        Assert.Contains("Preserve its language", format);
        Assert.EndsWith(text, format);
        Assert.Throws<ArgumentException>(() => CodexBridge.BuildPrompt(" ", TextAction.Translate, "English"));
        Assert.Throws<ArgumentException>(() => CodexBridge.BuildPrompt(new string('a', 100_001), TextAction.Format, "English"));
    }

    [Fact]
    public void Process_uses_argument_list_stdin_and_read_only_sandbox()
    {
        var info = CodexBridge.CreateStartInfo("C:\\Program Files\\codex.exe", "/tmp", "/tmp/result with spaces.txt");
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardInput);
        Assert.Contains("read-only", info.ArgumentList);
        Assert.Contains("/tmp/result with spaces.txt", info.ArgumentList);
        Assert.Equal("-", info.ArgumentList.Last());
        Assert.Throws<ArgumentException>(() => CodexBridge.CreateStartInfo("codex.cmd", "/tmp", "/tmp/out"));
    }

    [Fact]
    public async Task Bridge_sends_unicode_input_and_reads_only_the_final_message()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "altpane-fake-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(path, "#!/bin/sh\nwhile [ $# -gt 0 ]; do\n if [ \"$1\" = '--output-last-message' ]; then shift; out=$1; fi\n shift\ndone\ncat > \"$out\"\necho diagnostic-output\necho diagnostic-error >&2\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var result = await new CodexBridge().TransformAsync(path, "你好\n$(echo unsafe)", TextAction.Translate, "English", CancellationToken.None);
            Assert.Equal(CodexBridge.BuildPrompt("你好\n$(echo unsafe)", TextAction.Translate, "English"), result);
            Assert.DoesNotContain("diagnostic-output", result);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Cancellation_stops_a_running_bridge_request()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "altpane-cancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(path, "#!/bin/sh\ncat > /dev/null\nexec sleep 30\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CodexBridge().TransformAsync(
                path, "hello", TextAction.Format, "English", cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Missing_executable_reports_actionable_error()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new CodexBridge().TransformAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "hello", TextAction.Format, "English", CancellationToken.None));
        Assert.Contains("codex login", error.Message);
    }
}
