using System.Runtime.InteropServices;
using AltPane.Core.Models;
using AltPane.Core.Search;

namespace AltPane;

/// <summary>
/// Builds the built-in file index off the UI thread at low priority, keeps a
/// compact snapshot in memory, and refreshes it from filesystem watchers.
/// Search only reads the latest snapshot.
/// </summary>
internal sealed class FileIndexService : IDisposable
{
    readonly Func<AppSettings> _settings;
    readonly IFileTree _tree;
    readonly string _snapshotPath;
    readonly AutoResetEvent _wake = new(false);
    readonly object _watchGate = new();
    readonly List<FileSystemWatcher> _watchers = new();
    Timer? _debounce;
    Thread? _thread;
    int _generation;
    int _disposed;
    volatile FileIndex _current = FileIndex.Empty;

    public FileIndexService(Func<AppSettings> settings)
        : this(settings, new PhysicalFileTree(), FileIndexStore.DefaultPath())
    {
    }

    public FileIndexService(Func<AppSettings> settings, IFileTree tree, string snapshotPath)
    {
        _settings = settings;
        _tree = tree;
        _snapshotPath = snapshotPath;
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
        Interlocked.Increment(ref _generation);
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
                ScanIfEnabled();
            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
            }
            _wake.WaitOne();
        }
    }

    void ScanIfEnabled()
    {
        var settings = _settings();
        if (!settings.UseBuiltinFileIndex)
        {
            StopWatchers();
            return;
        }

        var generation = Volatile.Read(ref _generation);
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
        AppLog.Info(
            $"file index scan {clock.ElapsedMilliseconds} ms, entries {result.Index.Count}, memory {result.Index.EstimatedBytes} bytes, working set {ProcessFootprint.Megabytes()} MB");
        BindWatchers(roots, exclusions);
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
                if (ParentWouldSkip(child, exclusions)) continue;
                yield return (child, true);
            }
        }
    }

    void BindWatchers(IReadOnlyList<string> roots, IReadOnlyList<string>? exclusions)
    {
        StopWatchers();
        if (Volatile.Read(ref _disposed) != 0) return;
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
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                        InternalBufferSize = 64 * 1024,
                        EnableRaisingEvents = true
                    };
                    watcher.Changed += (_, e) => OnChange(e.FullPath, roots, exclusions);
                    watcher.Created += (_, e) => OnChange(e.FullPath, roots, exclusions);
                    watcher.Deleted += (_, e) => OnChange(e.FullPath, roots, exclusions);
                    watcher.Renamed += (_, e) => OnChange(e.FullPath, roots, exclusions);
                    watcher.Error += (_, e) => OnError(e.GetException());
                    _watchers.Add(watcher);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex);
                }
            }
        }
    }

    void OnChange(string? path, IReadOnlyList<string> roots, IReadOnlyList<string>? exclusions)
    {
        if (FileIndexWatch.Ignore(path, roots, exclusions)) return;
        _debounce?.Change(FileIndexWatch.DebounceMilliseconds, Timeout.Infinite);
    }

    void OnError(Exception? error)
    {
        if (FileIndexWatch.IsOverflow(error))
            AppLog.Info("file index watcher overflow, rescanning");
        else if (error is not null)
            AppLog.Info("file index watcher error: " + error.GetType().Name);
        _debounce?.Change(Timeout.Infinite, Timeout.Infinite);
        _wake.Set();
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
        return FileIndexRules.SkipDirectory(new FileNode(name, true, hidden, system, reparse), path, userExclusions);
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
