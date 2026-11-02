using System.Text.Json;

namespace AltPane.Core.Improvements;

public sealed class ImprovementEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public string Request { get; set; } = "";
    /// <summary>done, failed, cancelled or running.</summary>
    public string Status { get; set; } = "running";
    public string Summary { get; set; } = "";
}

/// <summary>Plain JSON list of past requests and what the agent reported, newest last.</summary>
public static class ImprovementHistory
{
    public const int Capacity = 100;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AltPane", "improvements.json");

    public static List<ImprovementEntry> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new List<ImprovementEntry>();
            var items = JsonSerializer.Deserialize<List<ImprovementEntry>>(File.ReadAllText(path), Options);
            return Trim(items ?? new List<ImprovementEntry>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new List<ImprovementEntry>();
        }
    }

    public static void Save(string path, List<ImprovementEntry> entries)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Trim(entries), Options));
        File.Move(temporary, path, overwrite: true);
    }

    public static List<ImprovementEntry> Trim(List<ImprovementEntry> entries) =>
        entries.Count <= Capacity ? entries : entries.GetRange(entries.Count - Capacity, Capacity);
}
