using AltPane.Core.Filtering;

namespace AltPane.Core.Apps;

/// <summary>
/// Friendly application names taken from the installed-app index. A full path wins.
/// A process name is used only when exactly one indexed app has it, so two
/// programs that share an executable name are not merged. Anything else stays
/// the executable name. This does not read file versions.
/// </summary>
public static class AppDisplayNames
{
    static readonly object Gate = new();
    static Dictionary<string, string> _byPath = new(StringComparer.OrdinalIgnoreCase);
    static Dictionary<string, string> _byProcess = new(StringComparer.OrdinalIgnoreCase);

    public static void Replace(IReadOnlyList<AppEntry>? apps)
    {
        var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byProcess = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (apps is not null)
        {
            foreach (var app in apps)
            {
                if (string.IsNullOrWhiteSpace(app.Name)) continue;
                var path = string.IsNullOrWhiteSpace(app.ResolvedPath) ? app.LaunchPath : app.ResolvedPath;
                if (!string.IsNullOrWhiteSpace(path))
                    byPath[path] = app.Name;
                var process = ProcessNames.Normalize(app.ProcessName);
                if (process.Length == 0 && !string.IsNullOrWhiteSpace(path))
                    process = ProcessNames.Normalize(Path.GetFileNameWithoutExtension(path));
                if (process.Length == 0) continue;
                counts[process] = counts.TryGetValue(process, out var count) ? count + 1 : 1;
                byProcess[process] = app.Name;
            }
        }

        foreach (var pair in counts)
        {
            if (pair.Value != 1) byProcess.Remove(pair.Key);
        }

        lock (Gate)
        {
            _byPath = byPath;
            _byProcess = byProcess;
        }
    }

    public static string Resolve(string? processName, string? path)
    {
        Dictionary<string, string> byPath;
        Dictionary<string, string> byProcess;
        lock (Gate)
        {
            byPath = _byPath;
            byProcess = _byProcess;
        }

        if (!string.IsNullOrWhiteSpace(path) && byPath.TryGetValue(path, out var named))
            return named;
        var process = ProcessNames.Normalize(processName);
        if (process.Length > 0 && byProcess.TryGetValue(process, out named))
            return named;
        return process;
    }
}
