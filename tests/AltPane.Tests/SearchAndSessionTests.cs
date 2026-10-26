using AltPane.Core.Models;
using AltPane.Core.Search;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class SearchMatcherTests
{
    [Theory]
    [InlineData("", "Notepad", "notepad", true)]
    [InlineData("note", "Notepad", "notepad", true)]
    [InlineData("NOTEPAD", "Untitled - Notepad", "notepad", true)]
    [InlineData("chrome", "New Tab", "chrome", true)]
    [InlineData("记事", "记事本", "notepad", true)]
    [InlineData("missing", "Notepad", "notepad", false)]
    public void Substring_match_is_case_insensitive(string query, string title, string process, bool expected) =>
        Assert.Equal(expected, SearchMatcher.Matches(query, title, process));

    [Fact]
    public void Acronym_matches_word_starts()
    {
        Assert.True(SearchMatcher.IsAcronym("Visual Studio Code", "vsc"));
        Assert.True(SearchMatcher.Matches("vsc", "Visual Studio Code", "Code"));
        Assert.False(SearchMatcher.Matches("qqq", "Visual Studio Code", "Code"));
    }

    [Fact]
    public void Every_whitespace_token_must_match()
    {
        Assert.True(SearchMatcher.Matches("visual code", "Visual Studio Code", "Code"));
        Assert.False(SearchMatcher.Matches("visual nope", "Visual Studio Code", "Code"));
    }

    [Fact]
    public void Title_substring_ranks_ahead_of_process_and_fuzzy()
    {
        var items = new[]
        {
            Entry(1, "Something", "chrome"),
            Entry(2, "Google Chrome", "chrome"),
            Entry(3, "Visual Studio Code", "Code")
        };
        var ranked = SearchMatcher.Rank(items, "chr", e => e.Title, e => e.DisplayProcessName, e => e.OriginalIndex);
        Assert.Equal(new long[] { 2, 1 }, ranked.Select(e => e.Handle).ToArray());
        Assert.Equal(0, SearchMatcher.Score("chr", "Google Chrome", "chrome"));
        Assert.Equal(1, SearchMatcher.Score("chr", "Something", "chrome"));
    }

    [Fact]
    public void Empty_query_preserves_order()
    {
        var items = new[] { Entry(1, "B", "b"), Entry(2, "A", "a") };
        var ranked = SearchMatcher.Rank(items, "  ", e => e.Title, e => e.DisplayProcessName, e => e.OriginalIndex);
        Assert.Equal(new long[] { 1, 2 }, ranked.Select(e => e.Handle).ToArray());
    }

    static WindowEntry Entry(int index, string title, string process) => new()
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
}

public class SwitcherSessionTests
{
    static WindowEntry Win(int index, string title, string process) => new()
    {
        Handle = index,
        Title = title,
        ClassName = "C",
        ProcessName = process,
        DisplayProcessName = process,
        ProcessId = 100 + index,
        KillProcessId = 100 + index,
        KillProcessName = process,
        OriginalIndex = index
    };

    static SwitcherSession Sample() => new(new[]
    {
        Win(1, "Current", "current"),
        Win(2, "Notepad", "notepad"),
        Win(3, "Google Chrome", "chrome"),
        Win(4, "Visual Studio Code", "Code")
    });

    [Fact]
    public void First_forward_cycle_selects_the_previous_window()
    {
        var session = Sample();
        Assert.Equal(0, session.SelectedIndex);
        session.Move(1);
        Assert.Equal("Notepad", session.Selected!.Title);
    }

    [Fact]
    public void Reverse_cycle_stops_on_the_first_window()
    {
        var session = Sample();
        session.Move(-1);
        Assert.Equal("Current", session.Selected!.Title);
        session.Move(1);
        Assert.Equal("Notepad", session.Selected!.Title);
    }

    [Fact]
    public void Forward_stops_on_the_last_window()
    {
        var session = Sample();
        session.SelectLast();
        var last = session.SelectedIndex;
        session.Move(1);
        Assert.Equal(last, session.SelectedIndex);
        session.Move(4);
        Assert.Equal(last, session.SelectedIndex);
    }

    [Fact]
    public void Page_clamps_and_arrows_wrap()
    {
        var session = Sample();
        session.Page(1);
        Assert.Equal(3, session.SelectedIndex);
        session.Page(-1);
        Assert.Equal(0, session.SelectedIndex);
    }

    [Fact]
    public void Query_filters_and_resets_selection()
    {
        var session = Sample();
        session.Move(2);
        session.AppendQuery("code");
        Assert.Single(session.Filtered);
        Assert.Equal("Visual Studio Code", session.Selected!.Title);
        session.Backspace();
        session.Backspace();
        session.Backspace();
        session.Backspace();
        Assert.Equal(4, session.Filtered.Count);
        Assert.Equal(0, session.SelectedIndex);
    }

    [Fact]
    public void Chinese_substring_filters()
    {
        var session = new SwitcherSession(new[]
        {
            Win(1, "微信", "WeChat"),
            Win(2, "记事本", "notepad")
        });
        session.SetQuery("记事");
        Assert.Equal("记事本", session.Selected!.Title);
    }

    [Fact]
    public void Removing_the_selected_window_keeps_a_neighbor()
    {
        var session = Sample();
        session.Move(1);
        Assert.True(session.RemoveHandle(2));
        Assert.Equal(3, session.Filtered.Count);
        Assert.NotNull(session.Selected);
        Assert.NotEqual(2, session.Selected!.Handle);
    }

    [Fact]
    public void Empty_session_has_no_selection()
    {
        var session = new SwitcherSession(Array.Empty<WindowEntry>());
        session.Move(1);
        session.AppendQuery("a");
        Assert.Null(session.Selected);
        Assert.False(session.RemoveHandle(1));
    }

    [Fact]
    public void Hover_selects_by_handle()
    {
        var session = Sample();
        Assert.True(session.SelectHandle(3));
        Assert.Equal("Google Chrome", session.Selected!.Title);
        Assert.False(session.SelectHandle(99));
    }
}
