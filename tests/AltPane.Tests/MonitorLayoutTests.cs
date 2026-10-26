using AltPane.Core.Layout;
using AltPane.Core.Models;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class MonitorLayoutTests
{
    static DisplayBounds Screen(
        int x, int y, int width, int height,
        int number = 0,
        string name = "",
        bool primary = false,
        int screenX = 0,
        int screenY = 0,
        int screenWidth = 0,
        int screenHeight = 0,
        string friendly = "") =>
        new(x, y, width, height, 96, 96, number, name, primary, screenX, screenY, screenWidth, screenHeight, friendly);

    [Fact]
    public void One_monitor_hides_badges_and_two_monitors_show_them()
    {
        Assert.False(MonitorLayout.ShowBadges(0));
        Assert.False(MonitorLayout.ShowBadges(1));
        Assert.True(MonitorLayout.ShowBadges(2));
    }

    [Fact]
    public void Spanning_window_uses_the_largest_overlap_and_ties_break_to_the_lower_number()
    {
        var monitors = new[]
        {
            Screen(0, 0, 1920, 1080, number: 1),
            Screen(1920, 0, 1920, 1080, number: 2)
        };
        Assert.Equal(1, MonitorLayout.IndexForRect(monitors, 1800, 100, 3400, 800));
        Assert.Equal(0, MonitorLayout.IndexForRect(monitors, 960, 0, 2880, 1080));
        Assert.Equal(-1, MonitorLayout.IndexForRect(monitors, 10, 10, 10, 40));
        Assert.Equal(-1, MonitorLayout.IndexForRect(Array.Empty<DisplayBounds>(), 0, 0, 10, 10));
    }

    [Fact]
    public void A_rect_that_misses_every_monitor_uses_the_nearest_center()
    {
        var monitors = new[]
        {
            Screen(0, 0, 1920, 1080, number: 1),
            Screen(1920, 0, 1920, 1080, number: 2)
        };
        Assert.Equal(0, MonitorLayout.IndexForRect(monitors, -4000, -4000, -3900, -3900));
        Assert.Equal(1, MonitorLayout.IndexForRect(monitors, 10000, 0, 10100, 100));
        Assert.Equal(-1, MonitorLayout.IndexForRect(monitors, 10000, 0, 10100, 100, nearest: false));
    }

    [Fact]
    public void Full_screen_rect_is_used_when_it_is_present()
    {
        var workOnly = Screen(0, 40, 1920, 1040, number: 1);
        var full = Screen(0, 40, 1920, 1040, number: 1, screenX: 0, screenY: 0, screenWidth: 2560, screenHeight: 1440);
        Assert.Equal((1920, 1040), MonitorLayout.Resolution(workOnly));
        Assert.Equal((2560, 1440), MonitorLayout.Resolution(full));
        var other = Screen(2560, 0, 1920, 1080, number: 2);
        Assert.Equal(0, MonitorLayout.IndexForRect(new[] { full, other }, 2400, 0, 2700, 100));
    }

    [Fact]
    public void Gdi_display_numbers_are_kept_when_they_are_unique()
    {
        Assert.Equal(2, MonitorLayout.DisplayIndex(@"\\.\DISPLAY2"));
        Assert.Equal(1, MonitorLayout.DisplayIndex("DISPLAY1"));
        Assert.Null(MonitorLayout.DisplayIndex("DISPLAY0"));
        Assert.Null(MonitorLayout.DisplayIndex("Dell U2720Q"));

        var numbered = MonitorLayout.NumberAndSort(new[]
        {
            Screen(1920, 0, 1280, 1024, name: "DISPLAY1"),
            Screen(0, 0, 1920, 1080, name: @"\\.\DISPLAY2")
        });
        Assert.Equal(0, numbered[0].X);
        Assert.Equal(2, numbered[0].Number);
        Assert.Equal(1, numbered[1].Number);

        var fallback = MonitorLayout.NumberAndSort(new[]
        {
            Screen(1920, 0, 1280, 1024, name: "DISPLAY1"),
            Screen(0, 0, 1920, 1080, name: "DISPLAY1")
        });
        Assert.Equal(1, fallback[0].Number);
        Assert.Equal(2, fallback[1].Number);
    }

    [Fact]
    public void Detail_suffix_prefers_primary_and_skips_generic_names()
    {
        Assert.Equal("Primary", MonitorLayout.DetailSuffix(true, "Generic PnP Monitor", "Primary"));
        Assert.Equal("Primary · Dell U2720Q", MonitorLayout.DetailSuffix(true, "Dell U2720Q", "Primary"));
        Assert.Equal("Dell U2720Q", MonitorLayout.DetailSuffix(false, "Dell U2720Q", "Primary"));
        Assert.Null(MonitorLayout.DetailSuffix(false, "DISPLAY2", "Primary"));
        Assert.Null(MonitorLayout.DetailSuffix(false, "通用即插即用监视器", "主显示器"));
        Assert.Equal("主显示器", MonitorLayout.DetailSuffix(true, "", "主显示器"));
    }

    [Fact]
    public void Colors_follow_the_display_number_and_wrap()
    {
        Assert.Equal(0, MonitorLayout.ColorSlot(1));
        Assert.Equal(7, MonitorLayout.ColorSlot(8));
        Assert.Equal(0, MonitorLayout.ColorSlot(9));
        Assert.Equal(MonitorLayout.Palette[0], MonitorLayout.Color(1));
        Assert.Equal(MonitorLayout.Palette[0], MonitorLayout.Color(9));
    }

    [Fact]
    public void Minimized_restore_rect_shifts_onto_the_primary_work_area_only_when_needed()
    {
        var primary = Screen(0, 40, 1920, 1040, number: 1, primary: true);
        var monitors = new[] { primary };
        Assert.Equal((0, 40), MonitorLayout.WorkspaceOrigin(monitors));
        var shifted = MonitorLayout.PreferIntersecting(monitors, 100, 0, 400, 20, 0, 40);
        Assert.Equal((100, 40, 400, 60), shifted);
        var already = MonitorLayout.PreferIntersecting(monitors, 100, 80, 500, 400, 0, 40);
        Assert.Equal((100, 80, 500, 400), already);
    }

    [Fact]
    public void Monitor_filter_keeps_windows_on_that_display_and_can_be_cleared()
    {
        var session = new SwitcherSession(new[]
        {
            Win(1, "Left", 1),
            Win(2, "Right", 2),
            Win(3, "Also right", 2)
        });
        session.Move(1);
        Assert.Equal(2, session.Selected!.Handle);
        session.SetMonitorFilter(2);
        Assert.Equal(2, session.Filtered.Count);
        Assert.Equal(2, session.Selected!.Handle);
        Assert.All(session.Filtered, entry => Assert.Equal(2, entry.MonitorNumber));
        session.SetMonitorFilter(1);
        Assert.Equal("Left", Assert.Single(session.Filtered).Title);
        session.SetQuery("right");
        Assert.Empty(session.Filtered);
        session.SetQuery("");
        session.SetMonitorFilter(0);
        Assert.Equal(3, session.Filtered.Count);
    }

    static WindowEntry Win(long handle, string title, int monitor) => new()
    {
        Handle = handle,
        Title = title,
        ClassName = "C",
        ProcessName = "app",
        DisplayProcessName = "app",
        ProcessId = (int)handle,
        KillProcessId = (int)handle,
        KillProcessName = "app",
        OriginalIndex = (int)handle,
        MonitorNumber = monitor
    };
}
