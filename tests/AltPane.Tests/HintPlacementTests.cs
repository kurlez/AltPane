using AltPane.Core.Layout;
using Xunit;

namespace AltPane.Tests;

public class HintPlacementTests
{
    static DisplayBounds Screen(int x, int y, int width, int height, int number = 0, string name = "") =>
        new(x, y, width, height, 96, 96, number, name);

    [Fact]
    public void Host_is_the_monitor_under_the_center()
    {
        var monitors = new[]
        {
            Screen(0, 0, 1920, 1080, number: 1),
            Screen(1920, 0, 1280, 1024, number: 2)
        };
        Assert.Equal(0, MonitorLayout.HostIndex(monitors, 100, 100, 800, 500));
        Assert.Equal(1, MonitorLayout.HostIndex(monitors, 2000, 100, 400, 300));
        Assert.Equal(1, MonitorLayout.HostIndex(monitors, 1820, 0, 200, 100));
        Assert.Equal(-1, MonitorLayout.HostIndex(monitors, 0, 0, 0, 10));
        Assert.Equal(-1, MonitorLayout.HostIndex(Array.Empty<DisplayBounds>(), 0, 0, 10, 10));
    }

    [Fact]
    public void A_window_on_this_instance_is_labeled_this_screen()
    {
        Assert.Equal("This screen", MonitorBadge.Label(2, 2, "This screen"));
        Assert.Equal("当前屏幕", MonitorBadge.Label(1, 1, "当前屏幕"));
        Assert.Equal("2", MonitorBadge.Label(2, 1, "当前屏幕"));
        Assert.Equal("3", MonitorBadge.Label(3, 0, "当前屏幕"));
        Assert.Equal("", MonitorBadge.Label(0, 1, "当前屏幕"));
        Assert.Equal("1", MonitorBadge.Label(1, 1, ""));
    }
}
