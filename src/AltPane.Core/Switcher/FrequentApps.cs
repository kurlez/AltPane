using AltPane.Core.Filtering;
using AltPane.Core.Input;
using AltPane.Core.Models;

namespace AltPane.Core.Switcher;

/// <summary>One stretch of foreground time. Unsolicited samples are focus-stealing and do not rank.</summary>
public readonly record struct FocusSample(long UtcTicks, long Milliseconds, bool Solicited = true);

/// <summary>Focus accumulated for one process, plus the exe or shortcut used to launch it.</summary>
public sealed record AppFocusRecord(
    string Key,
    string DisplayName,
    string LaunchPath,
    IReadOnlyList<FocusSample> Samples);

/// <summary>An open window of an app. Lower <see cref="Recency"/> is more recently used.</summary>
public readonly record struct OpenAppWindow(string Key, long Handle, int Recency);

/// <summary>One sidebar slot. <see cref="Shortcut"/> is F1–F8 for the slot's position.</summary>
public sealed record FrequentApp(
    string Key,
    string Name,
    string LaunchPath,
    long FocusMilliseconds,
    bool Pinned,
    bool Running,
    long WindowHandle,
    string Shortcut);

/// <summary>
/// F1–F8 select sidebar slots. Alt may still be held. Ctrl and Shift never take part,
/// so Ctrl+1–0, Ctrl+Enter, Ctrl+Shift+Enter, and Ctrl+C stay on the main list.
/// </summary>
public static class SidebarShortcuts
{
    public const int SlotCount = 8;
    public const int F1 = 0x70;

    public static int? Index(int virtualKey)
    {
        if (virtualKey is >= F1 and < F1 + SlotCount) return virtualKey - F1;
        return null;
    }

    public static string Label(int index) =>
        index is >= 0 and < SlotCount ? "F" + (index + 1) : "";

    public static bool IsActivation(in HookInput input)
    {
        if (!input.SwitcherOpen || !input.IsKeyDown || input.IsRepeat) return false;
        if (Index(input.VirtualKey) is null) return false;
        if (input.Modifiers.Ctrl || input.Modifiers.Shift) return false;
        if (!input.Modifiers.Win) return true;
        var trigger = input.LatchedTrigger != ModifierKeys.None
            ? input.LatchedTrigger
            : HotkeyLogic.TriggerModifier(input.Hotkey ?? HotkeyChord.AltTab());
        return trigger == ModifierKeys.Win;
    }
}

/// <summary>
/// Ranks apps by real focus time inside a rolling window. Pinned apps stay first,
/// in the saved order. Hidden apps, the demote list (including built-in VPN tools),
/// AltPane itself, and focus-stealing samples are left out.
/// </summary>
public static class FrequentApps
{
    public const int DefaultCount = 8;
    public const int MinCount = 6;
    public const int MaxCount = 8;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(7);
    public const string SelfProcess = "altpane";

    public static int ClampCount(int count)
    {
        if (count < MinCount) return MinCount;
        if (count > MaxCount) return MaxCount;
        return count;
    }

    public static bool IsSelf(string? key) =>
        string.Equals(ProcessNames.Normalize(key), SelfProcess, StringComparison.OrdinalIgnoreCase);

