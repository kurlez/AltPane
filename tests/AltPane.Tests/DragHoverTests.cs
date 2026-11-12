using AltPane.Core.Input;
using AltPane.Core.Localization;
using AltPane.Core.Models;
using AltPane.Core.Settings;
using Xunit;

namespace AltPane.Tests;

public class DragHoverTests
{
    static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void Dwell_fires_once_after_the_configured_time_on_the_same_target()
    {
        var dwell = new DragHoverDwell();
        Assert.False(dwell.Note("w:1", 1_000, 700));
        Assert.True(dwell.IsArmed);
        Assert.False(dwell.Note("w:1", 1_000 + 699, 700));
        Assert.True(dwell.Note("w:1", 1_000 + 700, 700));
        Assert.True(dwell.HasFired);
        Assert.False(dwell.IsArmed);
        Assert.False(dwell.Note("w:1", 1_000 + 2_000, 700));
        Assert.Equal(1, dwell.Progress(1_000 + 2_000, 700));
    }

    [Fact]
    public void A_new_target_restarts_the_timer_and_an_empty_id_cancels()
    {
        var dwell = new DragHoverDwell();
        Assert.False(dwell.Note("w:1", 100, 700));
        Assert.False(dwell.Note("w:2", 400, 700));
        Assert.Equal("w:2", dwell.TargetId);
        Assert.False(dwell.Note("w:2", 400 + 699, 700));
        Assert.True(dwell.Note("w:2", 400 + 700, 700));

        var other = new DragHoverDwell();
        other.Note("w:1", 0, 700);
        Assert.False(other.Note(null, 800, 700));
        Assert.Null(other.TargetId);
        Assert.Equal(0, other.Progress(800, 700));
        Assert.False(other.Note("", 800, 700));
        Assert.False(other.Note("w:1", 800, 700));
        Assert.True(other.Note("w:1", 800 + 700, 700));
    }

    [Fact]
    public void Leaving_and_returning_to_the_same_target_can_fire_again()
    {
        var dwell = new DragHoverDwell();
        Assert.True(dwell.Note("s:chrome", 700, 700) is false);
        Assert.True(dwell.Note("s:chrome", 1_400, 700));
        dwell.Note(null, 1_500, 700);
        Assert.False(dwell.HasFired);
        Assert.False(dwell.Note("s:chrome", 1_500, 700));
        Assert.True(dwell.Note("s:chrome", 2_200, 700));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(350, 0.5)]
    [InlineData(700, 1)]
    [InlineData(900, 1)]
    public void Progress_scales_from_zero_to_one(int elapsed, double expected)
    {
        var dwell = new DragHoverDwell();
        dwell.Note("w:8", 0, 700);
        Assert.Equal(expected, dwell.Progress(elapsed, 700), 3);
    }

    [Fact]
    public void Clamp_keeps_the_dwell_between_300_and_2000()
    {
        Assert.Equal(300, DragHoverDwell.Clamp(0));
        Assert.Equal(300, DragHoverDwell.Clamp(-40));
        Assert.Equal(2000, DragHoverDwell.Clamp(9_999));
        Assert.Equal(700, DragHoverDwell.Clamp(700));
        Assert.Equal(DragHoverDwell.DefaultMilliseconds, 700);
    }

    [Fact]
    public void File_drag_formats_are_recognized_and_text_is_not()
    {
        Assert.True(FileDragFormats.IsFileDrag(new[] { "FileDrop", "Preferred DropEffect" }));
        Assert.True(FileDragFormats.IsFileFormat("FileNameW"));
        Assert.True(FileDragFormats.IsFileFormat("FileGroupDescriptorW"));
        Assert.True(FileDragFormats.IsFileFormat("Shell IDList Array"));
        Assert.True(FileDragFormats.IsFileFormat("filename"));
        Assert.False(FileDragFormats.IsFileDrag(new[] { "Text", "UnicodeText", "Bitmap" }));
        Assert.False(FileDragFormats.IsFileDrag(Array.Empty<string>()));
        Assert.False(FileDragFormats.IsFileDrag(null));
        Assert.False(FileDragFormats.IsFileFormat(null));
        Assert.False(FileDragFormats.IsFileFormat(""));
    }

    [Fact]
    public void The_switcher_does_not_take_the_foreground_or_keyboard_while_a_button_is_held()
    {
        Assert.False(DragSwitcherPolicy.TakeForeground(mouseButtonDown: true));
        Assert.True(DragSwitcherPolicy.TakeForeground(mouseButtonDown: false));
        Assert.False(DragSwitcherPolicy.ClaimKeyboard(mouseButtonDown: true));
        Assert.True(DragSwitcherPolicy.ClaimKeyboard(mouseButtonDown: false));
    }

