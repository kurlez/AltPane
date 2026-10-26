using AltPane.Core.Models;
using AltPane.Core.Preview;
using AltPane.Core.Settings;
using Xunit;

namespace AltPane.Tests;

public class MinimizedPreviewTests
{
    static PlacementSnapshot Minimized(bool maximizedBefore) => new(
        Flags: maximizedBefore ? PlacementEdits.RestoreToMaximized : 0,
        ShowCmd: PlacementEdits.ShowMinimized,
        MinX: -1, MinY: -1, MaxX: -1, MaxY: -1,
        NormalLeft: 100, NormalTop: 40, NormalRight: 900, NormalBottom: 640);

    [Fact]
    public void Off_screen_preview_keeps_size_and_does_not_maximize()
    {
        var original = Minimized(maximizedBefore: true);
        var preview = PlacementEdits.OffScreen(original, virtualLeft: -1920, virtualTop: 0);
        Assert.Equal(800, preview.NormalRight - preview.NormalLeft);
        Assert.Equal(600, preview.NormalBottom - preview.NormalTop);
        Assert.True(preview.NormalRight <= -1920);
        Assert.Equal(PlacementEdits.ShowNoActivate, preview.ShowCmd);
        Assert.Equal(0, preview.Flags & PlacementEdits.RestoreToMaximized);
        Assert.Equal(PlacementEdits.RestoreToMaximized, original.Flags);
        Assert.Equal(100, original.NormalLeft);
    }

    [Fact]
    public void Activate_restores_maximized_and_normal_placement()
    {
        var maximized = PlacementEdits.ForActivate(Minimized(true));
        Assert.Equal(PlacementEdits.ShowMaximized, maximized.ShowCmd);
        Assert.Equal(100, maximized.NormalLeft);
        Assert.Equal(900, maximized.NormalRight);

        var normal = PlacementEdits.ForActivate(Minimized(false));
        Assert.Equal(PlacementEdits.ShowNoActivate, normal.ShowCmd);
        Assert.Equal(40, normal.NormalTop);
    }

    [Fact]
    public void Moving_away_returns_the_borrow_and_can_borrow_the_next_window()
    {
        var session = new MinimizedPreviewSession();
        var first = Minimized(false);
        var step = session.Consider(5, iconic: true, auto: true, manual: false);
        Assert.Equal(5, step.BorrowHandle);
        Assert.Equal(0, step.ReturnHandle);
        session.NoteBorrowed(5, first);

        step = session.Consider(5, iconic: true, auto: true, manual: false);
        Assert.Equal(0, step.BorrowHandle);
        Assert.True(session.IsBorrowing);

        step = session.Consider(8, iconic: true, auto: false, manual: true);
        Assert.Equal(5, step.ReturnHandle);
        Assert.Equal(first, step.ReturnPlacement);
        Assert.Equal(8, step.BorrowHandle);
        Assert.False(session.IsBorrowing);
    }

    [Fact]
    public void Automatic_preview_stays_off_until_asked_or_enabled()
    {
        var session = new MinimizedPreviewSession();
        var idle = session.Consider(3, iconic: true, auto: false, manual: false);
        Assert.Equal(0, idle.BorrowHandle);
        var manual = session.Consider(3, iconic: true, auto: false, manual: true);
        Assert.Equal(3, manual.BorrowHandle);
        var visible = new MinimizedPreviewSession();
        Assert.Equal(0, visible.Consider(3, iconic: false, auto: true, manual: true).BorrowHandle);
    }

    [Fact]
    public void Commit_puts_the_window_back_for_activation_and_cancel_minimizes_it()
    {
        var original = Minimized(true);
        var session = new MinimizedPreviewSession();
        session.NoteBorrowed(9, original);

        var committed = session.Commit();
        Assert.Equal(9, committed.ReleaseHandle);
        Assert.Equal(PlacementEdits.ShowMaximized, committed.ReleasePlacement!.Value.ShowCmd);
        Assert.Equal(100, committed.ReleasePlacement.Value.NormalLeft);
        Assert.False(session.IsBorrowing);
        Assert.Equal(0, session.Commit().ReleaseHandle);

        session.NoteBorrowed(9, original);
        var cancelled = session.Cancel();
        Assert.Equal(9, cancelled.ReturnHandle);
        Assert.Equal(original, cancelled.ReturnPlacement);
        Assert.Equal(PlacementEdits.ShowMinimized, cancelled.ReturnPlacement!.Value.ShowCmd);
        Assert.False(session.IsBorrowing);
    }

    [Fact]
    public void Preview_setting_defaults_off_and_round_trips()
    {
        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "theme": "Dark" }""");
        Assert.False(SettingsStore.Load(path).Settings.PreviewMinimized);

        var settings = AppSettings.CreateDefault();
        settings.PreviewMinimized = true;
        SettingsStore.Save(path, settings);
        Assert.True(SettingsStore.Load(path).Settings.PreviewMinimized);
    }
}
