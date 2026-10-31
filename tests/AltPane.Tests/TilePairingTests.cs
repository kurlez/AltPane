using AltPane.Core.Input;
using AltPane.Core.Models;
using AltPane.Core.Settings;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class TilePairingTests
{
    static TileRect Box(int x, int y, int width, int height) => new(x, y, x + width, y + height);

    static TileWindow Tile(
        long handle,
        TileRect frame,
        TileRect work,
        int rank = 5,
        int monitor = 1,
        bool minimized = false,
        bool visible = true,
        bool cloaked = false) =>
        new(handle, monitor, frame, work, minimized, visible, cloaked, rank);

    static bool HasPair(IReadOnlyList<TilePair> pairs, long a, long b) =>
        pairs.Any(pair =>
            (pair.First == a && pair.Second == b) || (pair.First == b && pair.Second == a));

    static WindowEntry Win(long handle, int z, string app, string? title = null, int monitor = 1) => new()
    {
        Handle = handle,
        Title = title ?? app,
        ClassName = "C",
        ProcessName = app,
        DisplayProcessName = app,
        ProcessId = (int)handle,
        KillProcessId = (int)handle,
        KillProcessName = app,
        IconPath = app + ".exe",
        OriginalIndex = z,
        MonitorNumber = monitor,
        MonitorWidth = 1920,
        MonitorHeight = 1080
    };

    [Fact]
    public void Left_and_right_halves_are_one_extra_row_and_both_windows_remain()
    {
        var work = Box(0, 0, 1920, 1080);
        var left = Tile(1, Box(0, 0, 960, 1080), work, rank: 4);
        var right = Tile(2, Box(960, 0, 960, 1080), work, rank: 1);
        var pairs = TilePairing.Find(new[] { left, right });

        var pair = Assert.Single(pairs);
        Assert.Equal(1, pair.First);
        Assert.Equal(2, pair.Second);
        Assert.Equal(2, pair.FocusHandle);
        Assert.Equal(1, pair.MonitorNumber);

        var chrome = Win(1, 0, "Chrome", "Inbox - Chrome");
        var code = Win(2, 1, "Code", "VS Code");
        var listed = TilePairListing.Append(new[] { chrome, code }, pairs);
        Assert.Equal(3, listed.Count);
        Assert.Same(chrome, listed[0]);
        Assert.Same(code, listed[1]);
        var extra = listed[2];
        Assert.True(extra.IsTilePair);
        Assert.Equal("Inbox - Chrome \uFF5C VS Code", extra.Title);
        Assert.Equal("Chrome \uFF5C Code", extra.DisplayProcessName);
        Assert.Equal(1, extra.TileLeft);
        Assert.Equal(2, extra.TileRight);
        Assert.Equal(2, extra.FocusHandle);
        Assert.Equal(1, extra.MonitorNumber);
        Assert.Equal("Chrome.exe", extra.IconPath);
        Assert.Equal("Code.exe", extra.SecondIconPath);
        Assert.False(extra.IsIconic);
    }

    [Fact]
    public void Top_and_bottom_halves_read_top_then_bottom()
    {
        var work = Box(0, 0, 1920, 1080);
        var top = Tile(1, Box(0, 0, 1920, 540), work, rank: 1);
        var bottom = Tile(2, Box(0, 540, 1920, 540), work, rank: 3);
        var pair = Assert.Single(TilePairing.Find(new[] { bottom, top }));
        Assert.Equal(1, pair.First);
        Assert.Equal(2, pair.Second);
        Assert.Equal(1, pair.FocusHandle);
    }

    [Fact]
    public void A_small_gap_or_sliver_overlap_still_pairs_and_a_real_gap_does_not()
    {
        var work = Box(0, 0, 1920, 1080);
        var tolerance = TilePairing.EdgeTolerance(work);
        var touching = TilePairing.Find(new[]
        {
            Tile(1, new TileRect(0, 0, 960 - tolerance, 1080), work),
            Tile(2, new TileRect(960, 0, 1920, 1080), work)
        });
        Assert.Single(touching);

        var border = TilePairing.Find(new[]
        {
            Tile(1, new TileRect(0, 0, 967, 1080), work),
            Tile(2, new TileRect(960, 0, 1920, 1080), work)
        });
        Assert.Single(border);

        var gap = TilePairing.Find(new[]
        {
            Tile(1, new TileRect(0, 0, 800, 1080), work),
            Tile(2, new TileRect(1120, 0, 1920, 1080), work)
        });
        Assert.Empty(gap);
    }

    [Fact]
    public void Overlapping_tiny_minimized_cloaked_and_other_monitor_windows_are_not_paired()
    {
        var work = Box(0, 0, 1920, 1080);
        Assert.Empty(TilePairing.Find(new[]
        {
            Tile(1, Box(100, 80, 1400, 900), work),
            Tile(2, Box(400, 160, 1400, 860), work)
        }));

        Assert.Empty(TilePairing.Find(new[]
        {
            Tile(1, Box(0, 0, 960, 1080), work),
            Tile(2, Box(960, 0, 200, 180), work)
        }));

        Assert.Empty(TilePairing.Find(new[]
        {
            Tile(1, Box(0, 0, 960, 1080), work, minimized: true),
            Tile(2, Box(960, 0, 960, 1080), work)
        }));

        Assert.Empty(TilePairing.Find(new[]
        {
            Tile(1, Box(0, 0, 960, 1080), work, cloaked: true),
            Tile(2, Box(960, 0, 960, 1080), work)
        }));

        Assert.Empty(TilePairing.Find(new[]
        {
            Tile(1, Box(0, 0, 960, 1080), work, visible: false),
            Tile(2, Box(960, 0, 960, 1080), work)
        }));

        var other = Box(1920, 0, 1920, 1080);
        Assert.Empty(TilePairing.Find(new[]
        {
            Tile(1, Box(0, 0, 960, 1080), work, monitor: 1),
            Tile(2, Box(1920, 0, 960, 1080), other, monitor: 2)
        }));

        Assert.Empty(TilePairing.Find(new[]
        {
            Tile(1, Box(0, 0, 1728, 1080), work),
            Tile(2, Box(1728, 0, 192, 1080), work)
        }));
    }

    [Fact]
    public void A_window_covering_both_halves_does_not_hide_the_pair()
    {
        var work = Box(0, 0, 1920, 1080);
        var pairs = TilePairing.Find(new[]
        {
            Tile(1, Box(0, 0, 960, 1080), work),
            Tile(2, Box(960, 0, 960, 1080), work),
            Tile(3, Box(200, 200, 1400, 700), work)
        });
        var pair = Assert.Single(pairs);
        Assert.True(HasPair(new[] { pair }, 1, 2));
    }

    [Fact]
    public void Three_columns_pair_neighbors_only()
    {
        var work = Box(0, 0, 1920, 1080);
        var pairs = TilePairing.Find(new[]
        {
            Tile(1, Box(0, 0, 640, 1080), work),
            Tile(2, Box(640, 0, 640, 1080), work),
            Tile(3, Box(1280, 0, 640, 1080), work)
        });
        Assert.Equal(2, pairs.Count);
        Assert.True(HasPair(pairs, 1, 2));
        Assert.True(HasPair(pairs, 2, 3));
        Assert.False(HasPair(pairs, 1, 3));
    }

    [Fact]
    public void A_grid_pairs_edges_and_not_diagonals()
    {
        var work = Box(0, 0, 1920, 1080);
        var pairs = TilePairing.Find(new[]
        {
            Tile(1, Box(0, 0, 960, 540), work),
            Tile(2, Box(960, 0, 960, 540), work),
            Tile(3, Box(0, 540, 960, 540), work),
            Tile(4, Box(960, 540, 960, 540), work)
        });
        Assert.Equal(4, pairs.Count);
        Assert.True(HasPair(pairs, 1, 2));
        Assert.True(HasPair(pairs, 3, 4));
        Assert.True(HasPair(pairs, 1, 3));
        Assert.True(HasPair(pairs, 2, 4));
        Assert.False(HasPair(pairs, 1, 4));
        Assert.False(HasPair(pairs, 2, 3));
    }

    [Fact]
    public void A_half_plus_two_quarters_pairs_each_shared_edge()
    {
        var work = Box(0, 0, 1920, 1080);
        var pairs = TilePairing.Find(new[]
        {
            Tile(1, Box(0, 0, 960, 1080), work),
            Tile(2, Box(960, 0, 960, 540), work),
            Tile(3, Box(960, 540, 960, 540), work)
        });
        Assert.Equal(3, pairs.Count);
        Assert.True(HasPair(pairs, 1, 2));
        Assert.True(HasPair(pairs, 1, 3));
        Assert.True(HasPair(pairs, 2, 3));
        var vertical = Assert.Single(pairs, pair => pair.First == 2 && pair.Second == 3);
        Assert.Equal(2, vertical.First);
        Assert.Equal(3, vertical.Second);
    }

    [Fact]
    public void Tolerance_grows_with_the_work_area_so_a_high_dpi_layout_still_pairs()
    {
        var hd = Box(0, 0, 1920, 1080);
        var uhd = Box(0, 0, 3840, 2160);
        Assert.True(TilePairing.EdgeTolerance(uhd) > TilePairing.EdgeTolerance(hd));

        var pairs = TilePairing.Find(new[]
        {
            Tile(1, Box(0, 0, 1920, 2160), uhd),
            Tile(2, Box(1920, 0, 1920, 2160), uhd)
        });
        Assert.Single(pairs);

        var tolerance = TilePairing.EdgeTolerance(uhd);
        var gapped = TilePairing.Find(new[]
        {
            Tile(1, new TileRect(0, 0, 1920 - tolerance, 2160), uhd),
            Tile(2, new TileRect(1920, 0, 3840, 2160), uhd)
        });
        Assert.Single(gapped);

        var apart = TilePairing.Find(new[]
        {
            Tile(1, new TileRect(0, 0, 1700, 2160), uhd),
            Tile(2, new TileRect(2100, 0, 3840, 2160), uhd)
        });
        Assert.Empty(apart);
    }

    [Fact]
    public void A_secondary_monitor_uses_its_own_work_area_origin()
    {
        var work = Box(1920, 0, 1920, 1080);
        var pair = Assert.Single(TilePairing.Find(new[]
        {
            Tile(8, new TileRect(1920, 0, 2880, 1080), work, monitor: 2),
            Tile(9, new TileRect(2880, 0, 3840, 1080), work, monitor: 2, rank: 0)
        }));
        Assert.Equal(2, pair.MonitorNumber);
        Assert.Equal(8, pair.First);
        Assert.Equal(9, pair.FocusHandle);
    }

    [Fact]
    public void Two_windows_that_only_fill_the_top_of_the_screen_are_not_a_pair()
    {
        var work = Box(0, 0, 1920, 1080);
        Assert.Empty(TilePairing.Find(new[]
        {
            Tile(1, Box(0, 0, 960, 500), work),
            Tile(2, Box(960, 0, 960, 500), work)
        }));
    }

    [Fact]
    public void The_combined_row_sorts_by_the_more_recent_window_and_does_not_take_row_zero()
    {
        var chrome = Win(1, 0, "Chrome");
        var code = Win(2, 2, "Code");
        var notes = Win(3, 1, "Notes");
        var pair = TilePairListing.Create(chrome, code, -1, chrome.Handle);
        var mru = new ActivationMru();
        mru.Note(code.Handle);
        mru.Note(chrome.Handle);
        mru.Note(notes.Handle);

        var ordered = StableGroupedOrder.Arrange(
            new[] { notes, chrome, code, pair },
            foregroundHandle: notes.Handle,
            mru.Rank);

        Assert.Equal(chrome.Handle, ordered[0].Handle);
        Assert.False(ordered[0].IsTilePair);
        Assert.Contains(ordered, window => window.Handle == code.Handle && !window.IsTilePair);
        Assert.Contains(ordered, window => window.Handle == chrome.Handle && !window.IsTilePair);
        var pairAt = ordered.FindIndex(window => window.IsTilePair);
        var codeAt = ordered.FindIndex(window => window.Handle == code.Handle);
        Assert.True(pairAt > 0);
        Assert.True(pairAt < codeAt);
    }

    [Fact]
    public void Search_pinyin_monitor_filter_and_removal_treat_the_pair_as_a_normal_row()
    {
        var wechat = Win(1, 0, "WeChat", "微信");
        var chrome = Win(2, 1, "Chrome");
        var pair = TilePairListing.Create(wechat, chrome, -1, wechat.Handle);
        var session = new SwitcherSession(new[] { wechat, chrome, pair });

        session.SetQuery("wx");
        Assert.Contains(session.Filtered, window => window.IsTilePair);
        Assert.Contains(session.Filtered, window => window.Handle == wechat.Handle);

        session.SetQuery("chrome");
        Assert.Contains(session.Filtered, window => window.IsTilePair);
        Assert.Contains(session.Filtered, window => window.Handle == chrome.Handle);
        var index = session.Filtered.ToList().FindIndex(window => window.IsTilePair);
        Assert.NotEqual("", DigitSelect.Label(index));

        session.SetQuery("");
        session.SetMonitorFilter(2);
        Assert.Empty(session.Filtered);
        session.SetMonitorFilter(1);
        Assert.Equal(3, session.Filtered.Count);

        Assert.True(session.RemoveHandle(wechat.Handle));
        Assert.DoesNotContain(session.All, window => window.IsTilePair);
        Assert.Contains(session.All, window => window.Handle == chrome.Handle);

        Assert.Equal(RowHints.TilePair, RowHints.Text(HintRow.TilePair, true));
        Assert.Equal("", RowHints.Text(HintRow.TilePair, false));
    }

    [Fact]
    public void Schema_10_turns_tiled_pairs_on_and_keeps_an_explicit_off()
    {
        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var oldPath = Path.Combine(directory, "old.json");
        File.WriteAllText(oldPath, """{ "version": 9, "fileDialogJump": false }""");
        var migrated = SettingsStore.Load(oldPath);
        Assert.True(migrated.Migrated);
        Assert.Equal(AppSettings.CurrentSchema, migrated.Settings.Version);
        Assert.True(migrated.Settings.CombineTiledWindows);
        Assert.False(migrated.Settings.FileDialogJump);

        var explicitOff = Path.Combine(directory, "off-old.json");
        File.WriteAllText(explicitOff, """{ "version": 9, "combineTiledWindows": false }""");
        var keptOld = SettingsStore.Load(explicitOff);
        Assert.True(keptOld.Migrated);
        Assert.False(keptOld.Settings.CombineTiledWindows);

        var chosen = Path.Combine(directory, "chosen.json");
        File.WriteAllText(chosen, """{ "version": 10, "combineTiledWindows": false }""");
        var kept = SettingsStore.Load(chosen);
        Assert.False(kept.Migrated);
        Assert.False(kept.Settings.CombineTiledWindows);
        Assert.Equal(10, kept.Settings.Version);

        Assert.True(AppSettings.CreateDefault().CombineTiledWindows);
        SettingsStore.Save(oldPath, AppSettings.CreateDefault());
        var round = SettingsStore.Load(oldPath);
        Assert.False(round.Migrated);
        Assert.True(round.Settings.CombineTiledWindows);
        Assert.Contains("\"combineTiledWindows\": true", File.ReadAllText(oldPath), StringComparison.Ordinal);
    }
}
