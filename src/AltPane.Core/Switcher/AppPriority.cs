using System.Text.Json;
using System.Text.Json.Serialization;
using AltPane.Core.Filtering;
using AltPane.Core.Models;

namespace AltPane.Core.Switcher;

public enum UsageTier
{
    Pinned = 0,
    Frequent = 1,
    Infrequent = 2
}

/// <summary>
/// Process names that should not jump ahead of the apps a person actually uses.
/// Matched after <see cref="ProcessNames.Normalize"/>. The window process for 飞连
/// is CorpLink.exe; corplink-uc.exe and corplink-helper.exe are its resident modules.
/// </summary>
public static class LowPriorityApps
{
    public static IReadOnlyCollection<string> All => Names;

    static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "corplink", "corplink-uc", "corplink-helper", "feilian",
        "clash", "clash-win64", "clash-verge", "verge-mihomo", "v2rayn",
        "openvpn", "openvpn-gui", "pangpa", "pangps", "vpnui", "fortitray", "wireguard", "zsatray",
        "wechatappex", "wechatplayer", "wechatupdate", "wechatbrowser"
    };

    public static bool Contains(string? processName) =>
        Names.Contains(ProcessNames.Normalize(processName));
}

public static class AppIdentity
{
    public static string Key(WindowEntry entry)
    {
        if (entry is null) return "";
        var name = entry.HostedProcessName;
        if (string.IsNullOrWhiteSpace(name)) name = entry.KillProcessName;
        if (string.IsNullOrWhiteSpace(name)) name = entry.ProcessName;
        return ProcessNames.Normalize(name);
    }
}

public static class AppPriorityLists
{
    public static void Pin(AppSettings settings, string? key) =>
        Set(settings, key, pin: true, demote: false);

    public static void Demote(AppSettings settings, string? key) =>
        Set(settings, key, pin: false, demote: true);

    public static void Restore(AppSettings settings, string? key)
    {
        var name = ProcessNames.Normalize(key);
        if (name.Length == 0) return;
        settings.PinnedApps = Without(settings.PinnedApps, name);
        settings.DemotedApps = Without(settings.DemotedApps, name);
    }

