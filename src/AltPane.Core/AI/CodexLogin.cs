using System.Diagnostics;

namespace AltPane.Core.AI;

/// <summary>Delegates authentication and credential storage to the official CLI.</summary>
public sealed class CodexLogin
{
    public static ProcessStartInfo CreateStartInfo(string executable, bool interactive)
    {
        CodexBridge.ValidateExecutable(executable);
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = interactive,
            CreateNoWindow = !interactive,
            RedirectStandardOutput = !interactive,
            RedirectStandardError = !interactive,
            WorkingDirectory = Path.GetTempPath()
        };
        info.ArgumentList.Add("login");
        if (!interactive) info.ArgumentList.Add("status");
        return info;
    }

    /// <summary>A nonzero status means unconfirmed, not necessarily logged out.</summary>
    public async Task<bool> CheckAsync(string executable, CancellationToken cancellationToken) =>
        await RunAsync(executable, interactive: false, cancellationToken) == 0;

    public async Task LoginAsync(string executable, CancellationToken cancellationToken)
    {
        var exitCode = await RunAsync(executable, interactive: true, cancellationToken);
        if (exitCode != 0)
            throw new InvalidOperationException("Codex 登录未完成。请查看登录窗口提示后重试。");
    }

    static async Task<int> RunAsync(string executable, bool interactive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(interactive ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(15));
        using var process = new Process { StartInfo = CreateStartInfo(executable, interactive) };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("无法启动 Codex。请安装官方 Codex CLI，并选择原生 codex.exe 路径。");
        }
        using var registration = timeout.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        // Status output may contain account details. Do not retain or display it.
        var drain = interactive ? Task.CompletedTask : Task.WhenAll(
            process.StandardOutput.BaseStream.CopyToAsync(Stream.Null),
            process.StandardError.BaseStream.CopyToAsync(Stream.Null));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await drain.WaitAsync(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(interactive ? "Codex 登录等待超过十分钟，已停止。请重试。" : "检查登录状态超时，请重试。");
        }
    }
}
