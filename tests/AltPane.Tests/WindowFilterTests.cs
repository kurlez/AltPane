using AltPane.Core.Filtering;
using AltPane.Core.Models;
using Xunit;

namespace AltPane.Tests;

public class WindowFilterTests
{
    static FilterContext Ctx(bool otherDesktops = false, params string[] excluded) => new()
    {
        CurrentProcessId = 7,
        IncludeOtherVirtualDesktops = otherDesktops,
        ExcludedProcessNames = excluded
    };

    static WindowCandidate Ok() => new()
    {
        Handle = 100,
        Title = "Notepad",
        ClassName = "Notepad",
        ProcessName = "notepad",
        ProcessId = 42,
        RootOwnerHandle = 100,
        IsVisible = true,
        CloakFlags = 0,
        VirtualDesktopSame = 1
    };

    [Fact]
    public void Includes_a_normal_visible_window() =>
        Assert.True(WindowFilter.ShouldInclude(Ok(), Ctx()));

    [Fact]
    public void Excludes_invisible_and_zero_handles()
    {
        Assert.False(WindowFilter.ShouldInclude(Ok() with { IsVisible = false }, Ctx()));
        Assert.False(WindowFilter.ShouldInclude(Ok() with { Handle = 0 }, Ctx()));
    }

    [Fact]
    public void Excludes_tool_windows_unless_appwindow()
    {
        Assert.False(WindowFilter.ShouldInclude(Ok() with { ExStyle = WinStyles.WsExToolWindow }, Ctx()));
        Assert.True(WindowFilter.ShouldInclude(
            Ok() with { ExStyle = WinStyles.WsExToolWindow | WinStyles.WsExAppWindow }, Ctx()));
    }

    [Fact]
    public void Excludes_owned_windows_unless_appwindow()
    {
        Assert.False(WindowFilter.ShouldInclude(Ok() with { RootOwnerHandle = 9, OwnerHandle = 9 }, Ctx()));
        Assert.True(WindowFilter.ShouldInclude(
            Ok() with { RootOwnerHandle = 9, OwnerHandle = 9, ExStyle = WinStyles.WsExAppWindow }, Ctx()));
    }

    [Fact]
    public void Owner_fallback_when_root_is_unknown() =>
        Assert.False(WindowFilter.ShouldInclude(Ok() with { RootOwnerHandle = 0, OwnerHandle = 50 }, Ctx()));

    [Fact]
    public void Excludes_noactivate_unless_appwindow()
    {
        Assert.False(WindowFilter.ShouldInclude(Ok() with { ExStyle = WinStyles.WsExNoActivate }, Ctx()));
        Assert.True(WindowFilter.ShouldInclude(
            Ok() with { ExStyle = WinStyles.WsExNoActivate | WinStyles.WsExAppWindow }, Ctx()));
    }

    [Fact]
    public void Excludes_cloaked_uwp_ghosts_even_if_other_desktops_are_requested()
    {
        var ghost = Ok() with
        {
            ClassName = "ApplicationFrameWindow",
            ProcessName = "ApplicationFrameHost",
            Title = "Photos",
            CloakFlags = WinStyles.CloakApp
        };
        Assert.False(WindowFilter.ShouldInclude(ghost, Ctx()));
        Assert.False(WindowFilter.ShouldInclude(ghost, Ctx(otherDesktops: true)));
    }

    [Fact]
    public void Shell_cloak_follows_the_virtual_desktop_setting()
    {
        var parked = Ok() with { CloakFlags = WinStyles.CloakShell };
        Assert.False(WindowFilter.ShouldInclude(parked, Ctx()));
        Assert.True(WindowFilter.ShouldInclude(parked, Ctx(otherDesktops: true)));
    }

    [Fact]
    public void Virtual_desktop_flag_is_honored_when_not_cloaked()
    {
        var other = Ok() with { VirtualDesktopSame = 0, CloakFlags = 0 };
        Assert.False(WindowFilter.ShouldInclude(other, Ctx()));
        Assert.True(WindowFilter.ShouldInclude(other, Ctx(otherDesktops: true)));
        Assert.True(WindowFilter.ShouldInclude(Ok() with { VirtualDesktopSame = -1 }, Ctx()));
    }

    [Fact]
    public void Excludes_shell_classes_and_uwp_core_windows()
    {
        Assert.False(WindowFilter.ShouldInclude(Ok() with { ClassName = "Progman", Title = "Program Manager" }, Ctx()));
        Assert.False(WindowFilter.ShouldInclude(Ok() with { ClassName = "Shell_TrayWnd", Title = "Tray" }, Ctx()));
        Assert.False(WindowFilter.ShouldInclude(
            Ok() with { ClassName = "Windows.UI.Core.CoreWindow", Title = "App" }, Ctx()));
    }

    [Fact]
    public void Includes_visible_application_frame_host_window() =>
        Assert.True(WindowFilter.ShouldInclude(Ok() with
        {
            ClassName = "ApplicationFrameWindow",
            ProcessName = "ApplicationFrameHost",
            HostedProcessName = "Microsoft.Windows.Photos",
            Title = "Photos"
        }, Ctx()));

    [Fact]
    public void Excludes_empty_titles_and_our_own_process()
    {
        Assert.False(WindowFilter.ShouldInclude(Ok() with { Title = "   " }, Ctx()));
        Assert.False(WindowFilter.ShouldInclude(Ok() with { ProcessId = 7 }, Ctx()));
        Assert.True(WindowFilter.ShouldInclude(Ok() with { ProcessId = 7 }, new FilterContext()));
    }

    [Fact]
    public void Exclusion_list_matches_process_or_hosted_name_without_exe()
    {
        Assert.False(WindowFilter.ShouldInclude(Ok(), Ctx(false, "Notepad.EXE")));
        var uwp = Ok() with
        {
            ProcessName = "ApplicationFrameHost",
            HostedProcessName = "WeChat.exe",
            Title = "微信"
        };
        Assert.False(WindowFilter.ShouldInclude(uwp, Ctx(false, "wechat")));
        Assert.False(WindowFilter.ShouldInclude(uwp, Ctx(false, "ApplicationFrameHost")));
        Assert.True(WindowFilter.ShouldInclude(uwp, Ctx(false, "spotify")));
    }

    [Fact]
    public void Includes_minimized_windows_represented_as_visible() =>
        Assert.True(WindowFilter.ShouldInclude(Ok(), Ctx()));
}
