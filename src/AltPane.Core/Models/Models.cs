using System.Text.Json.Serialization;

namespace AltPane.Core.Models;

[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppTheme
{
    System,
    Light,
    Dark
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SwitcherSize
{
    Compact,
    Comfortable,
    Large
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SwitcherPlacement
{
    MouseCursor,
    ActiveWindow
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppLanguage
{
    System,
    ZhCn,
    En
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LoadSortMode
{
    Recent,
    Cpu,
    Memory
}

/// <summary>
/// A window the filter can judge without touching Win32.
/// VirtualDesktopSame: 1 on the current desktop, 0 on another, -1 unknown.
/// CloakFlags: raw DWMWA_CLOAKED bits (1 = app, 2 = shell).
/// RootOwnerHandle equals Handle when the window owns itself.
/// </summary>
public sealed record WindowCandidate
{
    public long Handle { get; init; }
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string? HostedProcessName { get; init; }
    public int ProcessId { get; init; }
    public long OwnerHandle { get; init; }
    public long RootOwnerHandle { get; init; }
    public long ExStyle { get; init; }
    public bool IsVisible { get; init; }
    public int CloakFlags { get; init; }
    public int VirtualDesktopSame { get; init; } = -1;
}

public sealed class WindowEntry
{
    public required long Handle { get; init; }
    public required string Title { get; init; }
    public required string ClassName { get; init; }
    public required string ProcessName { get; init; }
    public string? HostedProcessName { get; init; }
    public required string DisplayProcessName { get; init; }
    public required int ProcessId { get; init; }
    public int HostedProcessId { get; init; }
    public required int KillProcessId { get; init; }
    public required string KillProcessName { get; init; }
    public string IconPath { get; init; } = "";
    public bool IsIconic { get; set; }
    public bool IsZoomed { get; set; }
    public int OriginalIndex { get; init; }
}

public sealed class HotkeyChord
{
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.Alt;
    public int VirtualKey { get; set; } = 0x09;

    public static HotkeyChord AltTab() => new()
    {
        Modifiers = ModifierKeys.Alt,
        VirtualKey = 0x09
    };
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public HotkeyChord? Hotkey { get; set; } = HotkeyChord.AltTab();
    public bool CaptureWinTab { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;
    public SwitcherSize Size { get; set; } = SwitcherSize.Comfortable;
    public bool ShowPreview { get; set; } = true;
    public SwitcherPlacement Placement { get; set; } = SwitcherPlacement.MouseCursor;
    public AppLanguage Language { get; set; } = AppLanguage.System;
    public List<string> ExcludedProcesses { get; set; } = new();
    public bool StartWithWindows { get; set; }
    public bool OverridePaused { get; set; }
    public bool IncludeOtherVirtualDesktops { get; set; }
    public bool WelcomeShown { get; set; }
    public bool ShowLoad { get; set; } = true;
    public LoadSortMode SortMode { get; set; } = LoadSortMode.Recent;
    public bool ConfirmEndTask { get; set; } = true;
    public bool EndTaskKillsTree { get; set; } = true;
    public bool PreviewMinimized { get; set; }
    /// <summary>
    /// When true (the default), releasing the trigger key leaves the switcher open.
    /// Enter or a click activates the selection. When false, releasing the trigger activates it.
    /// </summary>
    public bool StickySwitcher { get; set; } = true;
    /// <summary>
    /// When sticky mode is on, a tap released before the switcher appears still leaves it open.
    /// Turn this on to activate that tap immediately. Ignored when <see cref="StickySwitcher"/> is false.
    /// </summary>
    public bool QuickTapSwitches { get; set; }
    /// <summary>When true (the default), a search with no matching window also offers installed apps.</summary>
    public bool LaunchApps { get; set; } = true;
    public bool IndexStartMenu { get; set; } = true;
    public bool IndexStoreApps { get; set; } = true;
    public bool IndexDesktop { get; set; } = true;
    /// <summary>Executables on PATH. Off by default because system directories are noisy.</summary>
    public bool IndexPath { get; set; }
    public List<string> ExcludedApps { get; set; } = new();

    public static AppSettings CreateDefault() => new()
    {
        Version = 1,
        Hotkey = HotkeyChord.AltTab(),
        ShowPreview = true,
        Size = SwitcherSize.Comfortable,
        Placement = SwitcherPlacement.MouseCursor,
        Theme = AppTheme.System,
        Language = AppLanguage.System,
        ExcludedProcesses = new List<string>(),
        ExcludedApps = new List<string>(),
        ShowLoad = true,
        SortMode = LoadSortMode.Recent,
        ConfirmEndTask = true,
        EndTaskKillsTree = true,
        StickySwitcher = true,
        LaunchApps = true,
        IndexStartMenu = true,
        IndexStoreApps = true,
        IndexDesktop = true
    };
}