    public static bool IsShortcut(string? path)
    {
        var ext = Path.GetExtension(path ?? "");
        return ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".url", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A shortcut replaces an exe. An empty incoming path leaves the current one.</summary>
    public static string PreferLaunchPath(string? current, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming)) return current?.Trim() ?? "";
        var next = incoming.Trim();
        if (string.IsNullOrWhiteSpace(current)) return next;
        var kept = current.Trim();
        if (IsShortcut(kept) && !IsShortcut(next)) return kept;
        return next;
    }

    public static string ShortName(string? displayName, string key)
    {
        var text = string.IsNullOrWhiteSpace(displayName) ? key : displayName.Trim();
        var slash = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));
        if (slash >= 0 && slash < text.Length - 1)
            text = Path.GetFileNameWithoutExtension(text[(slash + 1)..]);
        return text.Length == 0 ? key : text;
    }

    public static IReadOnlyList<FrequentApp> Rank(
        IReadOnlyList<AppFocusRecord>? records,
        DateTime utcNow,
        TimeSpan window,
        int count,
        IReadOnlyList<string>? pinned,
        IReadOnlyCollection<string>? hidden,
        IReadOnlyCollection<string>? demoted,
        IReadOnlyList<OpenAppWindow>? open)
    {
        if (utcNow.Kind == DateTimeKind.Unspecified)
            utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        count = ClampCount(count <= 0 ? DefaultCount : count);
        if (window < TimeSpan.Zero) window = TimeSpan.Zero;
        var cutoff = utcNow - window;
        var totals = new Dictionary<string, Aggregate>(StringComparer.OrdinalIgnoreCase);
        if (records is not null)
        {
            foreach (var record in records)
            {
                var key = ProcessNames.Normalize(record.Key);
                if (key.Length == 0 || IsSelf(key)) continue;
                if (!totals.TryGetValue(key, out var agg))
                {
                    agg = new Aggregate(key);
                    totals[key] = agg;
                }
                agg.Absorb(record, cutoff.Ticks, utcNow.Ticks);
            }
        }

        var result = new List<FrequentApp>(count);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (pinned is not null)
        {
            foreach (var raw in pinned)
            {
                if (result.Count >= count) break;
                var key = ProcessNames.Normalize(raw);
                if (key.Length == 0 || IsSelf(key) || IsListed(hidden, key)) continue;
                totals.TryGetValue(key, out var agg);
                var canonical = agg?.Key ?? key;
                if (!used.Add(canonical)) continue;
                result.Add(ToApp(canonical, agg, pinned: true, open));
            }
        }

        var rest = new List<Aggregate>();
        foreach (var agg in totals.Values)
        {
            if (used.Contains(agg.Key) || IsListed(hidden, agg.Key)) continue;
            if (AppPriorityLists.IsDemoted(null, demoted, agg.Key)) continue;
            if (agg.FocusMilliseconds <= 0) continue;
            rest.Add(agg);
        }
        rest.Sort(static (a, b) =>
        {
            var byTime = b.FocusMilliseconds.CompareTo(a.FocusMilliseconds);
            if (byTime != 0) return byTime;
            var byRecent = b.LatestTicks.CompareTo(a.LatestTicks);
            if (byRecent != 0) return byRecent;
            return StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key);
        });
        foreach (var agg in rest)
        {
            if (result.Count >= count) break;
            result.Add(ToApp(agg.Key, agg, pinned: false, open));
        }

        for (var i = 0; i < result.Count; i++)
            result[i] = result[i] with { Shortcut = SidebarShortcuts.Label(i) };
        return result;
    }

    /// <summary>Keeps the open-time order and only refreshes the running dot and window handle.</summary>
    public static IReadOnlyList<FrequentApp> WithRunning(
        IReadOnlyList<FrequentApp> order,
        IReadOnlyList<OpenAppWindow>? open)
    {
        if (order.Count == 0) return order;
        var next = new FrequentApp[order.Count];
        for (var i = 0; i < order.Count; i++)
        {
            var handle = BestHandle(order[i].Key, open);
            next[i] = order[i] with { Running = handle != 0, WindowHandle = handle };
        }
        return next;
    }

    static FrequentApp ToApp(string key, Aggregate? agg, bool pinned, IReadOnlyList<OpenAppWindow>? open)
    {
        var handle = BestHandle(key, open);
        var name = agg is null || string.IsNullOrWhiteSpace(agg.DisplayName) ? key : ShortName(agg.DisplayName, key);
        return new FrequentApp(
            key,
            name,
            agg?.LaunchPath ?? "",
            agg?.FocusMilliseconds ?? 0,
            pinned,
            handle != 0,
            handle,
            "");
    }

    static long BestHandle(string key, IReadOnlyList<OpenAppWindow>? open)
    {
        if (open is null || open.Count == 0) return 0;
        long handle = 0;
        var best = int.MaxValue;
        var found = false;
        foreach (var item in open)
        {
            if (item.Handle == 0) continue;
            if (!string.Equals(ProcessNames.Normalize(item.Key), key, StringComparison.OrdinalIgnoreCase)) continue;
            if (!found || item.Recency < best)
            {
                found = true;
                best = item.Recency;
                handle = item.Handle;
            }
        }
        return handle;
    }

    static bool IsListed(IReadOnlyCollection<string>? items, string key)
    {
        if (items is null || key.Length == 0) return false;
        foreach (var item in items)
        {
            if (string.Equals(ProcessNames.Normalize(item), key, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    sealed class Aggregate
    {
        public Aggregate(string key) => Key = key;
        public string Key { get; }
        public string DisplayName { get; private set; } = "";
        public string LaunchPath { get; private set; } = "";
        public long FocusMilliseconds { get; private set; }
        public long LatestTicks { get; private set; }

        public void Absorb(AppFocusRecord record, long cutoffTicks, long nowTicks)
        {
            if (!string.IsNullOrWhiteSpace(record.DisplayName))
                DisplayName = record.DisplayName.Trim();
            LaunchPath = FrequentApps.PreferLaunchPath(LaunchPath, record.LaunchPath);
            if (record.Samples is null) return;
            foreach (var sample in record.Samples)
            {
                if (!sample.Solicited || sample.Milliseconds <= 0) continue;
                if (sample.UtcTicks < cutoffTicks || sample.UtcTicks > nowTicks) continue;
                FocusMilliseconds += sample.Milliseconds;
                if (sample.UtcTicks > LatestTicks) LatestTicks = sample.UtcTicks;
            }
        }
    }
}

/// <summary>User-ordered sidebar pins and the hide list. A hide removes a pin. AltPane cannot be pinned.</summary>
public static class SidebarLists
{
    public static void Pin(AppSettings settings, string? key)
    {
        var name = ProcessNames.Normalize(key);
        if (name.Length == 0 || FrequentApps.IsSelf(name)) return;
        settings.SidebarHiddenApps = Without(settings.SidebarHiddenApps, name);
        if (Contains(settings.SidebarPinnedApps, name)) return;
        var next = Copy(settings.SidebarPinnedApps);
        next.Add(name);
        settings.SidebarPinnedApps = next;
    }

    public static void Unpin(AppSettings settings, string? key)
    {
        var name = ProcessNames.Normalize(key);
        if (name.Length == 0) return;
        settings.SidebarPinnedApps = Without(settings.SidebarPinnedApps, name);
    }

    public static void Hide(AppSettings settings, string? key)
    {
        var name = ProcessNames.Normalize(key);
        if (name.Length == 0 || FrequentApps.IsSelf(name)) return;
        settings.SidebarPinnedApps = Without(settings.SidebarPinnedApps, name);
        if (Contains(settings.SidebarHiddenApps, name)) return;
        var next = Copy(settings.SidebarHiddenApps);
        next.Add(name);
        settings.SidebarHiddenApps = next;
    }

    public static bool IsPinned(IReadOnlyCollection<string>? pinned, string key) => Contains(pinned, key);

    public static void Sanitize(AppSettings settings)
    {
        if (settings.FrequentSidebarCount <= 0)
            settings.FrequentSidebarCount = FrequentApps.DefaultCount;
        settings.FrequentSidebarCount = FrequentApps.ClampCount(settings.FrequentSidebarCount);
        settings.SidebarHiddenApps = Clean(settings.SidebarHiddenApps);
        settings.SidebarPinnedApps = RemoveAny(Clean(settings.SidebarPinnedApps), settings.SidebarHiddenApps);
    }

    static List<string> Clean(IReadOnlyList<string>? items)
    {
        var next = new List<string>();
        if (items is null) return next;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in items)
        {
            var name = ProcessNames.Normalize(raw);
            if (name.Length == 0 || FrequentApps.IsSelf(name) || !seen.Add(name)) continue;
            next.Add(name);
        }
        return next;
    }

    static List<string> RemoveAny(List<string> items, IReadOnlyCollection<string>? hidden)
    {
        if (hidden is null || hidden.Count == 0) return items;
        items.RemoveAll(item => Contains(hidden, item));
        return items;
    }

    static bool Contains(IReadOnlyCollection<string>? items, string key)
    {
        if (items is null) return false;
        foreach (var item in items)
        {
            if (string.Equals(ProcessNames.Normalize(item), key, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static List<string> Copy(List<string>? items)
    {
        var next = new List<string>();
        if (items is null) return next;
        foreach (var item in items)
        {
            var name = ProcessNames.Normalize(item);
            if (name.Length == 0) continue;
            next.Add(name);
        }
        return next;
    }

    static List<string> Without(List<string>? items, string key)
    {
        var next = Copy(items);
        next.RemoveAll(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
        return next;
    }
}
