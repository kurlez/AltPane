using AltPane.Core.Localization;
using AltPane.Core.Models;
using AltPane.Core.Settings;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class SwitcherStyleTests
{
    static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Theory]
    [InlineData(SwitcherStyle.Dark, false, true)]
    [InlineData(SwitcherStyle.Dark, true, true)]
    [InlineData(SwitcherStyle.FollowTheme, false, false)]
    [InlineData(SwitcherStyle.FollowTheme, true, true)]
    public void Dark_style_ignores_the_app_theme_and_follow_uses_it(SwitcherStyle style, bool appDark, bool expected)
    {
        Assert.Equal(expected, SwitcherAppearance.IsDark(style, appDark));
    }

    [Fact]
    public void Palette_names_match_the_theme_files()
    {
        Assert.Equal("Dark", SwitcherAppearance.PaletteName(true));
        Assert.Equal("Light", SwitcherAppearance.PaletteName(false));
        Assert.True(SwitcherAppearance.IsDark((SwitcherStyle)42, false));
    }

    [Fact]
    public void Defaults_are_dark_and_schema_is_at_least_11()
    {
        Assert.True(AppSettings.CurrentSchema >= 11);
        Assert.Equal(SwitcherStyle.Dark, AppSettings.CreateDefault().SwitcherStyle);
        Assert.Equal(SwitcherStyle.Dark, new AppSettings().SwitcherStyle);
    }

    [Fact]
    public void Schema_11_makes_old_files_dark_and_keeps_an_explicit_follow()
    {
        var directory = TempDirectory();
        var oldPath = Path.Combine(directory, "old.json");
        File.WriteAllText(oldPath, """{ "version": 10, "theme": "Light", "combineTiledWindows": false }""");
        var migrated = SettingsStore.Load(oldPath);
        Assert.True(migrated.Migrated);
        Assert.Equal(AppSettings.CurrentSchema, migrated.Settings.Version);
        Assert.Equal(SwitcherStyle.Dark, migrated.Settings.SwitcherStyle);
        Assert.Equal(AppTheme.Light, migrated.Settings.Theme);
        Assert.False(migrated.Settings.CombineTiledWindows);

        var follow = Path.Combine(directory, "follow.json");
        File.WriteAllText(follow, """{ "version": 11, "switcherStyle": "FollowTheme" }""");
        var kept = SettingsStore.Load(follow);
        Assert.Equal(SwitcherStyle.FollowTheme, kept.Settings.SwitcherStyle);
    }

    [Fact]
    public void Switcher_style_round_trips_and_invalid_values_fall_back_to_dark()
    {
        var directory = TempDirectory();
        var path = Path.Combine(directory, "settings.json");
        var settings = AppSettings.CreateDefault();
        settings.SwitcherStyle = SwitcherStyle.FollowTheme;
        SettingsStore.Save(path, settings);
        Assert.Contains("\"switcherStyle\": \"FollowTheme\"", File.ReadAllText(path), StringComparison.Ordinal);
        var round = SettingsStore.Load(path);
        Assert.False(round.Migrated);
        Assert.Equal(SwitcherStyle.FollowTheme, round.Settings.SwitcherStyle);

        var invalid = Path.Combine(directory, "invalid.json");
        File.WriteAllText(invalid, """{ "version": 11, "switcherStyle": 7 }""");
        Assert.Equal(SwitcherStyle.Dark, SettingsStore.Load(invalid).Settings.SwitcherStyle);
    }

    [Fact]
    public void Switcher_style_labels_exist_in_both_languages()
    {
        foreach (var key in new[] { "settings.switcherStyle", "settings.switcherStyle.dark", "settings.switcherStyle.follow" })
        {
            Assert.False(string.IsNullOrWhiteSpace(StringTables.Chinese[key]));
            Assert.False(string.IsNullOrWhiteSpace(StringTables.English[key]));
        }
        Assert.Equal("切换窗口风格", StringTables.Chinese["settings.switcherStyle"]);
        Assert.Equal("深色（默认）", StringTables.Chinese["settings.switcherStyle.dark"]);
    }
}
