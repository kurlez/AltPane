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
        Assert.Equal(new[] { "Chrome", "记事本", "微信", "", "123 Notes" }, session.Filtered.Select(window => window.Title));
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, session.Filtered.Select(window => session.IndexForHandle(window.Handle)));
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
        Assert.Equal("c", session.IndexForHandle(3));
        session.SetQuery("c");
        Assert.Equal("中文", session.Selected!.Title);
        Assert.Contains(session.Filtered, window => window.Handle == 3);
        session.SetQuery("al");
        Assert.Equal("Alpha", session.Selected!.Title);
        session.SetQuery("");
        Assert.Equal(new[] { "Alpha", "Beta", "中文" }, session.Filtered.Select(window => window.Title));
        Assert.Equal("a", session.IndexForHandle(1));
        Assert.Equal("b", session.IndexForHandle(2));
        Assert.Equal("c", session.IndexForHandle(3));
    }

    [Fact]
    public void Codes_grow_to_two_letters_and_new_windows_do_not_renumber()
    {
        Assert.Equal("a", RowIndex.Code(0));
        Assert.Equal("z", RowIndex.Code(25));
        Assert.Equal("aa", RowIndex.Code(26));
        Assert.Equal("ab", RowIndex.Code(27));
        var session = new SwitcherSession(new[] { Win(1, "Alpha", "a"), Win(2, "Beta", "b") });
        session.SetSortMode(LoadSortMode.Initial);
        Assert.True(session.RemoveHandle(1));
        Assert.Equal("b", session.IndexForHandle(2));
        session.NoteArrived(Win(9, "Zebra", "z"));
        Assert.Equal("b", session.IndexForHandle(2));
        Assert.Equal("c", session.IndexForHandle(9));
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
    }
}
