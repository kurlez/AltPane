using System.Text.Json;
using System.Text.Json.Serialization;
using AltPane.Core.Filtering;
using AltPane.Core.Input;
using AltPane.Core.Models;

namespace AltPane.Core.Settings;

public readonly record struct SettingsLoadResult(AppSettings Settings, bool RecoveredFromError, string? Error);

public static class SettingsStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string DefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "AltPane", "settings.json");
    }

    public static SettingsLoadResult Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new SettingsLoadResult(AppSettings.CreateDefault(), false, null);
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.CreateDefault();
            Normalize(settings);
            return new SettingsLoadResult(settings, false, null);
        }
        catch (Exception ex)
        {
            TryBackup(path);
            return new SettingsLoadResult(AppSettings.CreateDefault(), true, ex.Message);
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        Normalize(settings);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, Options);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public static void Normalize(AppSettings settings)
    {
        settings.Version = 1;
        settings.Hotkey ??= HotkeyChord.AltTab();
        if (!HotkeyLogic.IsValid(settings.Hotkey))
            settings.Hotkey = HotkeyChord.AltTab();
        if (!Enum.IsDefined(settings.Theme)) settings.Theme = AppTheme.System;
        if (!Enum.IsDefined(settings.Size)) settings.Size = SwitcherSize.Comfortable;
        if (!Enum.IsDefined(settings.Placement)) settings.Placement = SwitcherPlacement.MouseCursor;
        if (!Enum.IsDefined(settings.Language)) settings.Language = AppLanguage.System;
        if (!Enum.IsDefined(settings.SortMode)) settings.SortMode = LoadSortMode.Recent;
        settings.ExcludedProcesses = ProcessNames.NormalizeList(settings.ExcludedProcesses);
    }

    static void TryBackup(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Copy(path, path + ".bak", overwrite: true);
        }
        catch
        {
            // Best effort. Defaults still load.
        }
    }
}

public static class ExecutablePath
{
    public static string Quote(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (path.Contains('"')) return path;
        return path.Contains(' ') ? $"\"{path}\"" : path;
    }
}
