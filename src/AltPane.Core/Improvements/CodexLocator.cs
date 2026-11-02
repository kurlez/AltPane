namespace AltPane.Core.Improvements;

/// <summary>Finds the native Codex executable.</summary>
public static class CodexLocator
{
    public static string ValidateExecutable(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
            throw new ArgumentException("找不到 Codex。请安装 Codex CLI 并在设置中选择原生 codex.exe。");
        if (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择原生 codex.exe，而不是 .cmd 或 .bat 启动脚本。");
        return executable;
    }

    /// <summary>The configured path wins. Otherwise the first codex.exe (codex on Linux and macOS) on PATH.</summary>
    public static string? Find(string? configured, string? pathVariable, bool windows)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();
        if (string.IsNullOrEmpty(pathVariable)) return null;
        var name = windows ? "codex.exe" : "codex";
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is skipped.
            }
        }
        return null;
    }
}
