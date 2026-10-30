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
    Memory,
    Initial,
    /// <summary>
    /// Main list by decayed focus time, then most recent user activation.
    /// Apps under the threshold, and demoted apps, stay last.
    /// <see cref="StableGrouped"/> is the same value so a schema 6 file that
    /// stored that name still loads.
    /// </summary>
    FocusTime,
    StableGrouped = FocusTime
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
    public required string DisplayProcessName { get; set; }
    public required int ProcessId { get; init; }
    public int HostedProcessId { get; init; }
    public required int KillProcessId { get; init; }
    public required string KillProcessName { get; init; }
    public string IconPath { get; init; } = "";
    public bool IsIconic { get; set; }
    public bool IsZoomed { get; set; }
    public int OriginalIndex { get; init; }
    /// <summary>1-based display number. Zero when the window could not be placed on a monitor.</summary>
    public int MonitorNumber { get; set; }
    public int MonitorWidth { get; set; }
    public int MonitorHeight { get; set; }
    public bool MonitorPrimary { get; set; }
    public string MonitorFriendlyName { get; set; } = "";

    public WindowEntry Copy() => new()
    {
        Handle = Handle,
        Title = Title,
        ClassName = ClassName,
        ProcessName = ProcessName,
        HostedProcessName = HostedProcessName,
        DisplayProcessName = DisplayProcessName,
        ProcessId = ProcessId,
        HostedProcessId = HostedProcessId,
        KillProcessId = KillProcessId,
        KillProcessName = KillProcessName,
        IconPath = IconPath,
        IsIconic = IsIconic,
        IsZoomed = IsZoomed,
        OriginalIndex = OriginalIndex,
        MonitorNumber = MonitorNumber,
        MonitorWidth = MonitorWidth,
        MonitorHeight = MonitorHeight,
        MonitorPrimary = MonitorPrimary,
        MonitorFriendlyName = MonitorFriendlyName
    };
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
    /// <summary>
    /// 1 is a v0.1.0 file (Win+Tab capture defaulted off). v0.2.0 still wrote 1, and is
    /// recognized by the presence of stickySwitcher. 3 is a file saved by v0.3.0.
    /// 4 makes initial-letter order the window-list default.
    /// 5 drops live preview, minimized-preview restore, and the other-monitor reminder.
    /// The switcher is shown on every monitor. Those keys are ignored and not written back.
    /// 6 makes stable grouped the default. A stored initial-letter sort, which was the
    /// default, moves to stable grouped. An explicit recent, CPU, or memory choice stays.
    /// Choosing initial letter again after this upgrade keeps it.
    /// 7 turns that stable grouped mode into focus time (<see cref="LoadSortMode.FocusTime"/>).
    /// It adds <see cref="PinnedApps"/>, <see cref="DemotedApps"/>, and
    /// <see cref="FocusThresholdMinutes"/> (10 when missing). A stored stable-grouped
    /// sort loads as focus time. Recent, app name, CPU, and memory stay.
    /// </summary>
    public const int CurrentSchema = 7;

    public int Version { get; set; } = CurrentSchema;
    public HotkeyChord? Hotkey { get; set; } = HotkeyChord.AltTab();
    /// <summary>When true (the default), Win+Tab opens AltPane instead of Task View.</summary>
    public bool CaptureWinTab { get; set; } = true;
    public AppTheme Theme { get; set; } = AppTheme.System;
    public SwitcherSize Size { get; set; } = SwitcherSize.Comfortable;
    public SwitcherPlacement Placement { get; set; } = SwitcherPlacement.MouseCursor;
    /// <summary>When true (the default), each row shows its display relative to the monitor drawing that switcher.</summary>
    public bool ShowMonitorBadges { get; set; } = true;
    public AppLanguage Language { get; set; } = AppLanguage.System;
    public List<string> ExcludedProcesses { get; set; } = new();
    public bool StartWithWindows { get; set; }
    public bool OverridePaused { get; set; }
    public bool IncludeOtherVirtualDesktops { get; set; }
    public bool WelcomeShown { get; set; }
    public bool ShowLoad { get; set; } = true;
    public LoadSortMode SortMode { get; set; } = LoadSortMode.FocusTime;
    /// <summary>
    /// Decayed focus minutes an app needs to stay in the main list. Entering takes
    /// this much; leaving takes 80 percent, so a small change does not flap.
    /// </summary>
    public int FocusThresholdMinutes { get; set; } = 10;
    public bool ConfirmEndTask { get; set; } = true;
    public bool EndTaskKillsTree { get; set; } = true;
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
    /// <summary>
    /// Process names kept in the main list. A pin overrides a demotion and the
    /// focus threshold. Inside that list, order is still most recent use.
    /// </summary>
    public List<string> PinnedApps { get; set; } = new();
    /// <summary>Process names kept under Rarely used. Built-in VPN and tray tools are demoted too.</summary>
    public List<string> DemotedApps { get; set; } = new();
    /// <summary>When true (the default), a chosen window on another monitor is moved onto the switcher's monitor.</summary>
    public bool MoveToSwitcherMonitor { get; set; } = true;
    /// <summary>When true (the default), a search with no built-in hit also asks Everything, if it is running.</summary>
    public bool UseEverything { get; set; } = true;
    public string EverythingExtensions { get; set; } = "exe,lnk,url,appref-ms";
    public List<string> EverythingExcludedPaths { get; set; } = new();
    /// <summary>Document extensions for file search. A blank value uses the built-in list. <c>f </c> or <c>file:</c> searches every type.</summary>
    public string DocumentExtensions { get; set; } = "doc,docx,xls,xlsx,ppt,pptx,pdf,txt,md,csv,rtf,odt,ods,odp,wps,et,dps,ofd,epub";
    /// <summary>Everything HTTP server port used when IPC does not answer. 80 is Everything's default.</summary>
    public int EverythingHttpPort { get; set; } = 80;
    /// <summary>When the configured HTTP port does not answer, also try 8080.</summary>
    public bool EverythingHttpFallback { get; set; } = true;
    /// <summary>When true (the default), the switcher shows the shortcuts for the selected row.</summary>
    public bool ShowActionHint { get; set; } = true;
    /// <summary>When true, Win and Tab hook decisions are written to altpane.log.</summary>
    public bool VerboseLog { get; set; }

    public static AppSettings CreateDefault() => new()
    {
        Version = CurrentSchema,
        Hotkey = HotkeyChord.AltTab(),
        Size = SwitcherSize.Comfortable,
        Placement = SwitcherPlacement.MouseCursor,
        ShowMonitorBadges = true,
        Theme = AppTheme.System,
        Language = AppLanguage.System,
        ExcludedProcesses = new List<string>(),
        ExcludedApps = new List<string>(),
        PinnedApps = new List<string>(),
        DemotedApps = new List<string>(),
        ShowLoad = true,
        SortMode = LoadSortMode.FocusTime,
        FocusThresholdMinutes = 10,
        ConfirmEndTask = true,
        EndTaskKillsTree = true,
        StickySwitcher = true,
        CaptureWinTab = true,
        LaunchApps = true,
        IndexStartMenu = true,
        IndexStoreApps = true,
        IndexDesktop = true,
        MoveToSwitcherMonitor = true,
        UseEverything = true,
        EverythingExtensions = "exe,lnk,url,appref-ms",
        EverythingExcludedPaths = new List<string>(),
        DocumentExtensions = "doc,docx,xls,xlsx,ppt,pptx,pdf,txt,md,csv,rtf,odt,ods,odp,wps,et,dps,ofd,epub",
        EverythingHttpPort = 80,
        EverythingHttpFallback = true,
        ShowActionHint = true
    };
}
