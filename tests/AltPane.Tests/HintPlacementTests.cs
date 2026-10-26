using AltPane.Core.Layout;
using Xunit;

namespace AltPane.Tests;

public class HintPlacementTests
{
    static DisplayBounds Screen(int x, int y, int width, int height, int dpi = 96, int number = 0, string name = "") =>
        new(x, y, width, height, dpi, dpi, number, name);

    [Fact]
    public void One_monitor_has_no_hint()
    {
        var monitors = new[] { Screen(0, 0, 1920, 1080, number: 1) };
        Assert.Empty(OtherMonitorHints.Plan(monitors, 0));
        Assert.Empty(OtherMonitorHints.Plan(monitors, -1));
        Assert.Empty(OtherMonitorHints.Plan(Array.Empty<DisplayBounds>(), 0));
    }

    [Fact]
    public void Side_by_side_hints_point_across_and_sit_in_the_center()
    {
        var monitors = new[]
        {
            Screen(0, 0, 1920, 1080, number: 1, name: "DISPLAY1"),
            Screen(1920, 0, 1920, 1080, number: 2, name: "DISPLAY2")
        };
        var hints = OtherMonitorHints.Plan(monitors, hostIndex: 0);
        var hint = Assert.Single(hints);
        Assert.Equal(1, hint.HintMonitorIndex);
        Assert.Equal(1, hint.HostNumber);
        Assert.Equal("DISPLAY1", hint.HostName);
        Assert.Equal(HintDirection.Left, hint.Direction);
        Assert.Equal("←", OtherMonitorHints.Arrow(hint.Direction));
        Assert.Equal(1920 + (1920 - hint.Width) / 2, hint.X);
        Assert.Equal((1080 - hint.Height) / 2, hint.Y);
        Assert.Equal(OtherMonitorHints.WidthDip, hint.Width);
        Assert.Equal(OtherMonitorHints.HeightDip, hint.Height);

        var other = Assert.Single(OtherMonitorHints.Plan(monitors, hostIndex: 1));
        Assert.Equal(0, other.HintMonitorIndex);
        Assert.Equal(HintDirection.Right, other.Direction);
        Assert.Equal(2, other.HostNumber);
    }

    [Fact]
    public void Vertical_and_diagonal_directions_follow_the_host()
    {
        Assert.Equal(HintDirection.Down, OtherMonitorHints.Direction(0, 0, 0, 500));
        Assert.Equal(HintDirection.Up, OtherMonitorHints.Direction(0, 500, 0, 0));
        Assert.Equal(HintDirection.UpLeft, OtherMonitorHints.Direction(2000, 2000, 500, 400));
        Assert.Equal(HintDirection.DownRight, OtherMonitorHints.Direction(0, 0, 800, 900));
        Assert.Equal(HintDirection.Right, OtherMonitorHints.Direction(0, 0, 2000, 100));
        Assert.Equal(HintDirection.Right, OtherMonitorHints.Direction(10, 10, 10, 10));

        var lower = Screen(0, 0, 1920, 1080, number: 1);
        var upper = Screen(0, -1080, 1920, 1080, number: 2);
        var hint = Assert.Single(OtherMonitorHints.Plan(new[] { lower, upper }, hostIndex: 0));
        Assert.Equal(HintDirection.Down, hint.Direction);
        Assert.Equal("↓", OtherMonitorHints.Arrow(hint.Direction));
    }

    [Fact]
    public void High_dpi_and_small_monitors_scale_the_hint_and_keep_it_inside()
    {
        var primary = Screen(0, 0, 1920, 1080, dpi: 96, number: 1);
        var hidpi = Screen(1920, 0, 3840, 2160, dpi: 192, number: 2);
        var hint = Assert.Single(OtherMonitorHints.Plan(new[] { primary, hidpi }, 0));
        Assert.Equal(OtherMonitorHints.WidthDip * 2, hint.Width);
        Assert.Equal(OtherMonitorHints.HeightDip * 2, hint.Height);
        Assert.Equal(1920 + (3840 - hint.Width) / 2, hint.X);
        Assert.Equal((2160 - hint.Height) / 2, hint.Y);

        var tiny = Screen(3000, 0, 200, 120, number: 3);
        var clamped = OtherMonitorHints.Plan(new[] { primary, tiny }, 0).Single(item => item.HintMonitorIndex == 1);
        Assert.True(clamped.Width <= tiny.Width - 16);
        Assert.True(clamped.Height <= tiny.Height - 16);
        Assert.InRange(clamped.X, tiny.X, tiny.X + tiny.Width - clamped.Width);
        Assert.InRange(clamped.Y, tiny.Y, tiny.Y + tiny.Height - clamped.Height);
    }

    [Fact]
    public void Host_is_the_monitor_under_the_switcher_center()
    {
        var monitors = OtherMonitorHints.NumberLeftToRight(new[]
        {
            Screen(1920, 0, 1280, 1024),
            Screen(0, 0, 1920, 1080)
        });
        Assert.Equal(1, monitors[0].Number);
        Assert.Equal(0, monitors[0].X);
        Assert.Equal(2, monitors[1].Number);

        Assert.Equal(0, OtherMonitorHints.HostIndex(monitors, 100, 100, 800, 500));
        Assert.Equal(1, OtherMonitorHints.HostIndex(monitors, 2000, 100, 400, 300));
        Assert.Equal(1, OtherMonitorHints.HostIndex(monitors, 1820, 0, 200, 100));
        Assert.Equal(-1, OtherMonitorHints.HostIndex(monitors, 0, 0, 0, 10));

        var hints = OtherMonitorHints.Plan(monitors, 1);
        Assert.Equal(0, Assert.Single(hints).HintMonitorIndex);
        Assert.Equal(2, hints[0].HostNumber);
    }

    [Fact]
    public void A_cloned_work_area_does_not_cover_the_switcher()
    {
        var monitors = new[]
        {
            Screen(0, 0, 1920, 1080, number: 1),
            Screen(0, 0, 1920, 1080, number: 1),
            Screen(1920, 0, 1920, 1080, number: 2)
        };
        var hints = OtherMonitorHints.Plan(monitors, 0);
        var hint = Assert.Single(hints);
        Assert.Equal(2, hint.HintMonitorIndex);
    }
}
