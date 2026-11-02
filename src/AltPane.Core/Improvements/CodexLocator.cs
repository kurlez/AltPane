namespace AltPane.Core.Improvements;

/// <summary>Finds the native Codex executable and the AltPane source checkout.</summary>
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

    /// <summary>Walks up from a folder looking for AltPane.sln. Returns the folder that holds it.</summary>
    public static string? FindRepository(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory)) return null;
        try
        {
            var directory = new DirectoryInfo(startDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AltPane.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Treated as not found.
        }
        return null;
    }

    public static bool IsRepository(string? directory) =>
        !string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, "AltPane.sln"));
}
