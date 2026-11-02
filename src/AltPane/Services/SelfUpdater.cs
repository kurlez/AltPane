using System.Diagnostics;
using AltPane.Core.Improvements;
using AltPane.Core.Settings;

namespace AltPane;

/// <summary>
/// Replaces the running executable with a downloaded build. A hidden PowerShell script waits for this process
/// to exit, swaps the file (keeping the old one as .old) and starts the new one, so the caller must exit afterwards.
/// </summary>
internal static class SelfUpdater
{
    public static string ErrorFile => Path.Combine(Path.GetTempPath(), "altpane-update-error.txt");

    public static bool Start(string newExecutable)
    {
        try
        {
            var target = AppPaths.ResolveExecutable(Environment.ProcessPath);
            if (string.IsNullOrEmpty(target) || !File.Exists(newExecutable)) return false;
            try { File.Delete(ErrorFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            var script = Path.Combine(Path.GetTempPath(), "altpane-update-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(script, SelfUpdate.BuildScript(Environment.ProcessId, newExecutable, target, ErrorFile), new System.Text.UTF8Encoding(true));
            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script })
                info.ArgumentList.Add(argument);
            return Process.Start(info) is not null;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
    }
}