    public static bool IsPinned(IReadOnlyCollection<string>? pinned, string key)
    {
        if (pinned is null || key.Length == 0) return false;
        foreach (var item in pinned)
        {
            if (string.Equals(ProcessNames.Normalize(item), key, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsUserDemoted(IReadOnlyCollection<string>? demoted, string key)
    {
        if (demoted is null || key.Length == 0) return false;
        foreach (var item in demoted)
        {
            if (string.Equals(ProcessNames.Normalize(item), key, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Built-in or chosen demotion. A pin wins, so a pinned VPN is not demoted.</summary>
    public static bool IsDemoted(IReadOnlyCollection<string>? pinned, IReadOnlyCollection<string>? demoted, string key)
    {
        if (key.Length == 0 || IsPinned(pinned, key)) return false;
        return IsUserDemoted(demoted, key) || LowPriorityApps.Contains(key);
    }

    static void Set(AppSettings settings, string? key, bool pin, bool demote)
    {
        var name = ProcessNames.Normalize(key);
        if (name.Length == 0) return;
        settings.PinnedApps = pin ? With(settings.PinnedApps, name) : Without(settings.PinnedApps, name);
        settings.DemotedApps = demote ? With(settings.DemotedApps, name) : Without(settings.DemotedApps, name);
    }

    static List<string> With(List<string>? items, string key)
    {
        var next = Without(items, key);
        next.Add(key);
        return next;
    }

    static List<string> Without(List<string>? items, string key)
    {
        var next = new List<string>();
        if (items is null) return next;
        foreach (var item in items)
        {
            var name = ProcessNames.Normalize(item);
            if (name.Length == 0 || string.Equals(name, key, StringComparison.OrdinalIgnoreCase)) continue;
            next.Add(name);
        }
        return next;
    }
}

/// <summary>A foreground change the user caused, or one AltPane caused by activating a window.</summary>
public static class UserActivation
{
    public const int InputWindowMs = 500;

    public static bool IsUserCaused(uint eventTick, uint lastInputTick, bool activatedBySwitcher)
    {
        if (activatedBySwitcher) return true;
        return unchecked(eventTick - lastInputTick) <= InputWindowMs;
    }
}

/// <summary>
/// A window counts only after it has been in front for <see cref="DwellMs"/>.
/// One that appears and is gone before that does not count. Self-activation is
/// reported separately from a key or click.
/// </summary>
public sealed class ForegroundDwell
{
    public const int DwellMs = 1000;
    long _current;
    long _since;
    bool _user;
    bool _committed;

    public DwellResult OnForeground(long hwnd, long nowMs, bool userCaused)
    {
        if (hwnd == _current)
            return Poll(nowMs);
        var left = Leave(nowMs);
        _current = hwnd;
        _since = nowMs;
        _user = userCaused && hwnd != 0;
        _committed = false;
        return left;
    }

    public DwellResult Poll(long nowMs)
    {
        if (_current == 0 || _committed) return default;
        if (nowMs - _since < DwellMs) return default;
        _committed = true;
        return _user
            ? new DwellResult(_current, 0, 0, 0)
            : new DwellResult(0, _current, 0, 0);
    }

    DwellResult Leave(long nowMs)
    {
        if (_current == 0) return default;
        var elapsed = Math.Max(0, nowMs - _since);
        if (!_committed)
        {
            if (elapsed < DwellMs) return default;
            return _user
                ? new DwellResult(_current, 0, _current, elapsed)
                : new DwellResult(0, _current, 0, 0);
        }
        return _user
            ? new DwellResult(0, 0, _current, elapsed)
            : default;
    }
}

public readonly record struct DwellResult(long UserActivated, long Unsolicited, long FocusEnded, long FocusMilliseconds);

public sealed class PrioritySnapshot
{
    readonly Dictionary<string, UsageTier> _tiers;
    readonly HashSet<string> _demoted;

    public PrioritySnapshot(Dictionary<string, UsageTier> tiers, HashSet<string> demoted)
    {
        _tiers = tiers;
        _demoted = demoted;
    }

    public static PrioritySnapshot Neutral { get; } = new(
        new Dictionary<string, UsageTier>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public UsageTier Tier(WindowEntry entry)
    {
        var key = AppIdentity.Key(entry);
        if (key.Length == 0) return UsageTier.Frequent;
        return _tiers.TryGetValue(key, out var tier) ? tier : UsageTier.Frequent;
    }

    public bool IsDemoted(WindowEntry entry)
    {
        var key = AppIdentity.Key(entry);
        return key.Length > 0 && _demoted.Contains(key);
    }

    /// <summary>Short tier: under the focus threshold, or demoted. These never take row 1.</summary>
    public bool IsShort(WindowEntry entry) => Tier(entry) == UsageTier.Infrequent;
}

/// <summary>
/// Decayed focus minutes per process. Only user-caused foreground time is added.
/// Half-life is 7 days. An app enters the main list at the threshold and leaves
/// below 80 percent of it. An app seen for less than a day, with no crossing yet,
/// stays in the main list so the first day is not all "rarely used".
/// </summary>
public sealed class AppUsageTable
{
    public const double HalfLifeDays = 7;
    public const int DefaultThresholdMinutes = 10;
    public const double Hysteresis = 0.8;
    public const double BootstrapDays = 1;
    public const double MaxFocusMinutes = 8 * 60;
    const int MaxApps = 128;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    readonly object _gate = new();
    readonly Dictionary<string, Row> _rows = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, LaunchNote> _launches = new(StringComparer.OrdinalIgnoreCase);

    public static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "AltPane", "usage.json");
    }

    /// <summary>Remember the app without adding focus. Starts the one-day bootstrap clock.</summary>
    public void NoteSeen(string? key, DateTime utc) => Touch(key, utc, 0);

    public void NoteFocus(string? key, TimeSpan focused, DateTime utc, string? displayName = null, string? launchPath = null)
    {
        if (focused <= TimeSpan.Zero) return;
        var minutes = Math.Min(MaxFocusMinutes, focused.TotalMinutes);
        if (minutes <= 0) return;
        var cap = (long)TimeSpan.FromMinutes(MaxFocusMinutes).TotalMilliseconds;
        var milliseconds = (long)Math.Min(cap, focused.TotalMilliseconds);
        Touch(key, utc, minutes, displayName, launchPath, milliseconds);
    }

    /// <summary>
    /// Remember how to start the app without adding focus. A shortcut replaces an exe.
    /// A process that has never been focused is kept only in memory, so the usage file
    /// stays the apps the person actually used.
    /// </summary>
    public void RememberLaunch(string? key, string? displayName, string? launchPath)
    {
        var name = ProcessNames.Normalize(key);
        if (name.Length == 0 || FrequentApps.IsSelf(name)) return;
        lock (_gate)
        {
            if (!_launches.TryGetValue(name, out var known))
                known = new LaunchNote();
            if (!string.IsNullOrWhiteSpace(displayName))
                known.DisplayName = displayName.Trim();
            known.LaunchPath = FrequentApps.PreferLaunchPath(known.LaunchPath, launchPath);
            _launches[name] = known;
            if (_rows.TryGetValue(name, out var row))
                ApplyLaunch(row, known.DisplayName, known.LaunchPath);
        }
    }

    public IReadOnlyList<AppFocusRecord> Export()
    {
        lock (_gate)
        {
            var records = new List<AppFocusRecord>(_rows.Count + _launches.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _rows)
            {
                seen.Add(pair.Key);
                _launches.TryGetValue(pair.Key, out var known);
                var name = !string.IsNullOrWhiteSpace(pair.Value.DisplayName)
                    ? pair.Value.DisplayName
                    : known?.DisplayName ?? "";
                var path = FrequentApps.PreferLaunchPath(pair.Value.LaunchPath, known?.LaunchPath);
                records.Add(new AppFocusRecord(pair.Key, name, path, CopySamples(pair.Value.Samples)));
            }
            foreach (var pair in _launches)
            {
                if (!seen.Add(pair.Key)) continue;
                records.Add(new AppFocusRecord(pair.Key, pair.Value.DisplayName, pair.Value.LaunchPath, Array.Empty<FocusSample>()));
            }
            return records;
        }
    }

    public PrioritySnapshot Snapshot(
        IReadOnlyCollection<string>? pinned,
        IReadOnlyCollection<string>? demoted,
        DateTime utc,
        int thresholdMinutes = DefaultThresholdMinutes)
    {
        var threshold = Math.Clamp(thresholdMinutes, 1, 24 * 60);
        var tiers = new Dictionary<string, UsageTier>(StringComparer.OrdinalIgnoreCase);
        var demotedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            foreach (var pair in _rows.ToArray())
            {
                var tier = Classify(pair.Value, pair.Key, pinned, demoted, utc, threshold);
                tiers[pair.Key] = tier;
                if (AppPriorityLists.IsDemoted(pinned, demoted, pair.Key))
                    demotedKeys.Add(pair.Key);
            }
        }

        if (pinned is not null)
        {
            foreach (var item in pinned)
            {
                var key = ProcessNames.Normalize(item);
                if (key.Length == 0) continue;
                tiers[key] = UsageTier.Pinned;
            }
        }

        void DemoteKey(string? raw)
        {
            var key = ProcessNames.Normalize(raw);
            if (key.Length == 0 || AppPriorityLists.IsPinned(pinned, key)) return;
            demotedKeys.Add(key);
            tiers[key] = UsageTier.Infrequent;
        }

        if (demoted is not null)
        {
            foreach (var item in demoted)
                DemoteKey(item);
        }

        foreach (var name in LowPriorityApps.All)
            DemoteKey(name);

        return new PrioritySnapshot(tiers, demotedKeys);
    }

    public void Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        UsageFile? file;
        try
        {
            file = JsonSerializer.Deserialize<UsageFile>(File.ReadAllText(path), Json);
        }
        catch (Exception)
        {
            return;
        }
        if (file?.Apps is null) return;
        lock (_gate)
        {
            _rows.Clear();
            foreach (var app in file.Apps)
            {
                var key = ProcessNames.Normalize(app.Key);
                if (key.Length == 0) continue;
                var row = new Row
                {
                    FocusMinutes = Math.Max(0, app.FocusMinutes),
                    Above = app.Above,
                    FirstSeenUtcTicks = app.FirstSeenUtcTicks,
                    UpdatedUtcTicks = app.UpdatedUtcTicks,
                    DisplayName = app.DisplayName ?? "",
                    LaunchPath = app.LaunchPath ?? ""
                };
                if (app.Samples is not null)
                {
                    foreach (var sample in app.Samples)
                    {
                        if (sample.Milliseconds <= 0 || sample.UtcTicks <= 0) continue;
                        row.Samples.Add(new Sample
                        {
                            UtcTicks = sample.UtcTicks,
                            Milliseconds = sample.Milliseconds,
                            Solicited = sample.Solicited
                        });
                    }
                }
                if (row.Samples.Count == 0 && row.FocusMinutes > 0 && row.UpdatedUtcTicks > 0)
                {
                    var seeded = (long)Math.Round(row.FocusMinutes * 60_000d);
                    if (seeded > 0)
                    {
                        row.Samples.Add(new Sample
                        {
                            UtcTicks = row.UpdatedUtcTicks,
                            Milliseconds = seeded,
                            Solicited = true
                        });
                    }
                }
                _rows[key] = row;
            }
        }
    }

    public void Save(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        UsageFile file;
        lock (_gate)
        {
            var apps = _rows
                .OrderByDescending(pair => pair.Value.FocusMinutes)
                .Take(MaxApps)
                .Select(pair => new UsageApp
                {
                    Key = pair.Key,
                    FocusMinutes = pair.Value.FocusMinutes,
                    Above = pair.Value.Above,
                    FirstSeenUtcTicks = pair.Value.FirstSeenUtcTicks,
                    UpdatedUtcTicks = pair.Value.UpdatedUtcTicks,
                    DisplayName = string.IsNullOrEmpty(pair.Value.DisplayName) ? null : pair.Value.DisplayName,
                    LaunchPath = string.IsNullOrEmpty(pair.Value.LaunchPath) ? null : pair.Value.LaunchPath,
                    Samples = pair.Value.Samples.Count == 0
                        ? null
                        : pair.Value.Samples.Select(sample => new UsageSample
                        {
                            UtcTicks = sample.UtcTicks,
                            Milliseconds = sample.Milliseconds,
                            Solicited = sample.Solicited
                        }).ToList()
                })
                .ToList();
            file = new UsageFile { Apps = apps };
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(file, Json));
        File.Move(temporary, path, overwrite: true);
    }

    void Touch(string? key, DateTime utc, double minutes, string? displayName = null, string? launchPath = null, long sampleMilliseconds = 0)
    {
        var name = ProcessNames.Normalize(key);
        if (name.Length == 0 || FrequentApps.IsSelf(name)) return;
        if (utc.Kind == DateTimeKind.Unspecified)
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        lock (_gate)
        {
            if (!_rows.TryGetValue(name, out var row))
            {
                row = new Row { FirstSeenUtcTicks = utc.Ticks };
                _rows[name] = row;
            }
            Decay(row, utc);
            if (row.FirstSeenUtcTicks <= 0) row.FirstSeenUtcTicks = utc.Ticks;
            row.FocusMinutes += minutes;
            row.UpdatedUtcTicks = utc.Ticks;
            ApplyLaunch(row, displayName, launchPath);
            if (sampleMilliseconds > 0)
            {
                row.Samples.Add(new Sample
                {
                    UtcTicks = utc.Ticks,
                    Milliseconds = sampleMilliseconds,
                    Solicited = true
                });
                Prune(row, utc);
            }
        }
    }

    static void ApplyLaunch(Row row, string? displayName, string? launchPath)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            row.DisplayName = displayName.Trim();
        if (!string.IsNullOrWhiteSpace(launchPath))
            row.LaunchPath = FrequentApps.PreferLaunchPath(row.LaunchPath, launchPath);
    }

    static void Prune(Row row, DateTime utc)
    {
        var cutoff = utc.AddDays(-(FrequentApps.DefaultWindow.TotalDays + 1)).Ticks;
        row.Samples.RemoveAll(sample => sample.UtcTicks < cutoff || sample.Milliseconds <= 0);
        const int cap = 128;
        if (row.Samples.Count > cap)
            row.Samples.RemoveRange(0, row.Samples.Count - cap);
    }

    static List<FocusSample> CopySamples(List<Sample> samples)
    {
        var copy = new List<FocusSample>(samples.Count);
        foreach (var sample in samples)
            copy.Add(new FocusSample(sample.UtcTicks, sample.Milliseconds, sample.Solicited));
        return copy;
    }

    UsageTier Classify(Row row, string key, IReadOnlyCollection<string>? pinned, IReadOnlyCollection<string>? demoted, DateTime utc, int threshold)
    {
        Decay(row, utc);
        row.UpdatedUtcTicks = utc.Ticks;
        if (AppPriorityLists.IsPinned(pinned, key)) return UsageTier.Pinned;
        if (AppPriorityLists.IsDemoted(pinned, demoted, key)) return UsageTier.Infrequent;

        var enter = threshold;
        var leave = threshold * Hysteresis;
        if (row.FocusMinutes >= enter)
            row.Above = true;
        else if (row.Above && row.FocusMinutes < leave)
            row.Above = false;

        if (row.Above) return UsageTier.Frequent;
        if (!ObservedLongEnough(row, utc)) return UsageTier.Frequent;
        return UsageTier.Infrequent;
    }

    static bool ObservedLongEnough(Row row, DateTime utc)
    {
        if (row.FirstSeenUtcTicks <= 0) return false;
        var first = new DateTime(row.FirstSeenUtcTicks, DateTimeKind.Utc);
        return utc - first >= TimeSpan.FromDays(BootstrapDays);
    }

    static void Decay(Row row, DateTime utc)
    {
        if (row.UpdatedUtcTicks <= 0) return;
        var previous = new DateTime(row.UpdatedUtcTicks, DateTimeKind.Utc);
        var elapsed = utc - previous;
        if (elapsed <= TimeSpan.Zero) return;
        var factor = Math.Pow(0.5, elapsed.TotalDays / HalfLifeDays);
        row.FocusMinutes *= factor;
    }

    sealed class Row
    {
        public double FocusMinutes;
        public bool Above;
        public long FirstSeenUtcTicks;
        public long UpdatedUtcTicks;
        public string DisplayName = "";
        public string LaunchPath = "";
        public List<Sample> Samples = new();
    }

    sealed class Sample
    {
        public long UtcTicks;
        public long Milliseconds;
        public bool Solicited = true;
    }

    sealed class LaunchNote
    {
        public string DisplayName = "";
        public string LaunchPath = "";
    }

    sealed class UsageFile
    {
        public List<UsageApp> Apps { get; set; } = new();
    }

    sealed class UsageApp
    {
        public string Key { get; set; } = "";
        public double FocusMinutes { get; set; }
        public bool Above { get; set; }
        public long FirstSeenUtcTicks { get; set; }
        public long UpdatedUtcTicks { get; set; }
        public string? DisplayName { get; set; }
        public string? LaunchPath { get; set; }
        public List<UsageSample>? Samples { get; set; }
    }

    sealed class UsageSample
    {
        public long UtcTicks { get; set; }
        public long Milliseconds { get; set; }
        public bool Solicited { get; set; } = true;
    }
}
