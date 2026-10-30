using AltPane.Core.Input;
using AltPane.Core.Models;
using AltPane.Core.Settings;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class FrequentAppsTests
{
    static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    static AppFocusRecord App(string key, string name, string path, params FocusSample[] samples) =>
        new(key, name, path, samples);

    static FocusSample At(DateTime utc, long milliseconds, bool solicited = true) =>
        new(utc.Ticks, milliseconds, solicited);

    static FrequentApp? Find(IReadOnlyList<FrequentApp> ranked, string key)
    {
        foreach (var app in ranked)
        {
            if (string.Equals(app.Key, key, StringComparison.OrdinalIgnoreCase)) return app;
        }
        return null;
    }

    [Fact]
    public void Ranking_uses_real_focus_inside_the_window_and_drops_the_rest()
    {
        var window = TimeSpan.FromDays(7);
        var inside = Now - TimeSpan.FromDays(6);
        var edge = Now - window;
        var outside = edge.AddTicks(-1);
        var records = new[]
        {
            App("code", "Code", @"C:\code.exe",
                At(inside, 4_000),
                At(edge, 1_000),
                At(outside, 9_000_000),
                At(inside, 50_000, solicited: false)),
            App("chrome", "Chrome", @"C:\chrome.exe", At(Now - TimeSpan.FromDays(1), 3_000)),
            App("slack", "Slack", @"C:\slack.exe", At(inside, 100, solicited: false)),
            App("altpane", "AltPane", @"C:\AltPane.exe", At(inside, 9_000_000)),
            App("AltPane.exe", "AltPane", @"C:\AltPane.exe", At(inside, 9_000_000)),
            App("notes", "Notes", @"C:\notes.exe", At(Now.AddHours(1), 8_000))
        };

        var ranked = FrequentApps.Rank(records, Now, window, 8, null, null, null, null);

        Assert.Equal(new[] { "code", "chrome" }, ranked.Select(app => app.Key).ToArray());
        Assert.DoesNotContain(ranked, app => FrequentApps.IsSelf(app.Key));
        Assert.Equal(5_000, Find(ranked, "code")!.FocusMilliseconds);
        Assert.Equal("F1", ranked[0].Shortcut);
        Assert.Equal("F2", ranked[1].Shortcut);
    }

    [Fact]
    public void Pins_come_first_hides_and_the_demote_list_are_excluded()
    {
        var inside = Now - TimeSpan.FromDays(1);
        var records = new[]
        {
            App("chrome", "Chrome", @"C:\chrome.exe", At(inside, 9_000)),
            App("code", "Visual Studio Code", @"C:\code.exe", At(inside, 1_000)),
            App("notes", "Notes", @"C:\notes.exe", At(inside, 8_000)),
            App("slack", "Slack", @"C:\slack.exe", At(inside, 7_000)),
            App("corplink", "飞连", @"C:\CorpLink.exe", At(inside, 50_000)),
            App("outlook", "Outlook", @"C:\outlook.exe", At(inside, 6_000))
        };
        var ranked = FrequentApps.Rank(
            records,
            Now,
            TimeSpan.FromDays(7),
            8,
            pinned: new[] { "notes", "Code.exe" },
            hidden: new[] { "slack" },
            demoted: new[] { "outlook" },
            open: new[]
            {
                new OpenAppWindow("code", 10, 4),
                new OpenAppWindow("code", 11, 1),
                new OpenAppWindow("notes", 20, int.MaxValue)
            });

        Assert.Equal(new[] { "notes", "code", "chrome" }, ranked.Select(app => app.Key).ToArray());
        Assert.True(ranked[0].Pinned);
        Assert.True(ranked[1].Pinned);
        Assert.False(ranked[2].Pinned);
        Assert.Equal("F1", ranked[0].Shortcut);
        Assert.Equal("F3", ranked[2].Shortcut);
        Assert.Equal(11, Find(ranked, "code")!.WindowHandle);
        Assert.True(Find(ranked, "code")!.Running);
        Assert.True(Find(ranked, "notes")!.Running);
        Assert.Equal(20, Find(ranked, "notes")!.WindowHandle);
        Assert.False(Find(ranked, "chrome")!.Running);
    }

    [Fact]
    public void A_sidebar_pin_keeps_a_demoted_app_and_the_count_stays_inside_six_to_eight()
    {
        var inside = Now - TimeSpan.FromHours(2);
        var records = new List<AppFocusRecord>();
        for (var i = 0; i < 10; i++)
            records.Add(App("app" + i, "App " + i, @"C:\app" + i + ".exe", At(inside, 1_000 + i)));
        records.Add(App("corplink", "飞连", @"C:\CorpLink.exe", At(inside, 10)));

        var many = FrequentApps.Rank(records, Now, TimeSpan.FromDays(7), 100, new[] { "corplink" }, null, null, null);
        Assert.Equal(8, many.Count);
        Assert.Equal("corplink", many[0].Key);
        Assert.True(many[0].Pinned);
        Assert.Equal("F8", many[7].Shortcut);

        var six = FrequentApps.Rank(records, Now, TimeSpan.FromDays(7), 6, null, null, new[] { "corplink" }, null);
        Assert.Equal(6, six.Count);
        Assert.DoesNotContain(six, app => app.Key == "corplink");
        Assert.Equal("app9", six[0].Key);
    }

    [Fact]
    public void Equal_focus_prefers_the_more_recent_sample_then_the_name()
    {
        var older = Now - TimeSpan.FromDays(2);
        var newer = Now - TimeSpan.FromHours(3);
        var records = new[]
        {
            App("zeta", "Zeta", @"C:\z.exe", At(older, 1_000)),
            App("alpha", "Alpha", @"C:\a.exe", At(older, 1_000)),
            App("mid", "Mid", @"C:\m.exe", At(newer, 500), At(older, 500))
        };
        var ranked = FrequentApps.Rank(records, Now, TimeSpan.FromDays(7), 8, null, null, null, null);
        Assert.Equal(new[] { "mid", "alpha", "zeta" }, ranked.Select(app => app.Key).ToArray());
    }

    [Fact]
    public void The_open_order_stays_put_when_running_state_changes()
    {
        var ranked = FrequentApps.Rank(
            new[] { App("code", "Code", @"C:\code.exe", At(Now, 2_000)) },
            Now,
            TimeSpan.FromDays(7),
            8,
            null,
            null,
            null,
            null);
        Assert.False(ranked[0].Running);
        var live = FrequentApps.WithRunning(ranked, new[] { new OpenAppWindow("code", 42, 0) });
        Assert.Equal("code", live[0].Key);
        Assert.Equal("F1", live[0].Shortcut);
        Assert.True(live[0].Running);
        Assert.Equal(42, live[0].WindowHandle);
        Assert.False(ranked[0].Running);
    }

    [Fact]
    public void A_shortcut_is_kept_ahead_of_an_exe()
    {
        Assert.Equal(@"C:\Apps\Code.lnk", FrequentApps.PreferLaunchPath(@"C:\code.exe", @"C:\Apps\Code.lnk"));
        Assert.Equal(@"C:\Apps\Code.lnk", FrequentApps.PreferLaunchPath(@"C:\Apps\Code.lnk", @"C:\code.exe"));
        Assert.Equal("Code", FrequentApps.ShortName(@"C:\Apps\Code.exe", "code"));
    }

    [Fact]
    public void Usage_stores_real_focus_and_the_launch_target()
    {
        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "usage.json");
        var table = new AppUsageTable();
        table.NoteFocus("code", TimeSpan.FromMinutes(20), Now, "Code", @"C:\code.exe");
        table.RememberLaunch("code", "Visual Studio Code", @"C:\Apps\Code.lnk");
        table.NoteSeen("corplink", Now);
        table.Save(path);
        var loaded = new AppUsageTable();
        loaded.Load(path);
        var ranked = FrequentApps.Rank(loaded.Export(), Now, TimeSpan.FromDays(7), 8, null, null, null, null);
        var code = Assert.Single(ranked);
        Assert.Equal("code", code.Key);
        Assert.Equal("Visual Studio Code", code.Name);
        Assert.Equal(@"C:\Apps\Code.lnk", code.LaunchPath);
        Assert.Equal(20 * 60 * 1000, code.FocusMilliseconds);

        var legacy = Path.Combine(directory, "legacy.json");
        File.WriteAllText(legacy, """
        { "apps": [ { "key": "chrome", "focusMinutes": 15, "above": true, "firstSeenUtcTicks": 1, "updatedUtcTicks": 638000000000000000 } ] }
        """);
        var old = new AppUsageTable();
        old.Load(legacy);
        var seeded = FrequentApps.Rank(
            old.Export(),
            new DateTime(638000000000000000, DateTimeKind.Utc),
            TimeSpan.FromDays(7),
            8,
            null,
            null,
            null,
            null);
        Assert.Equal("chrome", Assert.Single(seeded).Key);
        Assert.True(seeded[0].FocusMilliseconds > 0);
    }

    [Fact]
    public void Sidebar_lists_keep_pin_order_and_a_hide_removes_the_pin()
    {
        var settings = AppSettings.CreateDefault();
        SidebarLists.Pin(settings, "Code.exe");
        SidebarLists.Pin(settings, "notes");
        SidebarLists.Pin(settings, "code");
        SidebarLists.Pin(settings, "AltPane");
        Assert.Equal(new[] { "Code", "notes" }, settings.SidebarPinnedApps);
        SidebarLists.Hide(settings, "notes");
        SidebarLists.Unpin(settings, "code");
        Assert.Empty(settings.SidebarPinnedApps);
        Assert.Equal(new[] { "notes" }, settings.SidebarHiddenApps);
        SidebarLists.Sanitize(settings);
        Assert.Equal(8, settings.FrequentSidebarCount);
    }

    [Fact]
    public void Schema_8_turns_the_sidebar_on_and_keeps_an_explicit_choice()
    {
        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var oldPath = Path.Combine(directory, "old.json");
        File.WriteAllText(oldPath, """{ "version": 7, "sortMode": "Recent", "demotedApps": [ "Outlook" ] }""");
        var migrated = SettingsStore.Load(oldPath);
        Assert.True(migrated.Migrated);
        Assert.Equal(AppSettings.CurrentSchema, migrated.Settings.Version);
        Assert.True(migrated.Settings.ShowFrequentSidebar);
        Assert.Equal(8, migrated.Settings.FrequentSidebarCount);
        Assert.Empty(migrated.Settings.SidebarPinnedApps);
        Assert.Empty(migrated.Settings.SidebarHiddenApps);
        Assert.Equal(LoadSortMode.Recent, migrated.Settings.SortMode);
        Assert.Equal(new[] { "Outlook" }, migrated.Settings.DemotedApps);

        var chosen = Path.Combine(directory, "chosen.json");
        File.WriteAllText(chosen, """
        { "version": 8, "showFrequentSidebar": false, "frequentSidebarCount": 6, "sidebarPinnedApps": [ "Code.exe" ], "sidebarHiddenApps": [ "Slack" ] }
        """);
        var kept = SettingsStore.Load(chosen);
        Assert.False(kept.Settings.ShowFrequentSidebar);
        Assert.Equal(6, kept.Settings.FrequentSidebarCount);
        Assert.Equal(new[] { "Code" }, kept.Settings.SidebarPinnedApps);
        Assert.Equal(new[] { "Slack" }, kept.Settings.SidebarHiddenApps);

        var wide = Path.Combine(directory, "wide.json");
        File.WriteAllText(wide, """{ "version": 8, "frequentSidebarCount": 20 }""");
        Assert.Equal(8, SettingsStore.Load(wide).Settings.FrequentSidebarCount);
    }
}

