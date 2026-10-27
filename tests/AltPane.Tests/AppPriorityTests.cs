using AltPane.Core.Models;
using AltPane.Core.Settings;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class AppPriorityTests
{
    static readonly DateTime Start = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    static WindowEntry Win(long handle, int z, string app) => new()
    {
        Handle = handle,
        Title = app,
        ClassName = "C",
        ProcessName = app,
        DisplayProcessName = app,
        ProcessId = (int)handle,
        KillProcessId = (int)handle,
        KillProcessName = app,
        OriginalIndex = z
    };

    [Fact]
    public void A_foreground_change_counts_only_after_recent_input_or_an_AltPane_activation_and_a_one_second_dwell()
    {
        Assert.True(UserActivation.IsUserCaused(1000, 600, activatedBySwitcher: false));
        Assert.False(UserActivation.IsUserCaused(1000, 400, activatedBySwitcher: false));
        Assert.True(UserActivation.IsUserCaused(1000, 0, activatedBySwitcher: true));
        Assert.True(UserActivation.IsUserCaused(100, uint.MaxValue - 50, activatedBySwitcher: false));

        var ignored = new ForegroundDwell();
        Assert.Equal(0L, ignored.OnForeground(5, 0, userCaused: false).UserActivated);
        Assert.Equal(0L, ignored.Poll(999).Unsolicited);
        Assert.Equal(5L, ignored.Poll(1000).Unsolicited);
        Assert.Equal(0L, ignored.Poll(2500).UserActivated);

        var brief = new ForegroundDwell();
        Assert.Equal(0L, brief.OnForeground(7, 0, userCaused: true).UserActivated);
        var leftEarly = brief.OnForeground(8, 400, userCaused: true);
        Assert.Equal(0L, leftEarly.UserActivated);
        Assert.Equal(0L, leftEarly.FocusMilliseconds);
        Assert.Equal(8L, brief.Poll(1400).UserActivated);

        var held = new ForegroundDwell();
        held.OnForeground(7, 0, userCaused: true);
        Assert.Equal(7L, held.Poll(1000).UserActivated);
        var left = held.OnForeground(9, 2500, userCaused: true);
        Assert.Equal(0L, left.UserActivated);
        Assert.Equal(7L, left.FocusEnded);
        Assert.Equal(2500L, left.FocusMilliseconds);
    }

    [Fact]
    public void Focus_time_enters_at_the_threshold_and_leaves_below_eighty_percent()
    {
        var table = new AppUsageTable();
        table.NoteFocus("code", TimeSpan.FromMinutes(10), Start);
        Assert.Equal(UsageTier.Frequent, Tier(table, "code", Start));
        Assert.Equal(UsageTier.Frequent, Tier(table, "code", Start.AddDays(2)));
        Assert.Equal(UsageTier.Infrequent, Tier(table, "code", Start.AddDays(3)));

        var below = new AppUsageTable();
        below.NoteFocus("notes", TimeSpan.FromMinutes(9), Start);
        Assert.Equal(UsageTier.Frequent, Tier(below, "notes", Start.AddHours(2)));
        Assert.Equal(UsageTier.Infrequent, Tier(below, "notes", Start.AddDays(2)));

        var crossed = new AppUsageTable();
        crossed.NoteFocus("editor", TimeSpan.FromMinutes(11), Start);
        Assert.Equal(UsageTier.Frequent, Tier(crossed, "editor", Start));
    }

    [Fact]
    public void Focus_minutes_halve_about_every_seven_days()
    {
        var table = new AppUsageTable();
        table.NoteFocus("code", TimeSpan.FromMinutes(20), Start);
        Assert.Equal(UsageTier.Frequent, Tier(table, "code", Start.AddDays(7)));
        Assert.Equal(UsageTier.Infrequent, Tier(table, "code", Start.AddDays(14)));
    }

    [Fact]
    public void The_first_day_keeps_unknown_apps_in_the_main_tier_and_a_built_in_vpn_does_not()
    {
        var table = new AppUsageTable();
        table.NoteSeen("notes", Start);
        table.NoteFocus("notes", TimeSpan.FromMinutes(1), Start);
        Assert.Equal(UsageTier.Frequent, Tier(table, "notes", Start.AddHours(6)));
        Assert.Equal(UsageTier.Infrequent, Tier(table, "notes", Start.AddDays(2)));

        Assert.Equal(UsageTier.Frequent, table.Snapshot(null, null, Start, 10).Tier(Win(1, 0, "brand-new")));
        Assert.Equal(UsageTier.Infrequent, table.Snapshot(null, null, Start, 10).Tier(Win(2, 0, "CorpLink.exe")));
        Assert.True(LowPriorityApps.Contains("Feilian.exe"));
        Assert.True(LowPriorityApps.Contains("corplink-helper"));
    }

    [Fact]
    public void A_pin_overrides_demotion_and_the_short_tier_and_does_not_jump_ahead_of_recent_use()
    {
        var table = new AppUsageTable();
        table.NoteFocus("corplink", TimeSpan.FromMinutes(5), Start);
        var demoted = table.Snapshot(null, new[] { "code" }, Start.AddDays(3), 10);
        Assert.Equal(UsageTier.Infrequent, demoted.Tier(Win(1, 0, "CorpLink")));
        Assert.Equal(UsageTier.Infrequent, demoted.Tier(Win(2, 0, "Code")));

        var pinned = table.Snapshot(new[] { "CorpLink.exe" }, new[] { "code" }, Start.AddDays(3), 10);
        Assert.Equal(UsageTier.Pinned, pinned.Tier(Win(1, 0, "corplink")));
        Assert.False(pinned.IsShort(Win(1, 0, "corplink")));

        var code = Win(1, 4, "Code");
        var chrome = Win(2, 0, "Chrome");
        var notes = Win(3, 1, "Notes");
        var mru = new ActivationMru();
        mru.Note(code.Handle);
        mru.Note(chrome.Handle);
        mru.Note(notes.Handle);
        UsageTier TierOf(WindowEntry entry) => entry.ProcessName == "Code" ? UsageTier.Pinned : UsageTier.Frequent;
        var ordered = StableGroupedOrder.Arrange(
            new[] { code, chrome, notes },
            foregroundHandle: notes.Handle,
            mru.Rank,
            TierOf,
            entry => TierOf(entry) == UsageTier.Infrequent);
        Assert.Equal(new[] { chrome.Handle, notes.Handle, code.Handle }, ordered.Select(window => window.Handle));
    }

    [Fact]
    public void A_demoted_app_never_takes_the_first_row_while_a_main_window_exists_and_search_still_finds_it()
    {
        var code = Win(1, 3, "Code");
        var vpn = Win(2, 0, "CorpLink");
        var mru = new ActivationMru();
        mru.Note(code.Handle);
        mru.Note(vpn.Handle);
        UsageTier TierOf(WindowEntry entry) => entry.ProcessName == "CorpLink" ? UsageTier.Infrequent : UsageTier.Frequent;
        var ordered = StableGroupedOrder.Arrange(
            new[] { vpn, code },
            foregroundHandle: vpn.Handle,
            mru.Rank,
            TierOf,
            entry => TierOf(entry) == UsageTier.Infrequent);
        Assert.Equal(code.Handle, ordered[0].Handle);
        Assert.Equal(vpn.Handle, ordered[1].Handle);

        var sections = StableGroupedOrder.Sections(ordered, TierOf, "不常用");
        Assert.Equal("不常用  ·  CorpLink", sections[1]);

        var session = new SwitcherSession(new[] { vpn, code });
        session.SetOrderContext(vpn.Handle, mru.Rank, TierOf, entry => TierOf(entry) == UsageTier.Infrequent);
        session.SetSortMode(LoadSortMode.FocusTime);
        session.SetQuery("corp");
        Assert.Equal(vpn.Handle, session.Filtered[0].Handle);
        Assert.NotEqual(session.IndexForHandle(code.Handle), session.IndexForHandle(vpn.Handle));
    }

    [Fact]
    public void Usage_round_trips_and_the_focus_time_sort_name_replaces_stable_grouped()
    {
        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var usagePath = Path.Combine(directory, "usage.json");
        var table = new AppUsageTable();
        table.NoteFocus("code", TimeSpan.FromMinutes(30), Start);
        table.Save(usagePath);
        var loaded = new AppUsageTable();
        loaded.Load(usagePath);
        Assert.Equal(UsageTier.Frequent, Tier(loaded, "code", Start));
        Assert.Contains("focusMinutes", File.ReadAllText(usagePath), StringComparison.OrdinalIgnoreCase);

        var settingsPath = Path.Combine(directory, "settings.json");
        SettingsStore.Save(settingsPath, AppSettings.CreateDefault());
        var saved = File.ReadAllText(settingsPath);
        Assert.Contains("FocusTime", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("StableGrouped", saved, StringComparison.Ordinal);
        Assert.Contains("focusThresholdMinutes", saved, StringComparison.Ordinal);

        var clamped = SettingsStore.Load(Write(directory, """{ "version": 7, "focusThresholdMinutes": 0 }"""));
        Assert.Equal(10, clamped.Settings.FocusThresholdMinutes);
        Assert.Equal(LoadSortMode.FocusTime, clamped.Settings.SortMode);
    }

    static UsageTier Tier(AppUsageTable table, string process, DateTime utc) =>
        table.Snapshot(null, null, utc, AppUsageTable.DefaultThresholdMinutes).Tier(Win(1, 0, process));

    static string Write(string directory, string json)
    {
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }
}
