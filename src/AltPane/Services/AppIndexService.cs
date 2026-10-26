using System.Collections.Concurrent;
using AltPane.Core.Apps;
using AltPane.Core.Models;
using AltPane.Core.Settings;

namespace AltPane;

/// <summary>
/// Builds the installed-app index off the UI thread, caches it, and applies
/// shortcut changes as they arrive. Search only reads the latest snapshot.
/// </summary>
internal sealed class AppIndexService : IDisposable
{
    readonly Func<AppSettings> _settings;
    readonly object _gate = new();
    readonly string _indexPath;
    readonly string _historyPath;
    readonly List<FileSystemWatcher> _watchers = new();
    readonly EverythingClient _everything = new();
    IReadOnlyList<AppEntry> _all = Array.Empty<AppEntry>();
    IReadOnlyList<AppEntry> _visible = Array.Empty<AppEntry>();
    IReadOnlyList<AppEntry> _extra = Array.Empty<AppEntry>();
    string _searchQuery = "";
    int _searchGeneration;
    Timer? _search;
    Dictionary<string, LaunchMemory> _usage = new(StringComparer.OrdinalIgnoreCase);
    Timer? _save;
    Timer? _periodic;
    int _sources;
    bool _started;
    bool _disposed;

    public AppIndexService(Func<AppSettings> settings)
    {
        _settings = settings;
        var directory = Path.GetDirectoryName(SettingsStore.DefaultPath()) ?? "";
        _indexPath = Path.Combine(directory, "apps.json");
        _historyPath = Path.Combine(directory, "launches.json");
    }

    public event Action? Updated;

    public IReadOnlyList<AppEntry> Visible
    {
        get { lock (_gate) return _visible; }
    }

    public EverythingStatus EverythingStatus() => _everything.Probe();

    public bool TryStartEverything() => _everything.TryStart();

