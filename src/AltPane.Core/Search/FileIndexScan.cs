namespace AltPane.Core.Search;

public readonly record struct FileNode(string Name, bool Directory, bool Hidden, bool System, bool Reparse);

/// <summary>
/// Directory listing for the file index. Production uses the disk.
/// Tests supply a tree that can hide folders or deny access.
/// </summary>
public interface IFileTree
{
    bool Exists(string directory);
    IReadOnlyList<FileNode> List(string directory);
}

public sealed class PhysicalFileTree : IFileTree
{
    public bool Exists(string directory)
    {
        try
        {
            return Directory.Exists(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<FileNode> List(string directory)
    {
        var list = new List<FileNode>();
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
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
            list.Add(new FileNode(
                name,
                (attributes & FileAttributes.Directory) != 0,
                (attributes & FileAttributes.Hidden) != 0,
                (attributes & FileAttributes.System) != 0,
                (attributes & FileAttributes.ReparsePoint) != 0));
        }
        return list;
    }
}

public readonly record struct FileScanResult(FileIndex Index, int SkippedDirectories);

public static class FileIndexPaths
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var text = path.Trim().Trim('"').Replace('/', '\\');
        if (text.Length >= 2 && text[1] == ':' && text.Length <= 3)
            return char.ToUpperInvariant(text[0]) + ":\\";
        return text.TrimEnd('\\');
    }

    public static string Combine(string directory, string name)
    {
        if (directory.EndsWith('\\')) return directory + name;
        return directory + "\\" + name;
    }

