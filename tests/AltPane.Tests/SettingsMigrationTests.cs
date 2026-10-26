using AltPane.Core.Input;
using AltPane.Core.Models;
using AltPane.Core.Settings;
using Xunit;

namespace AltPane.Tests;

public class SettingsMigrationTests
{
    [Fact]
    public void A_v1_file_without_sticky_mode_receives_the_later_defaults_and_keeps_real_choices()
    {
        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {
          "version": 1,
          "hotkey": { "modifiers": 3, "virtualKey": 32 },
          "captureWinTab": false,
          "theme": "Dark",
          "showPreview": false,
          "startWithWindows": true,
          "excludedProcesses": [ "Spotify" ],
          "welcomeShown": true
        }
        """);

        var loaded = SettingsStore.Load(path);
        Assert.True(loaded.Migrated);
        Assert.False(loaded.RecoveredFromError);
        Assert.Equal(AppSettings.CurrentSchema, loaded.Settings.Version);
        Assert.True(loaded.Settings.CaptureWinTab);
        Assert.True(loaded.Settings.StickySwitcher);
        Assert.True(loaded.Settings.ShowLoad);
        Assert.True(loaded.Settings.LaunchApps);
        Assert.True(loaded.Settings.ShowOtherMonitorHints);
        Assert.True(loaded.Settings.ShowMonitorBadges);
        Assert.True(loaded.Settings.MoveToSwitcherMonitor);
        Assert.True(loaded.Settings.UseEverything);
        Assert.False(loaded.Settings.IndexPath);
        Assert.False(loaded.Settings.QuickTapSwitches);
        Assert.False(loaded.Settings.ShowPreview);
        Assert.True(loaded.Settings.StartWithWindows);
        Assert.True(loaded.Settings.WelcomeShown);
        Assert.Equal(AppTheme.Dark, loaded.Settings.Theme);
        Assert.Equal(new[] { "Spotify" }, loaded.Settings.ExcludedProcesses);
        Assert.Equal(VirtualKeys.Space, loaded.Settings.Hotkey!.VirtualKey);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, loaded.Settings.Hotkey.Modifiers);

        var captured = new HookInput
        {
            Hotkey = HotkeyChord.AltTab(),
            CaptureWinTab = loaded.Settings.CaptureWinTab,
            VirtualKey = VirtualKeys.Tab,
            IsKeyDown = true,
            Modifiers = new ModifierState(false, false, false, true)
        };
        Assert.Equal(HookCommand.Cycle, HookInterpreter.Interpret(captured).Command);
    }

    [Fact]
    public void A_v2_shaped_file_keeps_an_explicit_win_tab_and_sticky_choice()
    {
        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {
          "version": 1,
          "captureWinTab": false,
          "stickySwitcher": false,
          "theme": "Light",
          "launchApps": false,
          "showMonitorBadges": false
        }
        """);

        var loaded = SettingsStore.Load(path);
        Assert.False(loaded.Settings.CaptureWinTab);
        Assert.False(loaded.Settings.StickySwitcher);
        Assert.False(loaded.Settings.LaunchApps);
        Assert.False(loaded.Settings.ShowMonitorBadges);
        Assert.Equal(AppTheme.Light, loaded.Settings.Theme);
        Assert.True(loaded.Settings.MoveToSwitcherMonitor);
        Assert.True(loaded.Settings.UseEverything);
        Assert.Equal(AppSettings.CurrentSchema, loaded.Settings.Version);

        SettingsStore.Save(path, loaded.Settings);
        var again = SettingsStore.Load(path);
        Assert.False(again.Migrated);
        Assert.False(again.Settings.CaptureWinTab);
        Assert.False(again.Settings.StickySwitcher);
    }

    [Fact]
    public void Executable_symlinks_resolve_to_the_real_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var real = Path.Combine(directory, "AltPane.exe");
        // A distinct name: on a case-insensitive volume, altpane.exe is the same file as AltPane.exe.
        var alias = Path.Combine(directory, "altpane-alias.exe");
        File.WriteAllText(real, "exe");
        Assert.Equal("", AppPaths.ResolveExecutable("  "));
        try
        {
            File.CreateSymbolicLink(alias, real);
        }
        catch (Exception ex) when (SymlinkNotPermitted(ex))
        {
            return;
        }
        var expected = Path.GetFullPath(real);
        Assert.Equal(expected, AppPaths.ResolveExecutable(alias), ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(expected, AppPaths.ResolveExecutable(real), ignoreCase: OperatingSystem.IsWindows());
    }

    static bool SymlinkNotPermitted(Exception ex)
    {
        if (ex is UnauthorizedAccessException or PlatformNotSupportedException) return true;
        if (ex is not IOException) return false;
        // ERROR_PRIVILEGE_NOT_HELD is 1314, surfaced as 0x80070522.
        if (ex.HResult == unchecked((int)0x80070522)) return true;
        return ex.Message.Contains("privilege", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("not permitted", StringComparison.OrdinalIgnoreCase);
    }
}
