using System.Text.Json.Serialization;

namespace AltPane.Core.Apps;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppKind
{
    Shortcut,
    Executable,
    Store
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppSource
{
    StartMenu,
    Store,
    Desktop,
    Path,
    Installed,
    Everything
}

public sealed class AppEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string LaunchPath { get; set; } = "";
    public AppKind Kind { get; set; }
    public AppSource Source { get; set; }
    public string ResolvedPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string IconLocation { get; set; } = "";
    public int IconIndex { get; set; }
    public string ProcessName { get; set; } = "";
}

public sealed class LaunchMemory
{
    public int Count { get; set; }
    public long LastUtcTicks { get; set; }
}

/// <summary>A ranked app row. SwitchHandle is set when a window of this app is already open.</summary>
public readonly record struct AppHit(AppEntry App, int Quality, long SwitchHandle);
