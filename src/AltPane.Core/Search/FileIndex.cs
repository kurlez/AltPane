using System.Text;

namespace AltPane.Core.Search;

/// <summary>
/// One indexed file. The directory string lives in <see cref="FileIndex"/> once;
/// the slot keeps only the parent id and the file name.
/// </summary>
public readonly struct FileSlot
{
    public FileSlot(int directoryId, string name)
    {
        DirectoryId = directoryId;
        Name = name;
    }

    public int DirectoryId { get; }
    public string Name { get; }
}

/// <summary>
/// Compact in-memory file list. Directory paths are stored once. Each file is a
/// name plus an index into that table, so a folder of many files does not repeat
/// its full path on every entry.
/// </summary>
public sealed class FileIndex
{
    public static FileIndex Empty { get; } = new(Array.Empty<string>(), Array.Empty<FileSlot>(), 0);

    readonly string[] _directories;

    public FileIndex(string[] directories, FileSlot[] files, long estimatedBytes)
    {
        _directories = directories;
        Files = files;
        EstimatedBytes = estimatedBytes;
    }

    public IReadOnlyList<FileSlot> Files { get; }
    public int Count => Files.Count;
    public int DirectoryCount => _directories.Length;
    public long EstimatedBytes { get; }

    public string Directory(int directoryId) =>
        (uint)directoryId < (uint)_directories.Length ? _directories[directoryId] : "";

    public string FullPath(FileSlot slot)
    {
        var directory = Directory(slot.DirectoryId);
        if (directory.Length == 0) return slot.Name;
        return directory.EndsWith('\\') ? directory + slot.Name : directory + "\\" + slot.Name;
    }

    public static long Estimate(IReadOnlyList<string> directories, IReadOnlyList<FileSlot> files)
    {
        long bytes = 64;
        foreach (var directory in directories)
            bytes += 24L + directory.Length * 2L;
        foreach (var file in files)
            bytes += 16L + file.Name.Length * 2L;
        return bytes;
    }
}

/// <summary>Binary snapshot under %LOCALAPPDATA%\AltPane. A bad file loads as empty.</summary>
public static class FileIndexStore
{
    public const string FileName = "file-index.bin";
    static readonly byte[] Magic = { (byte)'A', (byte)'L', (byte)'T', (byte)'F' };

    public static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root))
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
        return Path.Combine(root, "AltPane", FileName);
    }

    public static void Save(string path, FileIndex index)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
        {
            writer.Write(Magic);
            writer.Write((byte)1);
            writer.Write(index.DirectoryCount);
            for (var i = 0; i < index.DirectoryCount; i++)
                writer.Write(index.Directory(i));
            writer.Write(index.Count);
            foreach (var file in index.Files)
            {
                writer.Write(file.DirectoryId);
                writer.Write(file.Name);
            }
        }
        File.Move(temporary, path, overwrite: true);
    }

    public static FileIndex Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return FileIndex.Empty;
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            var magic = reader.ReadBytes(4);
            if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
                return FileIndex.Empty;
            if (reader.ReadByte() != 1) return FileIndex.Empty;
            var directoryCount = reader.ReadInt32();
            if (directoryCount < 0 || directoryCount > 1_000_000) return FileIndex.Empty;
            var directories = new string[directoryCount];
            for (var i = 0; i < directoryCount; i++)
                directories[i] = reader.ReadString();
            var fileCount = reader.ReadInt32();
            if (fileCount < 0 || fileCount > 2_000_000) return FileIndex.Empty;
            var files = new FileSlot[fileCount];
            for (var i = 0; i < fileCount; i++)
            {
                var directoryId = reader.ReadInt32();
                var name = reader.ReadString();
                if ((uint)directoryId >= (uint)directoryCount || string.IsNullOrEmpty(name))
                    return FileIndex.Empty;
                files[i] = new FileSlot(directoryId, name);
            }
            return new FileIndex(directories, files, FileIndex.Estimate(directories, files));
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or UnauthorizedAccessException)
        {
            return FileIndex.Empty;
        }
    }
}

