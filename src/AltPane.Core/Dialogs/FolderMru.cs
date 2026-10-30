using System.Text.Json;

namespace AltPane.Core.Dialogs;

/// <summary>Folders AltPane jumped to or saw a dialog leave open. Newest first, de-duplicated.</summary>
public sealed class FolderMru
{
    public const int DefaultCapacity = 16;
    readonly List<string> _items = new();

    public FolderMru(int capacity = DefaultCapacity)
    {
        Capacity = Math.Max(1, capacity);
    }

    public int Capacity { get; }
    public IReadOnlyList<string> Entries => _items;

    public void Remember(string? path)
    {
        var normal = FolderPath.Normalize(path);
        if (!FolderPath.IsAbsolute(normal)) return;
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (FolderPath.Same(_items[i], normal))
                _items.RemoveAt(i);
        }
        _items.Insert(0, normal);
        if (_items.Count > Capacity)
            _items.RemoveRange(Capacity, _items.Count - Capacity);
    }

    public void Replace(IEnumerable<string>? paths)
    {
        _items.Clear();
        if (paths is null) return;
        var list = paths as IReadOnlyList<string> ?? paths.ToList();
        for (var i = list.Count - 1; i >= 0; i--)
            Remember(list[i]);
    }
}

public static class FolderMruStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "AltPane", "folder-mru.json");
    }

    public static FolderMru Load(string path, int capacity = FolderMru.DefaultCapacity)
    {
        var mru = new FolderMru(capacity);
        try
        {
            if (!File.Exists(path)) return mru;
            var entries = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path), Options);
            mru.Replace(entries);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new FolderMru(capacity);
        }
        return mru;
    }

    public static void Save(string path, FolderMru mru)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(mru.Entries, Options);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }
}
