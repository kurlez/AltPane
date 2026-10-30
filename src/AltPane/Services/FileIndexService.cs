using System.Runtime.InteropServices;
using AltPane.Core.Models;
using AltPane.Core.Search;

namespace AltPane;

/// <summary>
/// Builds the built-in file index off the UI thread at low priority, keeps a
/// compact snapshot in memory, and applies filesystem changes one path at a time.
/// While Everything is reachable the snapshot is kept and the disk is not walked.
/// </summary>
internal sealed class FileIndexService : IDisposable
{
    readonly Func<AppSettings> _settings;
    readonly IFileTree _tree;
    readonly string _snapshotPath;
    readonly Func<bool>? _everythingReachable;
    readonly AutoResetEvent _wake = new(false);
    readonly object _watchGate = new();
    readonly List<FileSystemWatcher> _watchers = new();
    readonly List<string> _pending = new();
    Timer? _debounce;
    Thread? _thread;
    int _generation;
    int _disposed;
    int _scannedGeneration = -1;
    bool _needFull;
    DateTimeOffset _fullNotBefore;
    DateTimeOffset _overflowLoggedUntil;
    string[] _watchRoots = Array.Empty<string>();
    IReadOnlyList<string>? _watchExclusions = Array.Empty<string>();
    volatile FileIndex _current = FileIndex.Empty;

    public FileIndexService(Func<AppSettings> settings)
        : this(settings, new PhysicalFileTree(), FileIndexStore.DefaultPath(), null)
    {
    }

    public FileIndexService(Func<AppSettings> settings, IFileTree tree, string snapshotPath)
        : this(settings, tree, snapshotPath, null)
    {
    }

    public FileIndexService(Func<AppSettings> settings, IFileTree tree, string snapshotPath, Func<bool>? everythingReachable)
    {
        _settings = settings;
        _tree = tree;
        _snapshotPath = snapshotPath;
        _everythingReachable = everythingReachable;
    }

    public FileIndex Current => _current;