public static class FileIndexQuery
{
    public static IReadOnlyList<FileHit> Search(
        FileIndex index,
        string? text,
        IReadOnlyList<string>? extensions,
        IReadOnlyList<string>? excludedPaths,
        int limit = FileSearch.MaxResults)
    {
        if (index.Count == 0 || limit <= 0 || string.IsNullOrWhiteSpace(text))
            return Array.Empty<FileHit>();
        var terms = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return Array.Empty<FileHit>();

        var ranked = new List<(int Score, FileHit Hit)>();
        foreach (var slot in index.Files)
        {
            if (extensions is not null && !HasExtension(slot.Name, extensions)) continue;
            var score = ScoreName(slot.Name, terms);
            if (score < 0) continue;
            var full = index.FullPath(slot);
            if (FileIndexPaths.IsExcluded(full, excludedPaths)) continue;
            var directory = index.Directory(slot.DirectoryId);
            ranked.Add((score, new FileHit(full, slot.Name, directory)));
        }

        ranked.Sort(static (a, b) =>
        {
            var byScore = a.Score.CompareTo(b.Score);
            return byScore != 0 ? byScore : string.Compare(a.Hit.Name, b.Hit.Name, StringComparison.OrdinalIgnoreCase);
        });
        if (ranked.Count > limit) ranked.RemoveRange(limit, ranked.Count - limit);
        var hits = new List<FileHit>(ranked.Count);
        foreach (var item in ranked) hits.Add(item.Hit);
        return hits;
    }

    /// <summary>
    /// Applies the same <c>f </c> / <c>file:</c> plan as Everything: documents by default,
    /// every type after the prefix. Terms are AND. Matching is a case-insensitive
    /// substring, or pinyin initials when the name contains Han characters.
    /// </summary>
    public static IReadOnlyList<FileHit> SearchQuery(
        FileIndex index,
        string? raw,
        string? documentExtensions,
        IReadOnlyList<string>? excludedPaths,
        int limit = FileSearch.MaxResults)
    {
        if (!FileSearch.TryPlan(raw, documentExtensions, excludedPaths, out var plan))
            return Array.Empty<FileHit>();
        var extensions = plan.AllTypes ? null : FileSearch.Split(documentExtensions);
        return Search(index, plan.Text, extensions, excludedPaths, limit);
    }