    /// <summary>Built-in catalog plus Everything hits for this exact query. Hits are not written to apps.json.</summary>
    public IReadOnlyList<AppEntry> ForQuery(string? query)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(query) || _extra.Count == 0 || !string.Equals(_searchQuery, query.Trim(), StringComparison.Ordinal))
                return _visible;
            return EverythingIndex.Combine(_visible, _extra);
        }
    }

    public void ScheduleSearch(string? query)
    {
        var text = query?.Trim() ?? "";
        var generation = Interlocked.Increment(ref _searchGeneration);
        lock (_gate)
        {
            _searchQuery = text;
            _extra = Array.Empty<AppEntry>();
        }
        Updated?.Invoke();
        var settings = _settings();
        if (text.Length == 0 || !settings.LaunchApps || !settings.UseEverything) return;
        _search ??= new Timer(RunSearch, null, Timeout.Infinite, Timeout.Infinite);
        _search.Change(40, Timeout.Infinite);
    }

    public IReadOnlyDictionary<string, LaunchMemory> UsageSnapshot()
    {
        lock (_gate) return _usage;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var cached = AppCatalog.LoadIndex(_indexPath);
                var history = AppCatalog.LoadHistory(_historyPath);
                Publish(cached, history, save: false);
                FullScan();
                BindWatchers();
                _periodic = new Timer(_ => FullScan(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
            }
        });
    }

    public void ApplySettings()
    {
        var sources = SourceMask(_settings());
        var changed = sources != Volatile.Read(ref _sources);
        lock (_gate)
            _visible = Filter(_all, _settings());
        Updated?.Invoke();
        if (!changed) return;
        BindWatchers();
        ThreadPool.QueueUserWorkItem(_ => FullScan());
    }

    public void Remember(string id)
    {
        lock (_gate)
            _usage = new Dictionary<string, LaunchMemory>(AppCatalog.Remember(_usage, id, DateTime.UtcNow), StringComparer.OrdinalIgnoreCase);
        ScheduleSave();
        Updated?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _search?.Dispose();
        _periodic?.Dispose();
        _save?.Dispose();
        lock (_watchers)
        {
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
        }
    }

    void FullScan()
    {
        try
        {
            var settings = _settings();
            Volatile.Write(ref _sources, SourceMask(settings));
            var found = new List<AppEntry>();
            if (settings.IndexStartMenu)
                found.AddRange(ScanShortcuts(StartMenuRoots(), AppSource.StartMenu));
            if (settings.IndexDesktop)
                found.AddRange(ScanShortcuts(DesktopRoots(), AppSource.Desktop));
            if (settings.IndexPath)
                found.AddRange(ScanPath());
            if (settings.IndexStoreApps)
                found.AddRange(ScanStore());
            Publish(AppCatalog.Merge(found), usage: null, save: true);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    void Publish(IReadOnlyList<AppEntry> all, Dictionary<string, LaunchMemory>? usage, bool save)
    {
        lock (_gate)
        {
            _all = all;
            _visible = Filter(all, _settings());
            if (usage is not null) _usage = usage;
        }
        if (save) ScheduleSave();
        Updated?.Invoke();
    }

    static IReadOnlyList<AppEntry> Filter(IReadOnlyList<AppEntry> all, AppSettings settings)
    {
        var list = new List<AppEntry>(all.Count);
        foreach (var app in all)
        {
            if (!SourceEnabled(settings, app.Source)) continue;
            list.Add(app);
        }
        return list;
    }

    static bool SourceEnabled(AppSettings settings, AppSource source) => source switch
    {
        AppSource.StartMenu => settings.IndexStartMenu,
        AppSource.Store => settings.IndexStoreApps,
        AppSource.Desktop => settings.IndexDesktop,
        AppSource.Path => settings.IndexPath,
        _ => false
    };

    static int SourceMask(AppSettings settings) =>
        (settings.IndexStartMenu ? 1 : 0)
        | (settings.IndexStoreApps ? 2 : 0)
        | (settings.IndexDesktop ? 4 : 0)
        | (settings.IndexPath ? 8 : 0);

    void ScheduleSave()
    {
        _save ??= new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
        _save.Change(1500, Timeout.Infinite);
    }

    void SaveNow()
    {
        try
        {
            IReadOnlyList<AppEntry> apps;
            Dictionary<string, LaunchMemory> usage;
            lock (_gate)
            {
                apps = _all;
                usage = _usage;
            }
            AppCatalog.SaveIndex(_indexPath, apps);
            AppCatalog.SaveHistory(_historyPath, usage);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    void BindWatchers()
    {
        lock (_watchers)
        {
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
            var settings = _settings();
            if (settings.IndexStartMenu)
            {
                foreach (var root in StartMenuRoots()) Watch(root, AppSource.StartMenu);
            }
            if (settings.IndexDesktop)
            {
                foreach (var root in DesktopRoots()) Watch(root, AppSource.Desktop);
            }
        }
    }

    void Watch(string root, AppSource source)
    {
        if (!Directory.Exists(root)) return;
        var watcher = new FileSystemWatcher(root, "*.lnk")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        FileSystemEventHandler onChange = (_, e) => ApplyFile(e.FullPath, source, deleted: false);
        watcher.Created += onChange;
        watcher.Changed += onChange;
        watcher.Deleted += (_, e) => ApplyFile(e.FullPath, source, deleted: true);
        watcher.Renamed += (_, e) =>
        {
            ApplyFile(e.OldFullPath, source, deleted: true);
            ApplyFile(e.FullPath, source, deleted: false);
        };
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    void ApplyFile(string path, AppSource source, bool deleted)
    {
        try
        {
            IReadOnlyList<AppEntry> current;
            lock (_gate) current = _all;
            IReadOnlyList<AppEntry> next;
            if (deleted || !File.Exists(path))
            {
                next = AppCatalog.RemoveLaunchPath(current, path);
            }
            else
            {
                var bytes = File.ReadAllBytes(path);
                if (!ShellLinkReader.TryRead(bytes, out var link)) return;
                var entry = AppCatalog.FromShortcut(path, link, source);
                if (entry is null) return;
                next = AppCatalog.Upsert(current, entry);
            }
            Publish(next, usage: null, save: true);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    static IEnumerable<AppEntry> ScanShortcuts(IEnumerable<string> roots, AppSource source)
    {
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
                continue;
            }

            foreach (var file in files)
            {
                AppEntry? entry = null;
                try
                {
                    if (ShellLinkReader.TryRead(File.ReadAllBytes(file), out var link))
                        entry = AppCatalog.FromShortcut(file, link, source);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex);
                }
                if (entry is not null) yield return entry;
            }
        }
    }

    static IEnumerable<AppEntry> ScanPath()
    {
        var variable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(variable)) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in variable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = raw.Trim().Trim('"');
            if (dir.Length == 0 || !Directory.Exists(dir) || !seen.Add(dir)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.exe");
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (AppCatalog.IsNoisyPathExecutable(file)) continue;
                var name = Path.GetFileNameWithoutExtension(file);
                yield return AppCatalog.Create(name, file, AppKind.Executable, AppSource.Path, file, "", dir, file, 0);
            }
        }
    }

    static IEnumerable<AppEntry> ScanStore()
    {
        var found = new ConcurrentBag<AppEntry>();
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type is null) return;
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                if (folder is null) return;
                foreach (dynamic item in folder.Items())
                {
                    string name;
                    string path;
                    try
                    {
                        name = item.Name;
                        path = item.Path;
                    }
                    catch
                    {
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                    found.Add(AppCatalog.Create(name, path, AppKind.Store, AppSource.Store, "", "", "", "", 0));
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(25)) && error is not null)
            AppLog.Error(error);
        else if (error is not null)
            AppLog.Error(error);
        return found.ToArray();
    }

    static IEnumerable<string> StartMenuRoots()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu");
    }

    void RunSearch(object? _)
    {
        int generation;
        string query;
        lock (_gate)
        {
            generation = _searchGeneration;
            query = _searchQuery;
        }
        var settings = _settings();
        var built = EverythingIndex.Build(query, settings.EverythingExtensions, settings.EverythingExcludedPaths);
        if (built is null || generation != Volatile.Read(ref _searchGeneration)) return;
        IReadOnlyList<string> paths;
        try
        {
            paths = _everything.Query(built, 30, 200);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return;
        }
        if (generation != Volatile.Read(ref _searchGeneration)) return;
        var entries = new List<AppEntry>();
        foreach (var path in paths)
        {
            AppEntry? entry = null;
            try
            {
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(path)
                    && ShellLinkReader.TryRead(File.ReadAllBytes(path), out var link))
                {
                    entry = AppCatalog.FromShortcut(path, link, EverythingIndex.ClassifyPath(path));
                }
            }
            catch
            {
                entry = null;
            }
            entry ??= EverythingIndex.FromFile(path);
            if (entry is null || EverythingIndex.IsNoise(entry.ResolvedPath) || EverythingIndex.IsNoise(entry.LaunchPath))
                continue;
            entries.Add(entry);
        }
        lock (_gate)
        {
            if (generation != _searchGeneration) return;
            _extra = entries;
        }
        Updated?.Invoke();
    }

    static IEnumerable<string> DesktopRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    }
}