    public void Start()
    {
        if (_thread is not null) return;
        _debounce = new Timer(_ => _wake.Set(), null, Timeout.Infinite, Timeout.Infinite);
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "AltPane.FileIndex",
            Priority = ThreadPriority.Lowest
        };
        _thread.Start();
    }

    public void ApplySettings()
    {
        lock (_watchGate)
        {
            Interlocked.Increment(ref _generation);
            _needFull = true;
            _fullNotBefore = DateTimeOffset.MinValue;
        }
        _wake.Set();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Increment(ref _generation);
        _wake.Set();
        try { _thread?.Join(TimeSpan.FromSeconds(2)); } catch { }
        _debounce?.Dispose();
        StopWatchers();
        _wake.Dispose();
    }

    void Loop()
    {
        TryBackground();
        try
        {
            var loaded = FileIndexStore.Load(_snapshotPath);
            if (loaded.Count > 0)
            {
                _current = loaded;
                AppLog.Info($"file index loaded {loaded.Count} entries, memory {loaded.EstimatedBytes} bytes");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }

        while (Volatile.Read(ref _disposed) == 0)
        {
            try
            {
                Pump();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
            }
            _wake.WaitOne();
        }
    }

    void Pump()
    {
        var settings = _settings();
        if (!settings.UseBuiltinFileIndex)
        {
            StopWatchers();
            return;
        }

        if (!FileIndexWatch.ShouldBackgroundScan(true, EverythingReachable()))
        {
            StopWatchers();
            Schedule(FileIndexWatch.ReachableRecheck);
            return;
        }

        bool needFull;
        DateTimeOffset fullNotBefore;
        lock (_watchGate)
        {
            needFull = _needFull;
            fullNotBefore = _fullNotBefore;
        }
        var now = DateTimeOffset.UtcNow;
        if (needFull && now < fullNotBefore)
        {
            Schedule(fullNotBefore - now);
            return;
        }

        var generation = Volatile.Read(ref _generation);
        if (_scannedGeneration == generation && !needFull)
        {
            if (TakePending() > 0)
                ApplyPending(settings.EverythingExcludedPaths);
            return;
        }

        FullScan(settings, generation);
    }

    void FullScan(AppSettings settings, int generation)
    {
        StopWatchers();
        lock (_watchGate) _pending.Clear();
        var exclusions = settings.EverythingExcludedPaths;
        var roots = DiscoverRoots(settings);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = FileIndexScan.Run(roots, _tree, exclusions);
        clock.Stop();
        if (generation != Volatile.Read(ref _generation) || Volatile.Read(ref _disposed) != 0)
            return;
        _current = result.Index;
        try
        {
            FileIndexStore.Save(_snapshotPath, result.Index);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
        lock (_watchGate)
        {
            _scannedGeneration = generation;
            _needFull = false;
        }
        ProcessFootprint.TrimOnce();
        AppLog.Info(
            $"file index scan {clock.ElapsedMilliseconds} ms, entries {result.Index.Count}, memory {result.Index.EstimatedBytes} bytes, working set {ProcessFootprint.Megabytes()} MB");
        BindWatchers(roots, exclusions);
    }

    int TakePending()
    {
        lock (_watchGate) return _pending.Count;
    }

    void ApplyPending(IReadOnlyList<string>? exclusions)
    {
        List<string> paths;
        lock (_watchGate)
        {
            if (_pending.Count == 0) return;
            paths = new List<string>(_pending);
            _pending.Clear();
        }
        var index = _current;
        foreach (var path in paths)
        {
            if (FileIndexWatch.Ignore(path, _watchRoots, exclusions)) continue;
            index = ApplyOne(index, path, exclusions);
        }
        if (ReferenceEquals(index, _current)) return;
        _current = index;
        try
        {
            FileIndexStore.Save(_snapshotPath, index);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    FileIndex ApplyOne(FileIndex index, string path, IReadOnlyList<string>? exclusions)
    {
        try
        {
            if (_tree.Exists(path))
            {
                var partial = FileIndexScan.Run(new[] { path }, _tree, exclusions);
                return FileIndexEdit.Absorb(FileIndexEdit.Remove(index, path), partial.Index);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return index;
        }
        try
        {
            if (File.Exists(path)) return FileIndexEdit.Upsert(index, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return index;
        }
        return FileIndexEdit.Remove(index, path);
    }

    bool EverythingReachable()
    {
        try
        {
            return _everythingReachable?.Invoke() == true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
    }

    void Schedule(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        try { _debounce?.Change(delay, Timeout.InfiniteTimeSpan); } catch { }
    }

    /// <summary>
    /// The profile root is not watched recursively. AppData sits under it and would
    /// overflow the watcher buffer. Each folder the scan actually enters is watched
    /// on its own, and the profile directory itself is watched only for direct children.
    /// </summary>
    static IEnumerable<(string Path, bool Recursive)> WatchTargets(IReadOnlyList<string> roots, IReadOnlyList<string>? exclusions)
    {
        var profile = FileIndexPaths.Normalize(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        foreach (var root in roots)
        {
            if (FileIndexWatch.IsAltPaneStore(root)) continue;
            if (profile.Length == 0 || !root.Equals(profile, StringComparison.OrdinalIgnoreCase))
            {
                yield return (root, true);
                continue;
            }

            yield return (root, false);
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var child in children)
            {
                if (ParentWouldSkip(child, exclusions) || FileIndexWatch.IsAltPaneStore(child)) continue;
                yield return (child, true);
            }
        }
    }

    void BindWatchers(IReadOnlyList<string> roots, IReadOnlyList<string>? exclusions)
    {
        StopWatchers();
        if (Volatile.Read(ref _disposed) != 0) return;
        _watchRoots = roots as string[] ?? roots.ToArray();
        _watchExclusions = exclusions;
        lock (_watchGate)
        {
            foreach (var target in WatchTargets(roots, exclusions))
            {
                try
                {
                    if (!Directory.Exists(target.Path)) continue;
                    var watcher = new FileSystemWatcher(target.Path)
                    {
                        IncludeSubdirectories = target.Recursive,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                        InternalBufferSize = 64 * 1024,
                        EnableRaisingEvents = false
                    };
                    watcher.Changed += OnWatcher;
                    watcher.Created += OnWatcher;
                    watcher.Deleted += OnWatcher;
                    watcher.Renamed += OnRenamed;
                    watcher.Error += OnWatcherError;
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex);
                }
            }
        }
    }

    void OnWatcher(object? sender, FileSystemEventArgs e) => Note(e.FullPath);

    void OnRenamed(object? sender, RenamedEventArgs e)
    {
        Note(e.OldFullPath);
        Note(e.FullPath);
    }

    void Note(string? path)
    {
        if (FileIndexWatch.Ignore(path, _watchRoots, _watchExclusions)) return;
        lock (_watchGate) _pending.Add(path!);
        Schedule(TimeSpan.FromMilliseconds(FileIndexWatch.DebounceMilliseconds));
    }

    void OnWatcherError(object? sender, ErrorEventArgs e)
    {
        var error = e.GetException();
        if (!FileIndexWatch.IsOverflow(error))
        {
            if (error is not null)
                AppLog.Info("file index watcher error: " + error.GetType().Name);
            return;
        }
        var now = DateTimeOffset.UtcNow;
        lock (_watchGate)
        {
            if (now < _overflowLoggedUntil) return;
            _overflowLoggedUntil = now + FileIndexWatch.OverflowBackoff;
            _needFull = true;
            _fullNotBefore = now + FileIndexWatch.OverflowBackoff;
        }
        AppLog.Info("file index watcher overflow, full rescan in 5 min");
        Schedule(FileIndexWatch.OverflowBackoff);
    }

    void StopWatchers()
    {
        lock (_watchGate)
        {
            foreach (var watcher in _watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Changed -= OnWatcher;
                    watcher.Created -= OnWatcher;
                    watcher.Deleted -= OnWatcher;
                    watcher.Renamed -= OnRenamed;
                    watcher.Error -= OnWatcherError;
                    watcher.Dispose();
                }
                catch
                {
                    // The watcher is going away with the service.
                }
            }
            _watchers.Clear();
        }
    }

    static IReadOnlyList<string> DiscoverRoots(AppSettings settings)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var known = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            string.IsNullOrEmpty(profile) ? "" : Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };
        var oneDrive = new List<string>();
        foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) oneDrive.Add(value);
        }
        if (!string.IsNullOrEmpty(profile))
            oneDrive.Add(Path.Combine(profile, "OneDrive"));
        var startMenu = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu")
        };
        return FileIndexRoots.Collect(
            profile,
            known,
            oneDrive,
            startMenu,
            settings.BuiltinIndexFolders,
            path => ParentWouldSkip(path, settings.EverythingExcludedPaths));
    }

    static bool ParentWouldSkip(string path, IReadOnlyList<string>? userExclusions)
    {
        var name = Path.GetFileName(path.TrimEnd('\\'));
        var hidden = false;
        var system = false;
        var reparse = false;
        try
        {
            if (Directory.Exists(path))
            {
                var attributes = File.GetAttributes(path);
                hidden = (attributes & FileAttributes.Hidden) != 0;
                system = (attributes & FileAttributes.System) != 0;
                reparse = (attributes & FileAttributes.ReparsePoint) != 0;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
        return FileIndexRules.SkipDirectory(new FileNode(name, true, hidden, system, reparse), path, userExclusions)
            || FileIndexWatch.IsAltPaneStore(path);
    }

    static void TryBackground()
    {
        try
        {
            SetThreadPriority(GetCurrentThread(), 0x00010000);
        }
        catch
        {
            // Lowest managed priority is already set. Background mode is optional.
        }
    }

    [DllImport("kernel32.dll")]
    static extern nint GetCurrentThread();

    [DllImport("kernel32.dll")]
    static extern bool SetThreadPriority(nint thread, int priority);
}