    public static bool MatchesName(string name, string term)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(term)) return false;
        if (name.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (term.Length < 2 || !HasIdeograph(name)) return false;
        if (Pinyin.InitialsHit(name, term)) return true;
        var initials = Pinyin.PrimaryInitials(name);
        if (initials.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        return Pinyin.SpellHit(name, term);
    }

    static int ScoreName(string name, string[] terms)
    {
        var worst = 0;
        foreach (var term in terms)
        {
            var score = ScoreTerm(name, term);
            if (score < 0) return -1;
            if (score > worst) worst = score;
        }
        return worst;
    }

    static int ScoreTerm(string name, string term)
    {
        if (name.Equals(term, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains(term, StringComparison.OrdinalIgnoreCase)) return 2;
        if (term.Length >= 2 && HasIdeograph(name))
        {
            if (Pinyin.InitialsHit(name, term)) return 3;
            var initials = Pinyin.PrimaryInitials(name);
            if (initials.Contains(term, StringComparison.OrdinalIgnoreCase)) return 3;
            if (Pinyin.SpellHit(name, term)) return 4;
        }
        return -1;
    }

    static bool HasExtension(string name, IReadOnlyList<string> extensions)
    {
        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot >= name.Length - 1) return false;
        var ext = name.AsSpan(dot + 1);
        foreach (var wanted in extensions)
        {
            if (ext.Equals(wanted, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static bool HasIdeograph(string name)
    {
        foreach (var ch in name)
        {
            if (ch is >= (char)0x4E00 and <= (char)0x9FFF) return true;
        }
        return false;
    }
}

/// <summary>
/// One-file updates for the watcher. A normal create, delete, or rename does not
/// walk the profile again.
/// </summary>
public static class FileIndexEdit
{
    public static FileIndex Upsert(FileIndex index, string? fullPath)
    {
        var normal = FileIndexPaths.Normalize(fullPath);
        var (directory, name) = Split(normal);
        if (name.Length == 0 || FileIndexPaths.IsExcluded(normal, null) || FileIndexWatch.IsAltPaneStore(normal))
            return index;
        if (Contains(index, directory, name)) return index;
        var directories = new List<string>(CopyDirectories(index));
        var id = DirectoryId(directories, directory);
        var files = new FileSlot[index.Count + 1];
        for (var i = 0; i < index.Count; i++) files[i] = index.Files[i];
        files[^1] = new FileSlot(id, name);
        return Finish(directories, files);
    }

    public static FileIndex Remove(FileIndex index, string? fullPath)
    {
        var normal = FileIndexPaths.Normalize(fullPath);
        if (normal.Length == 0 || index.Count == 0) return index;
        var keep = new List<FileSlot>(index.Count);
        var removed = false;
        foreach (var slot in index.Files)
        {
            var full = index.FullPath(slot);
            if (full.Equals(normal, StringComparison.OrdinalIgnoreCase) || FileIndexPaths.IsUnder(full, normal))
            {
                removed = true;
                continue;
            }
            keep.Add(slot);
        }
        return removed ? Rebuild(index, keep) : index;
    }

    static FileIndex Rebuild(List<string> directories, List<FileSlot> keep) =>
        Finish(directories, keep.ToArray());

    public static FileIndex Absorb(FileIndex into, FileIndex extra)
    {
        if (extra.Count == 0) return into;
        var directories = new List<string>(CopyDirectories(into));
        var files = new List<FileSlot>(into.Count + extra.Count);
        foreach (var slot in into.Files) files.Add(slot);
        var map = new Dictionary<int, int>();
        foreach (var slot in extra.Files)
        {
            if (!map.TryGetValue(slot.DirectoryId, out var id))
            {
                id = DirectoryId(directories, extra.Directory(slot.DirectoryId));
                map[slot.DirectoryId] = id;
            }
            if (Listed(files, directories, id, slot.Name)) continue;
            files.Add(new FileSlot(id, slot.Name));
        }
        return Finish(directories, files.ToArray());
    }

    public static FileIndex ReplaceChildren(FileIndex index, string? directory, IReadOnlyList<string> names)
    {
        var normal = FileIndexPaths.Normalize(directory);
        if (normal.Length == 0) return index;
        var keep = new List<FileSlot>(index.Count);
        foreach (var slot in index.Files)
        {
            if (index.Directory(slot.DirectoryId).Equals(normal, StringComparison.OrdinalIgnoreCase)) continue;
            keep.Add(slot);
        }
        var directories = new List<string>(CopyDirectories(index));
        var id = -1;
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var full = FileIndexPaths.Combine(normal, name);
            if (FileIndexPaths.IsExcluded(full, null) || FileIndexWatch.IsAltPaneStore(full)) continue;
            if (id < 0) id = DirectoryId(directories, normal);
            keep.Add(new FileSlot(id, name));
        }
        return Rebuild(directories, keep);
    }

    static bool Listed(List<FileSlot> files, List<string> directories, int directoryId, string name)
    {
        var directory = directories[directoryId];
        foreach (var slot in files)
        {
            if (!slot.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if ((uint)slot.DirectoryId >= (uint)directories.Count) continue;
            if (directories[slot.DirectoryId].Equals(directory, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static bool Contains(FileIndex index, string directory, string name)
    {
        for (var i = 0; i < index.Count; i++)
        {
            var slot = index.Files[i];
            if (!slot.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (index.Directory(slot.DirectoryId).Equals(directory, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static (string Directory, string Name) Split(string normal)
    {
        if (normal.Length == 0) return ("", "");
        var cut = normal.LastIndexOf('\\');
        if (cut <= 0) return ("", normal);
        return (normal[..cut], normal[(cut + 1)..]);
    }

    static string[] CopyDirectories(FileIndex index)
    {
        var directories = new string[index.DirectoryCount];
        for (var i = 0; i < directories.Length; i++) directories[i] = index.Directory(i);
        return directories;
    }

    static int DirectoryId(List<string> directories, string directory)
    {
        for (var i = 0; i < directories.Count; i++)
        {
            if (directories[i].Equals(directory, StringComparison.OrdinalIgnoreCase)) return i;
        }
        directories.Add(directory);
        return directories.Count - 1;
    }

    static FileIndex Rebuild(FileIndex index, List<FileSlot> keep)
    {
        var used = new bool[index.DirectoryCount];
        foreach (var slot in keep)
        {
            if ((uint)slot.DirectoryId < (uint)used.Length) used[slot.DirectoryId] = true;
        }
        var map = new int[used.Length];
        var directories = new List<string>();
        for (var i = 0; i < used.Length; i++)
        {
            if (!used[i])
            {
                map[i] = -1;
                continue;
            }
            map[i] = directories.Count;
            directories.Add(index.Directory(i));
        }
        var files = new FileSlot[keep.Count];
        for (var i = 0; i < keep.Count; i++)
            files[i] = new FileSlot(map[keep[i].DirectoryId], keep[i].Name);
        return Finish(directories.ToArray(), files);
    }

    static FileIndex Finish(IReadOnlyList<string> directories, FileSlot[] files)
    {
        var copy = new string[directories.Count];
        for (var i = 0; i < copy.Length; i++) copy[i] = directories[i];
        return new FileIndex(copy, files, FileIndex.Estimate(copy, files));
    }
}
