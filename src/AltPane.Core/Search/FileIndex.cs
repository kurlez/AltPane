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
