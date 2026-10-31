using AltPane.Core.Windows;
using Xunit;

namespace AltPane.Tests;

public class ActivationPolicyTests
{
    [Fact]
    public void Z_order_flags_keep_size_and_position()
    {
        Assert.Equal(0x0613u, ActivationPolicy.PreserveZOrder);
        Assert.True(ActivationPolicy.PreservesFrame(ActivationPolicy.PreserveZOrder));
        Assert.False(ActivationPolicy.PreservesFrame(0));
        Assert.False(ActivationPolicy.PreservesFrame(0x0001));
    }

    [Fact]
    public void Only_a_minimized_window_is_restored_and_a_visible_window_is_not_shown_again()
    {
        Assert.Equal(ActivationPolicy.SwRestore, ActivationPolicy.ShowCommand(iconic: true, visible: true));
        Assert.Equal(ActivationPolicy.SwRestore, ActivationPolicy.ShowCommand(iconic: true, visible: false));
        Assert.Null(ActivationPolicy.ShowCommand(iconic: false, visible: true));
        Assert.Equal(ActivationPolicy.SwShow, ActivationPolicy.ShowCommand(iconic: false, visible: false));
    }

    [Fact]
    public void A_resize_or_an_unmaximize_is_drift_and_a_restore_from_iconic_is_not()
    {
        var large = new WindowFrame(10, 20, 1200, 800, Iconic: false, Zoomed: false, Visible: true);
        Assert.False(ActivationPolicy.FrameDrifted(large, large with { Width = 1204 }));
        Assert.True(ActivationPolicy.FrameDrifted(large, large with { Width = 160, Height = 40 }));
        Assert.True(ActivationPolicy.FrameDrifted(large with { Zoomed = true }, large with { Zoomed = false }));
        Assert.True(ActivationPolicy.LostMaximized(large with { Zoomed = true }, large with { Zoomed = false }));

        var iconic = new WindowFrame(0, 0, 160, 28, Iconic: true, Zoomed: false, Visible: true);
        var restored = new WindowFrame(10, 20, 1200, 800, Iconic: false, Zoomed: false, Visible: true);
        Assert.False(ActivationPolicy.FrameDrifted(iconic, restored));
    }

    [Fact]
    public void A_move_that_turns_a_large_window_into_a_stamp_is_refused()
    {
        Assert.True(ActivationPolicy.MoveWouldCollapse(1920, 1080, 48, 32));
        Assert.True(ActivationPolicy.MoveWouldCollapse(1600, 900, 400, 300));
        Assert.False(ActivationPolicy.MoveWouldCollapse(1920, 1080, 1366, 768));
        Assert.False(ActivationPolicy.MoveWouldCollapse(200, 80, 40, 40));

        var coerced = ActivationPolicy.CoerceRestore(0, 0, 16, 8, 1920, 1080);
        Assert.Equal(960, coerced.Width);
        Assert.Equal(540, coerced.Height);
        var kept = ActivationPolicy.CoerceRestore(8, 8, 400, 300, 1920, 1080);
        Assert.Equal((8, 8, 400, 300), kept);
    }
}
