using System.Runtime.InteropServices;
using AltPane.Core.Actions;
using AltPane.Core.Filtering;
using AltPane.Core.Input;
using AltPane.Core.Interop;
using AltPane.Core.Layout;
using AltPane.Core.Localization;
using AltPane.Core.Models;
using AltPane.Core.Settings;
using Xunit;

namespace AltPane.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Round_trip_preserves_fields_and_normalizes_exclusions()
    {
        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var settings = AppSettings.CreateDefault();
        settings.CaptureWinTab = true;
        settings.Theme = AppTheme.Dark;
        settings.Language = AppLanguage.ZhCn;
        settings.Size = SwitcherSize.Large;
        settings.Placement = SwitcherPlacement.ActiveWindow;
        settings.StartWithWindows = true;
        settings.IncludeOtherVirtualDesktops = true;
        settings.ShowPreview = false;
        settings.Hotkey = new HotkeyChord { Modifiers = ModifierKeys.Control | ModifierKeys.Alt, VirtualKey = VirtualKeys.Space };
        settings.ExcludedProcesses = new List<string> { "Notepad.EXE", "  spotify ", "Spotify", "" };

        SettingsStore.Save(path, settings);
        Assert.False(File.Exists(path + ".tmp"));
        var loaded = SettingsStore.Load(path);
        Assert.False(loaded.RecoveredFromError);
        Assert.True(loaded.Settings.CaptureWinTab);
        Assert.Equal(AppTheme.Dark, loaded.Settings.Theme);
        Assert.Equal(AppLanguage.ZhCn, loaded.Settings.Language);
        Assert.Equal(SwitcherSize.Large, loaded.Settings.Size);
        Assert.Equal(SwitcherPlacement.ActiveWindow, loaded.Settings.Placement);
        Assert.False(loaded.Settings.ShowPreview);
        Assert.Equal(VirtualKeys.Space, loaded.Settings.Hotkey!.VirtualKey);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, loaded.Settings.Hotkey.Modifiers);
        Assert.Equal(new[] { "Notepad", "spotify" }, loaded.Settings.ExcludedProcesses);
    }

    [Fact]
    public void Missing_file_returns_alt_tab_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "missing.json");
        var loaded = SettingsStore.Load(path);
        Assert.False(loaded.RecoveredFromError);
        Assert.True(HotkeyLogic.IsValid(loaded.Settings.Hotkey));
        Assert.Equal(VirtualKeys.Tab, loaded.Settings.Hotkey!.VirtualKey);
        Assert.Equal(ModifierKeys.Alt, loaded.Settings.Hotkey.Modifiers);
        Assert.True(loaded.Settings.ShowPreview);
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_replaced_with_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");
        var loaded = SettingsStore.Load(path);
        Assert.True(loaded.RecoveredFromError);
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(ModifierKeys.Alt, loaded.Settings.Hotkey!.Modifiers);
    }

    [Fact]
    public void Invalid_hotkey_in_an_otherwise_valid_file_falls_back()
    {
        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "hotkey": { "modifiers": 0, "virtualKey": 9 }, "theme": "Dark" }""");
        var loaded = SettingsStore.Load(path);
        Assert.False(loaded.RecoveredFromError);
        Assert.Equal(ModifierKeys.Alt, loaded.Settings.Hotkey!.Modifiers);
        Assert.Equal(AppTheme.Dark, loaded.Settings.Theme);
    }
}

public class ProcessActionPolicyTests
{
    [Theory]
    [InlineData("explorer", 1000, false)]
    [InlineData("svchost.exe", 20, false)]
    [InlineData("csrss", 12, false)]
    [InlineData("notepad", 400, true)]
    [InlineData("ApplicationFrameHost", 500, false)]
    public void Protected_processes_cannot_be_killed(string name, int pid, bool allowed)
    {
        var decision = ProcessActionPolicy.Evaluate(name, pid);
        Assert.Equal(allowed, decision.Allowed);
    }

    [Fact]
    public void System_pids_are_protected() =>
        Assert.False(ProcessActionPolicy.Evaluate("anything", 4).Allowed);

    [Fact]
    public void Uwp_kill_targets_the_hosted_process()
    {
        Assert.Equal(80, ProcessActionPolicy.ResolveKillProcessId("ApplicationFrameHost", 10, 80));
        Assert.Equal("WeChat", ProcessActionPolicy.ResolveKillProcessName("ApplicationFrameHost", "WeChat.exe"));
        Assert.Equal(10, ProcessActionPolicy.ResolveKillProcessId("notepad", 10, 0));
    }
}

public class LocalizerTests
{
    [Fact]
    public void System_chinese_and_explicit_languages()
    {
        var loc = new Localizer();
        loc.Apply(AppLanguage.System, "zh-CN");
        Assert.Equal("设置", loc["tray.settings"]);
        loc.Apply(AppLanguage.System, "zh-TW");
        Assert.Equal("退出", loc["tray.exit"]);
        loc.Apply(AppLanguage.System, "en-US");
        Assert.Equal("Settings", loc["tray.settings"]);
        loc.Apply(AppLanguage.En, "zh-CN");
        Assert.Equal("Settings", loc["tray.settings"]);
        loc.Apply(AppLanguage.ZhCn, "en-US");
        Assert.Contains("松开", loc.Format("hint.gesture", "Alt"));
    }

    [Fact]
    public void Missing_keys_fall_back_then_echo()
    {
        var loc = new Localizer();
        loc.Apply(AppLanguage.ZhCn, "zh-CN");
        Assert.Equal("not.a.key", loc["not.a.key"]);
        Assert.False(string.IsNullOrWhiteSpace(loc["tray.exit"]));
    }
}

public class RectangleMathTests
{
    [Fact]
    public void Centers_and_clamps_to_the_work_area()
    {
        var area = new RectangleMath.RectI(100, 200, 800, 600);
        var placed = RectangleMath.Center(area, 200, 100);
        Assert.Equal(new RectangleMath.RectI(400, 450, 200, 100), placed);
        var huge = RectangleMath.Center(area, 5000, 5000);
        Assert.Equal(area.Width, huge.Width);
        Assert.Equal(area.X, huge.X);
    }

    [Fact]
    public void Fits_thumbnails_with_letterboxing()
    {
        var dest = new RectangleMath.RectI(10, 20, 800, 600);
        var fitted = RectangleMath.FitContain(dest, 1920, 1080);
        Assert.Equal(800, fitted.Width);
        Assert.Equal(450, fitted.Height);
        Assert.Equal(10, fitted.X);
        Assert.True(fitted.Y > dest.Y);
        Assert.Equal(dest, RectangleMath.FitContain(dest, 0, 0));
    }

    [Fact]
    public void Converts_dips_with_rounding()
    {
        var physical = RectangleMath.DipToPhysical(10, 20, 100, 50, 1.5, 1.5);
        Assert.Equal(new RectangleMath.RectI(15, 30, 150, 75), physical);
    }

    [Fact]
    public void Size_presets_grow()
    {
        var compact = SwitcherMetrics.GetSize(SwitcherSize.Compact);
        var large = SwitcherMetrics.GetSize(SwitcherSize.Large);
        Assert.True(large.Width > compact.Width);
        Assert.True(large.Height > compact.Height);
    }
}

public class HotkeyFormatTests
{
    [Fact]
    public void Formats_common_chords()
    {
        Assert.Equal("Alt + Tab", HotkeyLogic.Format(HotkeyChord.AltTab()));
        Assert.Equal("Ctrl + Alt + Space", HotkeyLogic.Format(new HotkeyChord
        {
            Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
            VirtualKey = VirtualKeys.Space
        }));
        Assert.Equal("Win + Tab", HotkeyLogic.Format(new HotkeyChord
        {
            Modifiers = ModifierKeys.Win,
            VirtualKey = VirtualKeys.Tab
        }));
    }

    [Fact]
    public void Rejects_modifier_only_chords()
    {
        Assert.False(HotkeyLogic.IsValid(new HotkeyChord { Modifiers = ModifierKeys.None, VirtualKey = VirtualKeys.F }));
        Assert.False(HotkeyLogic.IsValid(new HotkeyChord { Modifiers = ModifierKeys.Alt, VirtualKey = VirtualKeys.Menu }));
        Assert.True(HotkeyLogic.IsValid(HotkeyChord.AltTab()));
    }

    [Theory]
    [InlineData(@"C:\Tools\AltPane.exe", @"C:\Tools\AltPane.exe")]
    [InlineData(@"C:\Program Files\AltPane.exe", "\"C:\\Program Files\\AltPane.exe\"")]
    public void Quotes_paths_that_contain_spaces(string path, string expected) =>
        Assert.Equal(expected, ExecutablePath.Quote(path));
}

public class NativeLayoutTests
{
    [Fact]
    public void Thumbnail_properties_match_the_windows_x64_size() =>
        Assert.Equal(48, Marshal.SizeOf<DwmThumbnailProperties>());

    [Fact]
    public void SendInput_struct_is_40_bytes_on_x64()
    {
        if (Environment.Is64BitProcess)
            Assert.Equal(40, Marshal.SizeOf<NativeInput>());
        Assert.Equal(16, Marshal.SizeOf<NativeRect>());
    }
}