public class SidebarShortcutTests
{
    static HookInput Open(int vk, ModifierState mods, bool search = false, ModifierKeys trigger = ModifierKeys.Alt) => new()
    {
        SwitcherOpen = true,
        SearchMode = search,
        GestureLatched = !search,
        LatchedTrigger = search ? ModifierKeys.None : trigger,
        Hotkey = HotkeyChord.AltTab(),
        VirtualKey = vk,
        IsKeyDown = true,
        Modifiers = mods
    };

    [Theory]
    [InlineData(VirtualKeys.F1, 0)]
    [InlineData(VirtualKeys.F8, 7)]
    public void Alt_function_keys_activate_sidebar_slots(int key, int index)
    {
        Assert.Equal(index, SidebarShortcuts.Index(key));
        Assert.Equal("F" + (index + 1), SidebarShortcuts.Label(index));
        var held = HookInterpreter.Interpret(Open(key, ModifierState.AltOnly));
        Assert.Equal(HookCommand.ActivateSidebar, held.Command);
        Assert.Equal(index, held.Delta);
        var plain = HookInterpreter.Interpret(Open(key, ModifierState.None, search: true));
        Assert.Equal(HookCommand.ActivateSidebar, plain.Command);
        Assert.Equal(index, plain.Delta);
    }

