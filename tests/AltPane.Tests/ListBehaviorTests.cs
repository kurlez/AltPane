using AltPane.Core.Models;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class ListBehaviorTests
{
    static WindowEntry Win(int index, string title, string process) => new()
    {
        Handle = index,
        Title = title,
        ClassName = "C",
        ProcessName = process,
        DisplayProcessName = process,
        ProcessId = index,
        KillProcessId = index,
        KillProcessName = process,
        OriginalIndex = index
    };

    [Fact]
    public void Initial_letter_sort_uses_pinyin_and_puts_symbols_last()
    {
        var session = new SwitcherSession(new[]
        {
            Win(1, "微信", "WeChat"),
            Win(2, "记事本", "notepad"),
            Win(3, "Chrome", "chrome"),
            Win(4, "123 Notes", "notes"),
            Win(5, "", "zebra")
        });
        session.SetSortMode(LoadSortMode.Initial);
        Assert.Equal(new[] { "Chrome", "记事本", "123 Notes", "微信", "" }, session.Filtered.Select(window => window.Title));
        Assert.Equal(new[] { "c", "notep", "notes", "w", "z" }, session.Filtered.Select(window => session.IndexForHandle(window.Handle)));
    }

    [Fact]
    public void Indexes_stay_put_while_search_filters_and_a_letter_selects_that_row()
    {
        var session = new SwitcherSession(new[]
        {
            Win(1, "Alpha", "alpha"),
            Win(2, "Beta", "beta"),
            Win(3, "中文", "zhong")
        });
        session.SetSortMode(LoadSortMode.Initial);
        Assert.Equal("z", session.IndexForHandle(3));
        session.SetQuery("z");
        Assert.Equal("中文", session.Selected!.Title);
        Assert.Contains(session.Filtered, window => window.Handle == 3);
        session.SetQuery("al");
        Assert.Equal("Alpha", session.Selected!.Title);
        session.SetQuery("");
        Assert.Equal(new[] { "Alpha", "Beta", "中文" }, session.Filtered.Select(window => window.Title));
        Assert.Equal("a", session.IndexForHandle(1));
        Assert.Equal("b", session.IndexForHandle(2));
        Assert.Equal("z", session.IndexForHandle(3));
    }

    [Fact]
    public void Name_badges_use_a_unique_prefix_and_new_windows_do_not_renumber()
    {
        Assert.Equal("wx", NameIndex.Letters("微信"));
        Assert.Equal("chrome", NameIndex.Letters("Chrome"));
        Assert.Equal(new[] { "ch", "co" }, NameIndex.Assign(new[] { "chrome", "code" }));
        Assert.Equal(new[] { "wx1", "wx2" }, NameIndex.Assign(new[] { "wx", "wx" }));
        Assert.Equal("c", NameIndex.Assign(new[] { "chrome" })[0]);
        var session = new SwitcherSession(new[] { Win(1, "Alpha", "alpha"), Win(2, "Beta", "beta") });
        session.SetSortMode(LoadSortMode.Initial);
        Assert.Equal("a", session.IndexForHandle(1));
        Assert.Equal("b", session.IndexForHandle(2));
        Assert.True(session.RemoveHandle(1));
        Assert.Equal("b", session.IndexForHandle(2));
        session.NoteArrived(Win(9, "Zebra", "zebra"));
        Assert.Equal("b", session.IndexForHandle(2));
        Assert.Equal("z", session.IndexForHandle(9));
        session.NoteArrived(Win(10, "Beacon", "beta"));
        Assert.Equal("b", session.IndexForHandle(2));
        Assert.Equal("be", session.IndexForHandle(10));
        var chinese = new SwitcherSession(new[] { Win(1, "notes", "chrome"), Win(2, "文档", "微信") });
        chinese.SetSortMode(LoadSortMode.Initial);
        Assert.Equal("wx", chinese.IndexForHandle(2));
        chinese.SetQuery("wx");
        Assert.Equal(2, chinese.Selected?.Handle);
    }

    [Fact]
    public void Click_outside_dismisses_unless_we_are_activating_or_bouncing()
    {
        Assert.True(SwitcherCleanup.ShouldDismiss(foreground: 5, switcher: 9, foregroundIsHint: false, holdingActivation: false, previousForeground: 4, ageMilliseconds: 400));
        Assert.False(SwitcherCleanup.ShouldDismiss(5, 9, foregroundIsHint: true, holdingActivation: false, previousForeground: 4, ageMilliseconds: 400));
        Assert.False(SwitcherCleanup.ShouldDismiss(5, 9, false, holdingActivation: true, previousForeground: 4, ageMilliseconds: 400));
        Assert.False(SwitcherCleanup.ShouldDismiss(4, 9, false, false, previousForeground: 4, ageMilliseconds: 100));
        Assert.True(SwitcherCleanup.ShouldDismiss(4, 9, false, false, previousForeground: 4, ageMilliseconds: 300));
        Assert.Equal(CleanupStep.UnregisterThumbnail, SwitcherCleanup.Steps(activateSelection: false)[0]);
        Assert.Contains(CleanupStep.ReturnBorrowedPreview, SwitcherCleanup.Steps(activateSelection: false));
        Assert.Contains(CleanupStep.RestorePreviousForeground, SwitcherCleanup.Steps(activateSelection: false));
        Assert.DoesNotContain(CleanupStep.ReturnBorrowedPreview, SwitcherCleanup.Steps(activateSelection: true));
        Assert.Contains(CleanupStep.ReleaseBorrowedForActivate, SwitcherCleanup.Steps(activateSelection: true));
        Assert.DoesNotContain(CleanupStep.RestorePreviousForeground, SwitcherCleanup.Steps(activateSelection: true));
        Assert.True(SwitcherCleanup.IsImeUiClass("MSCTFIME UI"));
        Assert.True(SwitcherCleanup.IsImeUiClass("CiceroUIWndFrame"));
        Assert.True(SwitcherCleanup.IsImeUiClass("IME"));
        Assert.True(SwitcherCleanup.IsImeUiClass("Microsoft.IME.UIManager"));
        Assert.False(SwitcherCleanup.IsImeUiClass("Chrome_WidgetWin_1"));
        Assert.False(SwitcherCleanup.ShouldDismiss(5, 9, false, false, previousForeground: 4, ageMilliseconds: 400, foregroundIsIme: true));
    }
}
