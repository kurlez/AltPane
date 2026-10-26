using System.Text.Json;
using System.Text.Json.Serialization;
using AltPane.Core.Models;
using AltPane.Core.Search;

namespace AltPane.Core.Apps;

public static class AppCatalog
{
    public const int MaxResults = 8;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AppEntry Create(
        string name,
        string launchPath,
        AppKind kind,
        AppSource source,
        string? resolvedPath,
        string? arguments,
        string? workingDirectory,
        string? iconLocation,
        int iconIndex)
    {
        var resolved = (resolvedPath ?? "").Trim().Trim('"');
        var launch = (launchPath ?? "").Trim();
        var process = ProcessNameOf(resolved);
        if (process.Length == 0 && kind == AppKind.Store)
            process = FamilyName(launch);
        var icon = string.IsNullOrWhiteSpace(iconLocation) ? resolved : iconLocation.Trim();
        return new AppEntry
        {
            Id = MakeId(kind, launch, resolved),
            Name = name.Trim(),
            LaunchPath = launch,
            Kind = kind,
            Source = source,
            ResolvedPath = resolved,
            Arguments = arguments?.Trim() ?? "",
            WorkingDirectory = workingDirectory?.Trim() ?? "",
            IconLocation = icon,
            IconIndex = iconIndex,
            ProcessName = process
        };
    }

    public static AppEntry? FromShortcut(string lnkPath, in ShellLinkReader.LinkData link, AppSource source)
    {
        var name = Stem(lnkPath);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var resolved = ShellLinkReader.ResolveTarget(link);
        if (!IsAbsolutePath(resolved))
            resolved = CombineRelative(lnkPath, link.RelativePath) ?? resolved;
        var icon = link.IconLocation;
        if (string.IsNullOrWhiteSpace(icon)) icon = resolved;
        return Create(
            name,
            lnkPath,
            AppKind.Shortcut,
            source,
            resolved,
            link.Arguments,
            link.WorkingDirectory,
            icon,
            link.IconIndex);
    }

    public static string MakeId(AppKind kind, string launchPath, string resolvedPath)
    {
        if (!string.IsNullOrWhiteSpace(resolvedPath) && kind != AppKind.Store)
            return "exe:" + Norm(resolvedPath);
        if (kind == AppKind.Store)
            return "store:" + Norm(launchPath);
        return "path:" + Norm(launchPath);
    }

    public static int SourceRank(AppSource source) => source switch
    {
        AppSource.StartMenu => 3,
        AppSource.Store => 3,
        AppSource.Desktop => 2,
        AppSource.Path => 1,
        _ => 0
    };