    [Fact]
    public void Sidebar_keys_do_not_take_ctrl_chords_or_a_closed_switcher()
    {
        var alt = new ModifierState(true, true, false, false);
        var row = HookInterpreter.Interpret(Open(0x31, alt));
        Assert.Equal(HookCommand.ActivateRow, row.Command);
        Assert.Equal(0, row.Delta);

        var searchRow = HookInterpreter.Interpret(Open(0x30, new ModifierState(false, true, false, false), search: true));
        Assert.Equal(HookCommand.ActivateRow, searchRow.Command);
        Assert.Equal(9, searchRow.Delta);

        var reveal = HookInterpreter.Interpret(Open(VirtualKeys.Return, alt));
        Assert.Equal(HookCommand.RevealPath, reveal.Command);
        var admin = HookInterpreter.Interpret(Open(VirtualKeys.Return, new ModifierState(true, true, true, false)));
        Assert.Equal(HookCommand.AdminLaunch, admin.Command);
        var copy = HookInterpreter.Interpret(Open(VirtualKeys.C, alt));
        Assert.Equal(HookCommand.CopyPath, copy.Command);

        var ctrlF1 = HookInterpreter.Interpret(Open(VirtualKeys.F1, alt));
        Assert.NotEqual(HookCommand.ActivateSidebar, ctrlF1.Command);
        var shiftF1 = HookInterpreter.Interpret(Open(VirtualKeys.F1, ModifierState.AltShift));
        Assert.NotEqual(HookCommand.ActivateSidebar, shiftF1.Command);

        var closed = new HookInput
        {
            Hotkey = HotkeyChord.AltTab(),
            VirtualKey = VirtualKeys.F4,
            IsKeyDown = true,
            Modifiers = ModifierState.AltOnly
        };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(closed).Command);

        var win = HookInterpreter.Interpret(Open(VirtualKeys.F1, new ModifierState(false, false, false, true), trigger: ModifierKeys.Win));
        Assert.Equal(HookCommand.ActivateSidebar, win.Command);
        var transition = GestureTransitions.Apply(
            GestureSnapshot.Idle with { SwitcherOpen = true, GestureLatched = true, LatchedTrigger = ModifierKeys.Win },
            win,
            null,
            true,
            ModifierKeys.Win);
        var op = Assert.IsType<UiOp.ActivateSidebar>(transition.Op);
        Assert.Equal(0, op.Index);
    }
}
