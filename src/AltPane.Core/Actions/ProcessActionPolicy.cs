using AltPane.Core.Filtering;

namespace AltPane.Core.Actions;

public readonly record struct KillDecision(bool Allowed, string ReasonKey);

public static class ProcessActionPolicy
{
    static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "lsaiso", "svchost", "dwm", "fontdrvhost", "sihost",
        "explorer", "csrss", "securityhealthservice", "msmpeng", "secure system",
        "memory compression", "registry"
    };

    public static int ResolveKillProcessId(string? processName, int processId, int hostedProcessId)
    {
        if (hostedProcessId > 0 &&
            string.Equals(processName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            return hostedProcessId;
        return processId;
    }

    public static string ResolveKillProcessName(string? processName, string? hostedProcessName) =>
        ProcessNames.DisplayName(processName, hostedProcessName);

    public static KillDecision Evaluate(string? processName, int processId)
    {
        if (processId <= 4) return new KillDecision(false, "kill.protected");
        var name = ProcessNames.Normalize(processName);
        if (name.Length == 0) return new KillDecision(false, "kill.protected");
        if (name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            return new KillDecision(false, "kill.frameHost");
        if (Protected.Contains(name)) return new KillDecision(false, "kill.protected");
        return new KillDecision(true, "kill.ok");
    }
}