    [Fact]
    public void Window_pair_and_running_sidebar_rows_can_activate_and_others_cannot()
    {
        Assert.True(DragHoverTarget.CanActivate(DragHoverKind.Window));
        Assert.True(DragHoverTarget.CanActivate(DragHoverKind.Pair));
        Assert.True(DragHoverTarget.CanActivate(DragHoverKind.Sidebar));
        Assert.False(DragHoverTarget.CanActivate(DragHoverKind.None));
        Assert.True(DragHoverTarget.CanActivateSidebar(true));
        Assert.False(DragHoverTarget.CanActivateSidebar(false));
        Assert.True(DragHoverTarget.Window(42).Id == "w:42");
        Assert.Equal("p:1:2", DragHoverTarget.Pair(1, 2, 1).Id);
        Assert.Equal("s:chrome", DragHoverTarget.Sidebar(0, "chrome").Id);
        Assert.True(DragHoverTarget.Window(0).IsEmpty);
        Assert.True(DragHoverTarget.None.IsEmpty);
        Assert.True(DragHoverTarget.Sidebar(0, "").IsEmpty);
    }

    [Fact]
    public void Defaults_are_on_at_700_ms_and_schema_is_12()
    {
        Assert.Equal(12, AppSettings.CurrentSchema);
        var defaults = AppSettings.CreateDefault();
        Assert.True(defaults.DragHoverActivate);
        Assert.Equal(700, defaults.DragHoverDwellMilliseconds);
        Assert.True(new AppSettings().DragHoverActivate);
        Assert.Equal(700, new AppSettings().DragHoverDwellMilliseconds);
    }

    [Fact]
    public void Schema_12_turns_the_feature_on_for_old_files_and_keeps_an_explicit_choice()
    {
        var directory = TempDirectory();
        var oldPath = Path.Combine(directory, "old.json");
        File.WriteAllText(oldPath, """{ "version": 11, "switcherStyle": "FollowTheme", "combineTiledWindows": false }""");
        var migrated = SettingsStore.Load(oldPath);
        Assert.True(migrated.Migrated);
        Assert.Equal(AppSettings.CurrentSchema, migrated.Settings.Version);
        Assert.True(migrated.Settings.DragHoverActivate);
        Assert.Equal(700, migrated.Settings.DragHoverDwellMilliseconds);
        Assert.Equal(SwitcherStyle.FollowTheme, migrated.Settings.SwitcherStyle);
        Assert.False(migrated.Settings.CombineTiledWindows);

        var keptPath = Path.Combine(directory, "kept.json");
        File.WriteAllText(keptPath, """{ "version": 12, "dragHoverActivate": false, "dragHoverDwellMilliseconds": 400 }""");
        var kept = SettingsStore.Load(keptPath);
        Assert.False(kept.Migrated);
        Assert.False(kept.Settings.DragHoverActivate);
        Assert.Equal(400, kept.Settings.DragHoverDwellMilliseconds);
    }

    [Fact]
    public void Dwell_round_trips_and_out_of_range_values_are_clamped()
    {
        var directory = TempDirectory();
        var path = Path.Combine(directory, "settings.json");
        var settings = AppSettings.CreateDefault();
        settings.DragHoverActivate = false;
        settings.DragHoverDwellMilliseconds = 1200;
        SettingsStore.Save(path, settings);
        var json = File.ReadAllText(path);
        Assert.Contains("\"dragHoverActivate\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"dragHoverDwellMilliseconds\": 1200", json, StringComparison.Ordinal);
        var round = SettingsStore.Load(path);
        Assert.False(round.Migrated);
        Assert.False(round.Settings.DragHoverActivate);
        Assert.Equal(1200, round.Settings.DragHoverDwellMilliseconds);

        var high = Path.Combine(directory, "high.json");
        File.WriteAllText(high, """{ "version": 12, "dragHoverDwellMilliseconds": 8000 }""");
        Assert.Equal(2000, SettingsStore.Load(high).Settings.DragHoverDwellMilliseconds);

        var low = Path.Combine(directory, "low.json");
        File.WriteAllText(low, """{ "version": 12, "dragHoverDwellMilliseconds": 50 }""");
        Assert.Equal(300, SettingsStore.Load(low).Settings.DragHoverDwellMilliseconds);
    }

    [Fact]
    public void Drag_hover_labels_exist_in_both_languages()
    {
        var keys = new[]
        {
            "settings.dragHover",
            "settings.dragHoverNote",
            "settings.dragHoverDwell",
            "settings.dragHoverDwell.400",
            "settings.dragHoverDwell.600",
            "settings.dragHoverDwell.700",
            "settings.dragHoverDwell.800",
            "settings.dragHoverDwell.1000",
            "settings.dragHoverDwell.1200",
            "settings.dragHoverDwell.custom"
        };
        foreach (var key in keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(StringTables.Chinese[key]));
            Assert.False(string.IsNullOrWhiteSpace(StringTables.English[key]));
        }
        Assert.Equal("拖动悬停激活窗口", StringTables.Chinese["settings.dragHover"]);
        Assert.Equal("悬停时间", StringTables.Chinese["settings.dragHoverDwell"]);
        Assert.Equal("700 毫秒（默认）", StringTables.Chinese["settings.dragHoverDwell.700"]);
    }
}
