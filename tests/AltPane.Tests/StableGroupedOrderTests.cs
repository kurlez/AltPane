using AltPane.Core.Load;
using AltPane.Core.Models;
using AltPane.Core.Settings;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class StableGroupedOrderTests
{
    static WindowEntry Win(long handle, int z, string app) => new()
    {
        Handle = handle,
        Title = app + " " + handle,
        ClassName = "C",
        ProcessName = app,
        DisplayProcessName = app,
        ProcessId = (int)handle,
        KillProcessId = (int)handle,
        KillProcessName = app,
        OriginalIndex = z
    };

    [Fact]
    public void Previous_window_is_first_and_the_foreground_window_stays_in_its_group()
    {
        var chromeNow = Win(1, 0, "Chrome");
        var chromeOld = Win(2, 4, "Chrome");
        var code = Win(3, 2, "Code");
        var notes = Win(4, 3, "Notes");
        var mru = new ActivationMru();
        mru.Note(notes.Handle);
        mru.Note(chromeOld.Handle);
        mru.Note(code.Handle);
        mru.Note(chromeNow.Handle);

        var ordered = StableGroupedOrder.Arrange(
            new[] { chromeNow, notes, code, chromeOld },
            foregroundHandle: chromeNow.Handle,
            mru.Rank);

        Assert.Equal(code.Handle, ordered[0].Handle);
        Assert.NotEqual(chromeNow.Handle, ordered[0].Handle);
        Assert.Contains(ordered, window => window.Handle == chromeNow.Handle);
        var withoutPinned = ordered.Skip(1).ToList();
        Assert.Equal(new[] { "Chrome", "Chrome", "Notes" }, withoutPinned.Select(window => window.DisplayProcessName));
        Assert.Equal(chromeNow.Handle, withoutPinned[0].Handle);
        Assert.Equal(chromeOld.Handle, withoutPinned[1].Handle);
    }

    [Fact]
    public void Groups_follow_pinyin_and_latin_initials_with_digits_and_symbols_after()
    {
        var wechat = Win(1, 0, "微信");
        var chrome = Win(2, 1, "Chrome");
        var notes = Win(3, 2, "记事本");
        var seven = Win(4, 3, "7-Zip");
        var marks = Win(5, 4, "***");
        var code = Win(6, 5, "Code");

        var grouped = StableGroupedOrder.Group(
            new[] { wechat, seven, notes, marks, chrome, code },
            foregroundHandle: 0,
            mruRank: null);

        Assert.Equal(
            new[] { "Chrome", "Code", "记事本", "微信", "***", "7-Zip" },
            grouped.Select(window => window.DisplayProcessName));
    }

    [Fact]
    public void Within_a_group_most_recent_use_beats_z_order()
    {
        var older = Win(1, 0, "Chrome");
        var newer = Win(2, 9, "Chrome");
        var foreground = Win(3, 1, "Chrome");
        var mru = new ActivationMru();
        mru.Note(older.Handle);
        mru.Note(newer.Handle);

        var grouped = StableGroupedOrder.Group(
            new[] { older, newer, foreground },
            foregroundHandle: foreground.Handle,
            mru.Rank);

        Assert.Equal(new[] { foreground.Handle, newer.Handle, older.Handle }, grouped.Select(window => window.Handle));
    }

    [Fact]
    public void Without_mru_a_group_follows_z_order()
    {
        var front = Win(1, 0, "Chrome");
        var mid = Win(2, 2, "Chrome");
        var back = Win(3, 5, "Chrome");
        var other = Win(4, 1, "Notes");

        var grouped = StableGroupedOrder.Group(
            new[] { back, other, mid, front },
            foregroundHandle: 0,
            mruRank: null);
        var arranged = StableGroupedOrder.Arrange(
            new[] { back, other, mid, front },
            foregroundHandle: front.Handle,
            mruRank: null);

        Assert.Equal(new[] { front.Handle, mid.Handle, back.Handle, other.Handle }, grouped.Select(window => window.Handle));
        Assert.Equal(other.Handle, arranged[0].Handle);
        Assert.Equal(new[] { front.Handle, mid.Handle, back.Handle }, arranged.Skip(1).Select(window => window.Handle));
    }

    [Fact]
    public void Recent_is_pure_mru_with_the_foreground_window_first()
    {
        var current = Win(1, 4, "Chrome");
        var previous = Win(2, 0, "Notes");
        var older = Win(3, 1, "Code");
        var mru = new ActivationMru();
        mru.Note(older.Handle);
        mru.Note(previous.Handle);
        mru.Note(current.Handle);

        var ordered = StableGroupedOrder.ByRecent(
            new[] { older, current, previous },
            foregroundHandle: current.Handle,
            mru.Rank);

        Assert.Equal(new[] { current.Handle, previous.Handle, older.Handle }, ordered.Select(window => window.Handle));
    }

    [Fact]
    public void Recent_without_mru_keeps_z_order_and_input_order_when_z_order_ties()
    {
        var first = Win(1, 0, "A");
        var second = Win(2, 0, "B");
        var third = Win(3, 2, "C");
        var ordered = StableGroupedOrder.ByRecent(new[] { first, second, third }, 0, null);
        Assert.Equal(new[] { first.Handle, second.Handle, third.Handle }, ordered.Select(window => window.Handle));
    }

    [Fact]
    public void Session_freezes_grouped_order_and_a_badge_selects_the_group_start()
    {
        var current = Win(1, 0, "Chrome");
        var olderChrome = Win(2, 3, "Chrome");
        var code = Win(3, 1, "Code");
        var wechat = Win(4, 2, "微信");
        var mru = new ActivationMru();
        mru.Note(wechat.Handle);
        mru.Note(olderChrome.Handle);
        mru.Note(code.Handle);
        mru.Note(current.Handle);

        var session = new SwitcherSession(new[] { current, wechat, code, olderChrome });
        session.SetOrderContext(current.Handle, mru.Rank);
        session.SetSortMode(LoadSortMode.StableGrouped);

        Assert.Equal(code.Handle, session.Filtered[0].Handle);
        Assert.Equal(new[] { "Code", "Chrome", "Chrome", "微信" }, session.Filtered.Select(window => window.DisplayProcessName));
        Assert.Equal("co", session.IndexForHandle(code.Handle));
        Assert.Equal("ch", session.IndexForHandle(current.Handle));
        Assert.Equal("ch2", session.IndexForHandle(olderChrome.Handle));
        Assert.Equal("wx", session.IndexForHandle(wechat.Handle));

        var sections = StableGroupedOrder.Sections(session.Filtered);
        Assert.Equal(new[] { "Code", "Chrome", "", "微信" }, sections);

        session.UpdateLoads(new Dictionary<long, TaskLoad> { [1] = new(90, 900, 10, 1) });
        Assert.Equal(code.Handle, session.Filtered[0].Handle);
        Assert.Equal(current.Handle, session.Filtered[1].Handle);

        session.SetQuery("ch");
        Assert.Equal(current.Handle, session.Selected?.Handle);
        session.SetQuery("wx");
        Assert.Equal(wechat.Handle, session.Selected?.Handle);
        session.SetQuery("");
        Assert.Equal(new[] { code.Handle, current.Handle, olderChrome.Handle, wechat.Handle }, session.Filtered.Select(window => window.Handle));
    }

    [Fact]
    public void Opening_forward_stays_on_the_previous_window_and_reverse_selects_the_last_row()
    {
        Assert.Equal(0, StableGroupedOrder.OpeningIndex(LoadSortMode.StableGrouped, 4, 1));
        Assert.Equal(0, StableGroupedOrder.OpeningIndex(LoadSortMode.StableGrouped, 4, 0));
        Assert.Equal(3, StableGroupedOrder.OpeningIndex(LoadSortMode.StableGrouped, 4, -1));
        Assert.Null(StableGroupedOrder.OpeningIndex(LoadSortMode.Recent, 4, 1));
        Assert.Null(StableGroupedOrder.OpeningIndex(LoadSortMode.Initial, 4, 1));
    }

    [Fact]
    public void Activation_history_moves_a_repeat_to_the_front_and_drops_the_oldest()
    {
        var history = new ActivationMru(3);
        history.Note(1);
        history.Note(2);
        history.Note(1);
        history.Note(3);
        history.Note(4);
        Assert.Equal(0, history.Rank(4));
        Assert.Equal(1, history.Rank(3));
        Assert.Equal(2, history.Rank(1));
        Assert.Equal(int.MaxValue, history.Rank(2));
        Assert.Equal(int.MaxValue, history.Rank(0));
    }

    [Fact]
    public void Schema_6_moves_the_default_initial_sort_and_keeps_an_explicit_choice()
    {
        var initial = Load("""{ "version": 5, "sortMode": "Initial", "theme": "Dark" }""");
        Assert.Equal(LoadSortMode.StableGrouped, initial.Settings.SortMode);
        Assert.Equal(AppTheme.Dark, initial.Settings.Theme);
        Assert.Equal(AppSettings.CurrentSchema, initial.Settings.Version);
        Assert.True(initial.Migrated);

        var recent = Load("""{ "version": 5, "sortMode": "Recent" }""");
        Assert.Equal(LoadSortMode.Recent, recent.Settings.SortMode);

        var memory = Load("""{ "version": 5, "sortMode": "Memory" }""");
        Assert.Equal(LoadSortMode.Memory, memory.Settings.SortMode);

        var kept = Load("""{ "version": 6, "sortMode": "Initial" }""");
        Assert.Equal(LoadSortMode.Initial, kept.Settings.SortMode);
        Assert.False(kept.Migrated);
    }

    static SettingsLoadResult Load(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return SettingsStore.Load(path);
    }
}
