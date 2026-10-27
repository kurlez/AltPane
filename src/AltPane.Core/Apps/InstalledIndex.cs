namespace AltPane.Core.Apps;

/// <summary>One child of a directory the indexer is allowed to look at.</summary>
public readonly record struct AppDirEntry(string FullPath, string Name, bool IsDirectory, bool ReparsePoint, bool System);

/// <summary>
/// Directory listing used by the app index. Production uses the real disk.
/// Tests supply a tree that can deny access or contain junctions.
/// </summary>
public interface IAppDirectory
{
    IReadOnlyList<AppDirEntry> List(string directory);
    byte[] ReadAllBytes(string file);
}

/// <summary>
/// Lists one directory at a time. Reparse points and system directories are not opened,
/// and a directory that refuses access is skipped instead of aborting the scan.
/// </summary>
public sealed class PhysicalAppDirectory : IAppDirectory
{
    static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        ReturnSpecialDirectories = false
    };

    public IReadOnlyList<AppDirEntry> List(string directory)
    {
        var list = new List<AppDirEntry>();
        foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", Options))
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) continue;
            var directoryEntry = (attributes & FileAttributes.Directory) != 0;
            var reparse = (attributes & FileAttributes.ReparsePoint) != 0 || IsLink(path);
            list.Add(new AppDirEntry(
                path,
                name,
                directoryEntry,
                reparse,
                (attributes & FileAttributes.System) != 0));
        }
        return list;
    }

    public byte[] ReadAllBytes(string file) => File.ReadAllBytes(file);

    static bool IsLink(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: false) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

public sealed class InstalledIndexRequest
{
    public bool StartMenu { get; init; }
    public bool Desktop { get; init; }
    public bool Path { get; init; }
    public bool Store { get; init; }
    public IReadOnlyList<string> StartMenuRoots { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DesktopRoots { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PathDirectories { get; init; } = Array.Empty<string>();
    public Func<IReadOnlyList<AppEntry>>? StoreApps { get; init; }
    public Action<string, Exception>? OnSourceFailed { get; init; }
}

public readonly record struct IndexScanResult(
    IReadOnlyList<AppEntry> Apps,
    int SkippedFolders,
    int StartMenuCount,
    int DesktopCount,
    int PathCount,
    int StoreCount);

/// <summary>
/// Scans Start Menu, desktop, PATH, and Store independently. One source throwing
/// does not discard what the others already found.
/// <see cref="PhysicalAppDirectory"/> is the directory listing FullScan uses:
/// inaccessible folders and reparse points (including the Start Menu junction 程序)
/// are skipped here, not in a separate helper.
/// </summary>
public static class InstalledIndex
{
    public static IndexScanResult Scan(InstalledIndexRequest request, IAppDirectory files)
    {
        var all = new List<AppEntry>();
        var skipped = 0;
        var startMenu = 0;
        var desktop = 0;
        var path = 0;
        var store = 0;

        void Take(bool enabled, string name, Func<ShortcutBatch> collect, Action<int> add)
        {
            if (!enabled) return;
            try
            {
                var batch = collect();
                all.AddRange(batch.Apps);
                skipped += batch.SkippedFolders;
                add(batch.Apps.Count);
            }
            catch (Exception ex)
            {
                request.OnSourceFailed?.Invoke(name, ex);
            }
        }

        Take(request.StartMenu, "Start Menu", () =>
            ShortcutWalk.Collect(request.StartMenuRoots, AppSource.StartMenu, files, recursive: true, extension: ".lnk"),
            count => startMenu += count);
        Take(request.Desktop, "Desktop", () =>
            ShortcutWalk.Collect(request.DesktopRoots, AppSource.Desktop, files, recursive: true, extension: ".lnk"),
            count => desktop += count);
        Take(request.Path, "PATH", () =>
            ShortcutWalk.Collect(request.PathDirectories, AppSource.Path, files, recursive: false, extension: ".exe"),
            count => path += count);
        Take(request.Store, "Store", () =>
        {
            var apps = request.StoreApps?.Invoke() ?? Array.Empty<AppEntry>();
            return new ShortcutBatch(apps, 0);
        }, count => store += count);

        return new IndexScanResult(AppCatalog.Merge(all), skipped, startMenu, desktop, path, store);
    }
}

public readonly record struct ShortcutBatch(IReadOnlyList<AppEntry> Apps, int SkippedFolders);

public static class ShortcutWalk
{
    public static ShortcutBatch Collect(
        IEnumerable<string> roots,
        AppSource source,
        IAppDirectory files,
        bool recursive,
        string extension)
    {
        var found = new List<AppEntry>();
        var skipped = 0;
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            Walk(root, source, files, recursive, extension, found, ref skipped);
        }
        return new ShortcutBatch(found, skipped);
    }

    static void Walk(
        string directory,
        AppSource source,
        IAppDirectory files,
        bool recursive,
        string extension,
        List<AppEntry> found,
        ref int skipped)
    {
        IReadOnlyList<AppDirEntry> children;
        try
        {
            children = files.List(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            skipped++;
            return;
        }

        foreach (var child in children)
        {
            try
            {
                if (child.IsDirectory)
                {
                    if (!recursive || child.ReparsePoint || child.System) continue;
                    Walk(child.FullPath, source, files, recursive, extension, found, ref skipped);
                    continue;
                }

                if (!child.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
                var bytes = files.ReadAllBytes(child.FullPath);
                if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ShellLinkReader.TryRead(bytes, out var link)) continue;
                    var entry = AppCatalog.FromShortcut(child.FullPath, link, source);
                    if (entry is not null) found.Add(entry);
                    continue;
                }

                if (AppCatalog.IsNoisyPathExecutable(child.FullPath)) continue;
                var name = Path.GetFileNameWithoutExtension(child.FullPath);
                var folder = Path.GetDirectoryName(child.FullPath) ?? "";
                found.Add(AppCatalog.Create(name, child.FullPath, AppKind.Executable, source, child.FullPath, "", folder, child.FullPath, 0));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }
    }
}
