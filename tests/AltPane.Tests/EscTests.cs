using AltPane.Core.Apps;
using AltPane.Core.Input;
using AltPane.Core.Models;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class EscActionTests
{
    [Fact]
    public void Query_then_empty_then_ime()
    {
        Assert.Equal(EscDecision.ClearQuery, EscAction.Decide(imeComposing: false, hasQuery: true));
        Assert.Equal(EscDecision.Close, EscAction.Decide(imeComposing: false, hasQuery: false));
        Assert.Equal(EscDecision.PassToIme, EscAction.Decide(imeComposing: true, hasQuery: true));
        Assert.Equal(EscDecision.PassToIme, EscAction.Decide(imeComposing: true, hasQuery: false));
    }

    [Fact]
    public void Held_alt_clears_a_query_and_a_second_esc_cancels()
    {
        var withQuery = Open(Key(VirtualKeys.Escape, true, ModifierState.AltOnly)) with { HasSearchQuery = true };
        Assert.Equal(HookCommand.ClearQuery, HookInterpreter.Interpret(withQuery).Command);
        var empty = Open(Key(VirtualKeys.Escape, true, ModifierState.AltOnly));
        Assert.Equal(HookCommand.Cancel, HookInterpreter.Interpret(empty).Command);
        var composing = withQuery with { ImeComposing = true };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(composing).Command);
    }

    [Fact]
    public void Search_mode_still_passes_esc_so_the_text_box_and_ime_see_it()
    {
        var search = Open(Key(VirtualKeys.Escape, true, ModifierState.None)) with
        {
            SearchMode = true,
            GestureLatched = false,
            HasSearchQuery = true
        };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(search).Command);
        var composing = search with { ImeComposing = true };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(composing).Command);
    }

    [Fact]
    public void Shift_enter_commits_without_moving_and_ctrl_enter_stays_admin()
    {
        var shift = Open(Key(VirtualKeys.Return, true, new ModifierState(true, false, true, false)));
        var stayed = HookInterpreter.Interpret(shift);
        Assert.Equal(HookCommand.Commit, stayed.Command);
        Assert.Equal(0, stayed.MoveChoice);
        var both = Open(Key(VirtualKeys.Return, true, new ModifierState(true, true, true, false)));
        Assert.Equal(HookCommand.AdminLaunch, HookInterpreter.Interpret(both).Command);
    }

    [Fact]
    public void Clear_query_keeps_the_switcher_open_and_the_next_esc_cancels()
    {
        var open = GestureSnapshot.Idle with
        {
            SwitcherOpen = true,
            GestureLatched = true,
            LatchedTrigger = ModifierKeys.Alt,
            QueryLength = 4
        };
        var cleared = GestureTransitions.Apply(open, HookResult.ClearQuery(), null, true, ModifierKeys.Alt);
        Assert.IsType<UiOp.ClearQuery>(cleared.Op);
        Assert.True(cleared.Next.SwitcherOpen);
        Assert.Equal(0, cleared.Next.QueryLength);
        Assert.True(cleared.Next.GestureLatched);

        var again = cleared.Next.ToHookInput(
            HotkeyChord.AltTab(), false, false, false, VirtualKeys.Escape, true, false, ModifierState.AltOnly);
        var cancel = HookInterpreter.Interpret(again);
        Assert.Equal(HookCommand.Cancel, cancel.Command);
        var closed = GestureTransitions.Apply(cleared.Next, cancel, null, true, ModifierKeys.Alt);
        Assert.IsType<UiOp.Cancel>(closed.Op);
        Assert.False(closed.Next.SwitcherOpen);
    }

    [Fact]
    public void Clearing_the_query_restores_the_window_selected_before_typing()
    {
        var session = new SwitcherSession(new[]
        {
            Window(1, "Notepad"),
            Window(2, "Chrome"),
            Window(3, "Mail")
        });
        Assert.True(session.SelectHandle(2));
        session.AppendQuery("mail");
        Assert.Equal(3, session.Selected!.Handle);
        Assert.True(session.ClearQuery());
        Assert.Equal("", session.Query);
        Assert.Equal(2, session.Selected!.Handle);
        Assert.False(session.ClearQuery());
    }

    [Fact]
    public void No_matching_window_preselects_the_top_app()
    {
        var session = new SwitcherSession(new[] { Window(1, "Notepad") });
        var app = AppCatalogEntry("Code", @"C:\Program Files\Microsoft VS Code\Code.exe");
        session.SetCatalog(new[] { app }, new Dictionary<string, LaunchMemory>(), Array.Empty<string>(), enabled: true);
        session.SetQuery("code");
        Assert.Null(session.Selected);
        Assert.NotNull(session.SelectedApp);
        Assert.Equal("Code", session.SelectedApp!.Value.App.Name);
    }

    static HookInput Key(int vk, bool down, ModifierState mods) => new()
    {
        Hotkey = HotkeyChord.AltTab(),
        VirtualKey = vk,
        IsKeyDown = down,
        Modifiers = mods
    };

    static HookInput Open(HookInput input) => input with
    {
        SwitcherOpen = true,
        GestureLatched = true,
        LatchedTrigger = ModifierKeys.Alt
    };

    static WindowEntry Window(long handle, string title) => new()
    {
        Handle = handle,
        Title = title,
        ClassName = "C",
        ProcessName = title,
        DisplayProcessName = title,
        ProcessId = (int)handle,
        KillProcessId = (int)handle,
        KillProcessName = title,
        OriginalIndex = (int)handle
    };

    static AppEntry AppCatalogEntry(string name, string path) =>
        AppCatalog.Create(name, path, AppKind.Executable, AppSource.Installed, path, "", "", path, 0);
}