    public static bool IsUnder(string? path, string? root)
    {
        var child = Normalize(path);
        var parent = Normalize(root);
        if (child.Length == 0 || parent.Length == 0) return false;
        if (child.Equals(parent, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = parent.EndsWith('\\') ? parent : parent + "\\";
        return child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUnderAny(string? path, IReadOnlyList<string>? roots)
    {
        if (roots is null || roots.Count == 0 || string.IsNullOrWhiteSpace(path)) return false;
        foreach (var root in roots)
        {
            if (IsUnder(path, root)) return true;
        }
        return false;
    }

    public static bool IsExcluded(string? path, IReadOnlyList<string>? userPaths)
    {
        if (FileSearch.IsExcluded(path)) return true;
        if (userPaths is null || string.IsNullOrWhiteSpace(path)) return false;
        foreach (var raw in userPaths)
        {
            if (IsUnder(path, raw)) return true;
        }
        return false;
    }
}

public static class FileIndexRules
{
    public const int MaxDepth = 32;
    public const int MaxEntries = 250_000;

    public static bool SkipDirectory(FileNode node, string fullPath, IReadOnlyList<string>? userExclusions)
    {
        if (string.IsNullOrEmpty(node.Name)) return true;
        if (node.Hidden || node.System || node.Reparse) return true;
        if (node.Name.Equals("AppData", StringComparison.OrdinalIgnoreCase)) return true;
        if (FileIndexPaths.IsExcluded(fullPath, userExclusions)) return true;
        return false;
    }

    public static bool SkipFile(FileNode node)
    {
        if (node.Directory || string.IsNullOrEmpty(node.Name)) return true;
        if (node.System) return true;
        if (node.Name.StartsWith("~$", StringComparison.Ordinal)) return true;
        return false;
    }
}

public static class FileIndexRoots
{
    public static IReadOnlyList<string> Collect(
        string? userProfile,
        IEnumerable<string>? knownFolders,
        IEnumerable<string>? oneDrive,
        IEnumerable<string>? startMenu,
        IEnumerable<string>? extra,
        Func<string, bool>? parentWouldSkip)
    {
        var all = new List<string>();
        void Add(string? path)
        {
            var normal = FileIndexPaths.Normalize(path);
            if (normal.Length > 0) all.Add(normal);
        }

        Add(userProfile);
        if (knownFolders is not null)
        {
            foreach (var path in knownFolders) Add(path);
        }
        if (oneDrive is not null)
        {
            foreach (var path in oneDrive) Add(path);
        }
        if (startMenu is not null)
        {
            foreach (var path in startMenu) Add(path);
        }
        if (extra is not null)
        {
            foreach (var path in extra) Add(path);
        }
        return Dedupe(all, parentWouldSkip);
    }

    /// <summary>
    /// Drops a root that a broader root will already walk. A nested root is kept
    /// when an ancestor would be skipped (AppData, a hidden folder, an exclusion),
    /// because the profile scan will not reach it.
    /// </summary>
    public static IReadOnlyList<string> Dedupe(IReadOnlyList<string> roots, Func<string, bool>? parentWouldSkip)
    {
        var unique = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (root.Length == 0 || !seen.Add(root)) continue;
            unique.Add(root);
        }
        unique.Sort(static (a, b) => a.Length.CompareTo(b.Length));
        var kept = new List<string>();
        foreach (var path in unique)
        {
            var covered = false;
            foreach (var parent in kept)
            {
                if (CoveredBy(parent, path, parentWouldSkip))
                {
                    covered = true;
                    break;
                }
            }
            if (!covered) kept.Add(path);
        }
        return kept;
    }

    public static bool CoveredBy(string parent, string path, Func<string, bool>? parentWouldSkip)
    {
        if (!FileIndexPaths.IsUnder(path, parent) || path.Equals(parent, StringComparison.OrdinalIgnoreCase))
            return false;
        var basePath = parent.TrimEnd('\\');
        var rest = path[basePath.Length..].Trim('\\');
        if (rest.Length == 0) return false;
        var parts = rest.Split('\\');
        var acc = basePath;
        for (var i = 0; i < parts.Length; i++)
        {
            acc = acc + "\\" + parts[i];
            if (parentWouldSkip is not null && parentWouldSkip(acc))
                return false;
        }
        return true;
    }
}

public static class FileIndexScan
{
    public static FileScanResult Run(IEnumerable<string> roots, IFileTree tree, IReadOnlyList<string>? userExclusions)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var directories = new List<string>();
        var directoryIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileSlot>();
        var walked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;

        foreach (var raw in roots)
        {
            var root = FileIndexPaths.Normalize(raw);
            if (root.Length == 0) continue;
            try
            {
                if (!tree.Exists(root)) continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                continue;
            }
            Walk(root, 0);
            if (files.Count >= FileIndexRules.MaxEntries) break;
        }

        var directoryArray = directories.ToArray();
        var fileArray = files.ToArray();
        directories.Clear();
        files.Clear();
        directoryIds.Clear();
        walked.Clear();
        var index = new FileIndex(directoryArray, fileArray, FileIndex.Estimate(directoryArray, fileArray));
        return new FileScanResult(index, skipped);

        void Walk(string directory, int depth)
        {
            if (depth > FileIndexRules.MaxDepth || files.Count >= FileIndexRules.MaxEntries) return;
            if (!walked.Add(directory)) return;
            IReadOnlyList<FileNode> children;
            try
            {
                children = tree.List(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                skipped++;
                return;
            }

            foreach (var child in children)
            {
                if (files.Count >= FileIndexRules.MaxEntries) return;
                var full = FileIndexPaths.Combine(directory, child.Name);
                if (child.Directory)
                {
                    if (FileIndexRules.SkipDirectory(child, full, userExclusions) || FileIndexWatch.IsAltPaneStore(full))
                    {
                        skipped++;
                        continue;
                    }
                    Walk(full, depth + 1);
                    continue;
                }
                if (FileIndexRules.SkipFile(child)) continue;
                if (FileExcluded(directory, child.Name, userExclusions)) continue;
                if (!directoryIds.TryGetValue(directory, out var directoryId))
                {
                    directoryId = directories.Count;
                    directories.Add(directory);
                    directoryIds.Add(directory, directoryId);
                }
                files.Add(new FileSlot(directoryId, child.Name));
            }
        }
    }

    static bool FileExcluded(string directory, string name, IReadOnlyList<string>? userExclusions)
    {
        if (FileSearch.IsExcluded(name) || FileSearch.IsExcluded(directory)) return true;
        if (FileIndexWatch.IsAltPaneStore(directory)) return true;
        if (userExclusions is null || userExclusions.Count == 0) return false;
        foreach (var root in userExclusions)
        {
            if (FileIndexPaths.IsUnder(directory, root)) return true;
        }
        return FileIndexPaths.IsExcluded(FileIndexPaths.Combine(directory, name), userExclusions);
    }
}

/// <summary>
/// Watcher decisions that do not touch the operating system. A normal change is
/// applied after the debounce. A buffer overflow waits out a backoff before a
/// full rescan, and AltPane's own files never count as a change.
/// </summary>
public static class FileIndexWatch
{
    public const int DebounceMilliseconds = 400;
    public static readonly TimeSpan OverflowBackoff = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ReachableRecheck = TimeSpan.FromMinutes(1);

    public static bool IsOverflow(Exception? error) => error is InternalBufferOverflowException;

    /// <summary>
    /// The background walk runs only while the built-in index is on and Everything
    /// is not already answering. A loaded snapshot is kept either way.
    /// </summary>
    public static bool ShouldBackgroundScan(bool indexEnabled, bool everythingReachable) =>
        indexEnabled && !everythingReachable;

    /// <summary>AltPane's own snapshot and log must not wake the watcher.</summary>
    public static bool IsAltPaneStore(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var text = path.Replace('/', '\\');
        return text.Contains(@"\AppData\Local\AltPane\", StringComparison.OrdinalIgnoreCase)
            || text.Contains(@"\AppData\Roaming\AltPane\", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith(@"\AppData\Local\AltPane", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith(@"\AppData\Roaming\AltPane", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsUnderAppData(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var parts = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Equals("AppData", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static bool Ignore(string? fullPath, IReadOnlyList<string>? watchedRoots, IReadOnlyList<string>? userExclusions)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return true;
        if (IsAltPaneStore(fullPath)) return true;
        if (FileIndexPaths.IsExcluded(fullPath, userExclusions)) return true;
        if (!IsUnderAppData(fullPath)) return false;
        if (watchedRoots is null) return true;
        foreach (var root in watchedRoots)
        {
            if (!IsUnderAppData(root)) continue;
            if (FileIndexPaths.IsUnder(fullPath, root)) return false;
        }
        return true;
    }
}