    public static IReadOnlyList<AppEntry> Merge(IEnumerable<AppEntry> entries)
    {
        var best = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Name)) continue;
            if (string.IsNullOrWhiteSpace(entry.Id))
                entry.Id = MakeId(entry.Kind, entry.LaunchPath, entry.ResolvedPath);
            if (!best.TryGetValue(entry.Id, out var have) || SourceRank(entry.Source) > SourceRank(have.Source))
                best[entry.Id] = entry;
        }

        var storeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in best.Values)
        {
            if (entry.Kind == AppKind.Store) storeNames.Add(entry.Name);
        }

        if (storeNames.Count > 0)
        {
            var drop = best.Where(pair =>
                    pair.Value.Kind == AppKind.Shortcut
                    && storeNames.Contains(pair.Value.Name)
                    && string.IsNullOrWhiteSpace(pair.Value.ResolvedPath))
                .Select(pair => pair.Key)
                .ToList();
            foreach (var key in drop) best.Remove(key);
        }

        return best.Values.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<AppEntry> Upsert(IReadOnlyList<AppEntry> current, AppEntry entry)
    {
        var list = new List<AppEntry>(current.Count + 1);
        foreach (var item in current)
        {
            if (item is null) continue;
            if (SameLaunch(item, entry)) continue;
            if (string.Equals(item.Id, entry.Id, StringComparison.OrdinalIgnoreCase)
                && SourceRank(entry.Source) >= SourceRank(item.Source))
                continue;
            list.Add(item);
        }
        list.Add(entry);
        return Merge(list);
    }

    public static IReadOnlyList<AppEntry> RemoveLaunchPath(IReadOnlyList<AppEntry> current, string launchPath)
    {
        var list = new List<AppEntry>(current.Count);
        foreach (var item in current)
        {
            if (string.Equals(item.LaunchPath, launchPath, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(item);
        }
        return list;
    }

    public static bool IsNoisyPathExecutable(string fullPath)
    {
        var norm = fullPath.Replace('/', '\\').ToLowerInvariant();
        return norm.Contains("\\windows\\system32\\", StringComparison.Ordinal)
            || norm.Contains("\\windows\\syswow64\\", StringComparison.Ordinal)
            || norm.Contains("\\windows\\winsxs\\", StringComparison.Ordinal);
    }

    public static bool IsExcluded(AppEntry app, IReadOnlyList<string>? exclusions)
    {
        if (exclusions is null || exclusions.Count == 0) return false;
        foreach (var raw in exclusions)
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;
            if (token.Contains('\\') || token.Contains('/'))
            {
                var folder = token.Replace('/', '\\').TrimEnd('\\');
                if (Under(app.ResolvedPath, folder) || Under(app.LaunchPath, folder) || Under(app.IconLocation, folder))
                    return true;
                continue;
            }

            var name = StripExe(token);
            if (Eq(app.Name, name) || Eq(app.ProcessName, name) || Eq(FileStem(app.ResolvedPath), name) || Eq(FileStem(app.LaunchPath), name))
                return true;
        }
        return false;
    }

    public static List<AppHit> Rank(
        IReadOnlyList<AppEntry> apps,
        string? query,
        IReadOnlyDictionary<string, LaunchMemory>? usage,
        IReadOnlyList<WindowEntry> allWindows,
        IReadOnlyList<WindowEntry> listedWindows,
        IReadOnlyList<string>? exclusions,
        DateTime utcNow,
        int limit = MaxResults)
    {
        if (string.IsNullOrWhiteSpace(query) || apps.Count == 0 || limit <= 0)
            return new List<AppHit>();

        var ranked = new List<(AppEntry App, int Score, long SwitchHandle, string Name)>();
        foreach (var app in apps)
        {
            if (IsExcluded(app, exclusions)) continue;
            var quality = Quality(query, app);
            if (quality < 0) continue;
            var switchHandle = FindOpenWindow(app, allWindows);
            if (switchHandle != 0 && listedWindows.Any(window => window.Handle == switchHandle))
                continue;
            var score = quality + UsageBoost(app, usage, utcNow);
            ranked.Add((app, score, switchHandle, app.Name));
        }

        ranked.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        if (ranked.Count > limit) ranked.RemoveRange(limit, ranked.Count - limit);
        var hits = new List<AppHit>(ranked.Count);
        foreach (var item in ranked)
            hits.Add(new AppHit(item.App, item.Score, item.SwitchHandle));
        return hits;
    }

    public static int Quality(string query, AppEntry app)
    {
        var total = 0;
        var any = false;
        foreach (var token in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var score = ScoreToken(token, app);
            if (score < 0) return -1;
            any = true;
            total += score;
        }
        return any ? total : -1;
    }

    public static long FindOpenWindow(AppEntry app, IReadOnlyList<WindowEntry> windows)
    {
        long handle = 0;
        var best = int.MaxValue;
        foreach (var window in windows)
        {
            if (!WindowMatches(app, window)) continue;
            if (window.OriginalIndex < best)
            {
                best = window.OriginalIndex;
                handle = window.Handle;
            }
        }
        return handle;
    }

    public static IReadOnlyDictionary<string, LaunchMemory> Remember(
        IReadOnlyDictionary<string, LaunchMemory> current,
        string id,
        DateTime utcNow,
        int cap = 400)
    {
        var next = new Dictionary<string, LaunchMemory>(current, StringComparer.OrdinalIgnoreCase);
        next.TryGetValue(id, out var memory);
        memory ??= new LaunchMemory();
        memory.Count = Math.Min(memory.Count + 1, 10_000);
        memory.LastUtcTicks = utcNow.Ticks;
        next[id] = memory;
        if (next.Count <= cap) return next;
        var oldest = next.OrderBy(pair => pair.Value.LastUtcTicks).First().Key;
        next.Remove(oldest);
        return next;
    }

    public static void SaveIndex(string path, IReadOnlyList<AppEntry> apps)
    {
        WriteAtomic(path, JsonSerializer.Serialize(apps, Json));
    }

    public static IReadOnlyList<AppEntry> LoadIndex(string path)
    {
        try
        {
            if (!File.Exists(path)) return Array.Empty<AppEntry>();
            var apps = JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(path), Json);
            if (apps is null) return Array.Empty<AppEntry>();
            foreach (var app in apps)
            {
                if (string.IsNullOrWhiteSpace(app.Id))
                    app.Id = MakeId(app.Kind, app.LaunchPath, app.ResolvedPath);
            }
            return apps;
        }
        catch
        {
            return Array.Empty<AppEntry>();
        }
    }

    public static void SaveHistory(string path, IReadOnlyDictionary<string, LaunchMemory> usage) =>
        WriteAtomic(path, JsonSerializer.Serialize(usage, Json));

    public static Dictionary<string, LaunchMemory> LoadHistory(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, LaunchMemory>(StringComparer.OrdinalIgnoreCase);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, LaunchMemory>>(File.ReadAllText(path), Json);
            return loaded is null
                ? new Dictionary<string, LaunchMemory>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, LaunchMemory>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, LaunchMemory>(StringComparer.OrdinalIgnoreCase);
        }
    }

    static int ScoreToken(string token, AppEntry app)
    {
        var name = ScoreText(token, app.Name, preferName: true);
        var process = ScoreText(token, app.ProcessName, preferName: false);
        if (name < 0) return process;
        if (process < 0) return name;
        return Math.Max(name, process);
    }

    static int ScoreText(string token, string? text, bool preferName)
    {
        if (string.IsNullOrWhiteSpace(text)) return -1;
        var bonus = preferName ? 0 : -30;
        if (text.Equals(token, StringComparison.OrdinalIgnoreCase)) return 1000 + bonus;
        if (text.StartsWith(token, StringComparison.OrdinalIgnoreCase)) return 860 + bonus;
        if (text.Contains(token, StringComparison.OrdinalIgnoreCase)) return 740 + bonus;
        if (token.Length >= 2 && Pinyin.SpellHit(text, token)) return 700 + bonus;
        if (token.Length >= 2 && Pinyin.InitialsHit(text, token)) return 680 + bonus;
        if (token.Length >= 2 && SearchMatcher.IsAcronym(text, token)) return 660 + bonus;
        if (token.Length >= 2 && SearchMatcher.IsDenseSubsequence(text, token)) return 520 + bonus;
        if (token.Length >= 2 && (IsLooseSubsequence(text, token) || IsLooseSubsequence(Compact(text), token)))
            return 360 + bonus;
        return -1;
    }

    public static bool IsLooseSubsequence(string? text, string query)
    {
        if (string.IsNullOrEmpty(text) || query.Length < 2) return false;
        var start = -1;
        var last = -1;
        var queryIndex = 0;
        var index = 0;
        foreach (var ch in text)
        {
            if (queryIndex < query.Length && char.ToUpperInvariant(ch) == char.ToUpperInvariant(query[queryIndex]))
            {
                if (queryIndex == 0) start = index;
                last = index;
                queryIndex++;
            }
            index++;
        }
        if (queryIndex != query.Length || start < 0) return false;
        return last - start + 1 <= Math.Max(query.Length * 3, query.Length + 4);
    }

    static string Compact(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) builder.Append(ch);
        }
        return builder.ToString();
    }

    static int UsageBoost(AppEntry app, IReadOnlyDictionary<string, LaunchMemory>? usage, DateTime utcNow)
    {
        var source = SourceRank(app.Source) * 8;
        if (usage is null || !usage.TryGetValue(app.Id, out var memory) || memory.Count <= 0)
            return source;
        var frequency = Math.Min(memory.Count, 40) * 6;
        var last = new DateTime(memory.LastUtcTicks, DateTimeKind.Utc);
        var age = utcNow - last;
        var recent = age.TotalHours <= 6 ? 50
            : age.TotalDays <= 1 ? 36
            : age.TotalDays <= 7 ? 20
            : age.TotalDays <= 30 ? 8
            : 0;
        return source + frequency + recent;
    }

    static bool WindowMatches(AppEntry app, WindowEntry window)
    {
        if (!string.IsNullOrEmpty(app.ProcessName) && (
                Eq(window.ProcessName, app.ProcessName)
                || Eq(window.DisplayProcessName, app.ProcessName)
                || Eq(window.KillProcessName, app.ProcessName)
                || Eq(window.HostedProcessName, app.ProcessName)))
            return true;
        return !string.IsNullOrEmpty(app.Name)
            && string.Equals(window.Title, app.Name, StringComparison.OrdinalIgnoreCase);
    }

    static string ProcessNameOf(string resolved)
    {
        if (!IsAbsolutePath(resolved)) return "";
        return StripExe(FileName(resolved));
    }

    static string FamilyName(string aumid)
    {
        var id = aumid;
        var bang = id.IndexOf('!');
        if (bang > 0) id = id[..bang];
        var underscore = id.IndexOf('_');
        if (underscore > 0) id = id[..underscore];
        return id.Trim();
    }

    static string FileStem(string path) =>
        string.IsNullOrWhiteSpace(path) ? "" : StripExe(FileName(path));

    static string FileName(string path)
    {
        var trimmed = path.Trim().Trim('"').TrimEnd('\\', '/');
        var cut = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        var file = cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
        return file;
    }

    static string Stem(string path)
    {
        var file = FileName(path);
        var dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    static string StripExe(string value)
    {
        var text = value.Trim();
        return text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? text[..^4] : text;
    }

    static bool Eq(string? left, string right) =>
        !string.IsNullOrEmpty(left) && string.Equals(StripExe(left), right, StringComparison.OrdinalIgnoreCase);

    static bool Under(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var norm = path.Replace('/', '\\');
        return norm.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || norm.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);
    }

    static bool SameLaunch(AppEntry item, AppEntry entry) =>
        string.Equals(item.LaunchPath, entry.LaunchPath, StringComparison.OrdinalIgnoreCase);

    static string Norm(string value) => value.Trim().Trim('"').Replace('/', '\\').ToLowerInvariant();

    static bool IsAbsolutePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && (path.Contains(':') || path.StartsWith("\\\\", StringComparison.Ordinal));

    static string? CombineRelative(string lnkPath, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || IsAbsolutePath(relative)) return relative;
        var dir = Path.GetDirectoryName(lnkPath);
        if (string.IsNullOrEmpty(dir)) return relative;
        return Path.Combine(dir, relative);
    }

    static void WriteAtomic(string path, string json)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }
}
