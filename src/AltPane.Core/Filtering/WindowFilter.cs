using AltPane.Core.Models;

namespace AltPane.Core.Filtering;

public static class WinStyles
{
    public const long WsExToolWindow = 0x00000080;
    public const long WsExAppWindow = 0x00040000;
    public const long WsExNoActivate = 0x08000000;
    public const int CloakApp = 0x1;
    public const int CloakShell = 0x2;
}

public sealed class FilterContext
{
    public int CurrentProcessId { get; init; }
    public bool IncludeOtherVirtualDesktops { get; init; }
    public IReadOnlyCollection<string>? ExcludedProcessNames { get; init; }
}

public static class ProcessNames
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var trimmed = name.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];
        return trimmed;
    }

    public static List<string> NormalizeList(IEnumerable<string>? names)
    {
        var list = new List<string>();
        if (names is null) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in names)
        {
            var name = Normalize(raw);
            if (name.Length == 0 || !seen.Add(name)) continue;
            list.Add(name);
        }
        return list;
    }

    public static string DisplayName(string? processName, string? hostedProcessName)
    {
        if (!string.IsNullOrWhiteSpace(hostedProcessName) &&
            string.Equals(processName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            return Normalize(hostedProcessName);
        return Normalize(processName);
    }

    public static bool IsExcluded(string? name, IReadOnlyCollection<string>? excluded)
    {
        if (excluded is null || excluded.Count == 0) return false;
        var normalized = Normalize(name);
        if (normalized.Length == 0) return false;
        foreach (var item in excluded)
        {
            if (string.Equals(Normalize(item), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

/// <summary>
/// Decides whether a top-level window belongs in an Alt+Tab list.
/// Rules follow the classic owner / tool-window contract, plus the Win10+ cloak bit
/// that hides UWP ghosts and windows parked on other virtual desktops.
/// </summary>
public static class WindowFilter
{
    static readonly HashSet<string> DeniedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "DV2ControlHost",
        "MsgrIMEWindowClass",
        "SysShadow",
        "DummyDWMListenerWindow",
        "Windows.Internal.Shell.TabProxyWindow",
        "MultitaskingViewFrame",
        "ForegroundStaging",
        "Windows.UI.Core.CoreWindow",
        "Shell_InputSwitchTopLevelWindow",
        "ApplicationManager_DesktopShellWindow"
    };

    /// <summary>
    /// False when the window is already rejected without knowing its process.
    /// The open path uses this to skip OpenProcess on windows that cannot appear.
    /// </summary>
    public static bool NeedsProcess(WindowCandidate candidate, FilterContext context)
    {
        if (candidate.Handle == 0 || !candidate.IsVisible) return false;
        if (context.CurrentProcessId != 0 && candidate.ProcessId == context.CurrentProcessId) return false;
        if (DeniedClasses.Contains(candidate.ClassName ?? "")) return false;
        if ((candidate.CloakFlags & WinStyles.CloakApp) != 0) return false;
        if (candidate.CloakFlags != 0 && !context.IncludeOtherVirtualDesktops) return false;
        if (!context.IncludeOtherVirtualDesktops && candidate.VirtualDesktopSame == 0) return false;

        var ex = candidate.ExStyle;
        var appWindow = (ex & WinStyles.WsExAppWindow) != 0;
        if ((ex & WinStyles.WsExToolWindow) != 0 && !appWindow) return false;
        if ((ex & WinStyles.WsExNoActivate) != 0 && !appWindow) return false;
        var owned = candidate.RootOwnerHandle != 0
            ? candidate.RootOwnerHandle != candidate.Handle
            : candidate.OwnerHandle != 0;
        if (owned && !appWindow) return false;
        if (string.IsNullOrWhiteSpace(candidate.Title)) return false;
        return true;
    }

    public static bool ShouldInclude(WindowCandidate candidate, FilterContext context)
    {
        if (!NeedsProcess(candidate, context)) return false;
        if (ProcessNames.IsExcluded(candidate.ProcessName, context.ExcludedProcessNames)) return false;
        if (ProcessNames.IsExcluded(candidate.HostedProcessName, context.ExcludedProcessNames)) return false;
        return true;
    }
}
