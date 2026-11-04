using System.Diagnostics;
using System.Text;

namespace AltPane.Core.Requests;

public sealed record CommandResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>The program could not be started, usually because it is not installed or not on PATH.</summary>
public sealed class CommandNotFoundException : Exception
{
    public CommandNotFoundException(string fileName, Exception? inner = null)
        : base($"无法启动 {fileName}。", inner) => FileName = fileName;

    public string FileName { get; }
}

/// <summary>Runs a command-line tool and captures its output. Tests supply a scripted one.</summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// Starts the program directly (no shell) and passes every argument through ProcessStartInfo.ArgumentList,
/// so user text can never be interpreted as shell syntax.
/// </summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory ?? "",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        // A credential or editor prompt would hang a window nobody can see.
        info.Environment["GH_PROMPT_DISABLED"] = "1";
        info.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";
        info.Environment["NO_COLOR"] = "1";
        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new CommandNotFoundException(fileName, ex);
        }
        process.StandardInput.Close();
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new CommandResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }
}
