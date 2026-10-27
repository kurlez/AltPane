using AltPane.Core.Preview;
using Xunit;

namespace AltPane.Tests;

public class MinimizedPreviewTests
{
    [Fact]
    public void A_minimized_window_that_was_maximized_stays_maximized_when_restored()
    {
        var maximized = new PlacementSnapshot(
            Flags: PlacementEdits.RestoreToMaximized,
            ShowCmd: PlacementEdits.ShowMinimized,
            MinX: -1, MinY: -1, MaxX: -1, MaxY: -1,
            NormalLeft: 10, NormalTop: 20, NormalRight: 800, NormalBottom: 600);
        var normal = maximized with { Flags = 0, ShowCmd = PlacementEdits.ShowMinimized };
        Assert.True(PlacementEdits.WasMaximized(maximized));
        Assert.True(PlacementEdits.WasMaximized(maximized with { Flags = 0, ShowCmd = PlacementEdits.ShowMaximized }));
        Assert.False(PlacementEdits.WasMaximized(normal));
    }
}
