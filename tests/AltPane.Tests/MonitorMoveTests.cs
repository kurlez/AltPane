using AltPane.Core.Layout;
using Xunit;

namespace AltPane.Tests;

public class MonitorMoveTests
{
    static DisplayBounds Work(int x, int y, int w, int h, int dpi, int number) =>
        new(x, y, w, h, dpi, dpi, number, @"\\.\DISPLAY" + number, number == 1, x, y, w, h);

    [Fact]
    public void Fractions_of_the_work_area_survive_a_dpi_change_without_a_second_scale()
    {
        var source = Work(0, 0, 1920, 1080, 96, 1);
        var target = Work(1920, 0, 3840, 2160, 192, 2);
        var plan = MonitorMove.Plan(source, target, 0, 0, 960, 540, false, false, false, 0, 0, 0, 0, false);
        Assert.True(plan.Move);
        Assert.False(plan.Maximize);
        Assert.Equal(1920, plan.X);
        Assert.Equal(0, plan.Y);
        Assert.Equal(1920, plan.Width);
        Assert.Equal(1080, plan.Height);
    }

    [Fact]
    public void A_window_larger_than_the_target_is_clamped_inside_the_work_area()
    {
        var source = Work(0, 0, 2560, 1440, 96, 1);
        var target = Work(2560, 0, 800, 600, 96, 2);
        var plan = MonitorMove.Plan(source, target, -40, -80, 3000, 2000, false, false, false, 0, 0, 0, 0, false);
        Assert.True(plan.Move);
        Assert.Equal(800, plan.Width);
        Assert.Equal(600, plan.Height);
        Assert.Equal(target.X, plan.X);
        Assert.Equal(target.Y, plan.Y);
    }

    [Fact]
    public void The_same_monitor_is_left_alone()
    {
        var monitor = Work(0, 40, 1920, 1040, 96, 1);
        var plan = MonitorMove.Plan(monitor, monitor, 100, 80, 800, 600, false, false, false, 0, 0, 0, 0, false);
        Assert.False(plan.Move);
        Assert.Equal(100, plan.X);
        Assert.Equal(800, plan.Width);
    }

    [Fact]
    public void A_left_snap_is_replayed_on_the_target()
    {
        var source = Work(0, 0, 1920, 1080, 96, 1);
        var target = Work(1920, 0, 1600, 900, 96, 2);
        var plan = MonitorMove.Plan(source, target, 0, 0, 960, 1080, false, false, false, 0, 0, 0, 0, false);
        Assert.Equal(SnapEdge.Left, MonitorMove.DetectSnap(source, 0, 0, 960, 1080));
        Assert.Equal(target.X, plan.X);
        Assert.Equal(target.Y, plan.Y);
        Assert.Equal(800, plan.Width);
        Assert.Equal(900, plan.Height);
        Assert.False(plan.Maximize);
    }

    [Fact]
    public void Maximized_and_fullscreen_windows_maximize_on_the_target_and_keep_a_mapped_restore_rect()
    {
        var source = Work(0, 0, 1920, 1080, 96, 1);
        var target = Work(1920, 0, 2560, 1440, 120, 2);
        var maximized = MonitorMove.Plan(
            source, target, 0, 0, 1920, 1080, minimized: false, maximized: true, fullscreen: false,
            restoreLeft: 100, restoreTop: 80, restoreWidth: 960, restoreHeight: 540, restoreToMaximized: false);
        Assert.True(maximized.Move);
        Assert.True(maximized.Maximize);
        Assert.Equal(1920 + (int)Math.Round(100 / 1920d * 2560), maximized.X);
        Assert.Equal((int)Math.Round(80 / 1080d * 1440), maximized.Y);
        Assert.Equal(1280, maximized.Width);
        Assert.Equal(720, maximized.Height);

        var full = MonitorMove.Plan(
            source, target, 0, 0, 1920, 1080, false, false, fullscreen: true,
            40, 40, 800, 600, false);
        Assert.True(full.Maximize);
        Assert.True(MonitorMove.CoversScreen(source, 0, 0, 1920, 1080));
    }

    [Fact]
    public void A_minimized_window_uses_its_restore_rect_unless_it_should_come_back_maximized()
    {
        var source = Work(0, 0, 1000, 1000, 96, 1);
        var target = Work(2000, 0, 2000, 1000, 96, 2);
        var restored = MonitorMove.Plan(
            source, target, -32000, -32000, 160, 28, minimized: true, maximized: false, fullscreen: false,
            restoreLeft: 100, restoreTop: 200, restoreWidth: 500, restoreHeight: 400, restoreToMaximized: false);
        Assert.False(restored.Maximize);
        Assert.Equal(2000 + 200, restored.X);
        Assert.Equal(200, restored.Y);
        Assert.Equal(1000, restored.Width);
        Assert.Equal(400, restored.Height);

        var backToMax = MonitorMove.Plan(
            source, target, -32000, -32000, 160, 28, true, false, false,
            100, 200, 500, 400, restoreToMaximized: true);
        Assert.True(backToMax.Maximize);
    }
}
