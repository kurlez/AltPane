using System.Diagnostics;
using System.Text;

namespace AltPane.Core.Improvements;

/// <summary>Runs <c>codex app-server</c> and speaks JSON lines over its stdin and stdout.</summary>
public sealed class ProcessRpcTransport : IRpcTransport
{
    readonly Process _process;
    readonly SemaphoreSlim _writeGate = new(1, 1);

    ProcessRpcTransport(Process process) => _process = process;

    public static ProcessStartInfo CreateStartInfo(string executable, string workingDirectory)
    {
        CodexLocator.ValidateExecutable(executable);
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        info.ArgumentList.Add("app-server");
        return info;
    }

    public static ProcessRpcTransport Start(string executable, string workingDirectory)
    {
        var process = new Process { StartInfo = CreateStartInfo(executable, workingDirectory) };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            process.Dispose();
            throw new InvalidOperationException("无法启动 Codex。请安装 Codex CLI，执行 codex login，并选择原生 codex.exe 路径。");
        }
        // Drain stderr so a chatty server never blocks on a full pipe. Nothing is kept.
        _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        return new ProcessRpcTransport(process);
    }

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken) =>
        _process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();

    public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        _process.Dispose();
        _writeGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
