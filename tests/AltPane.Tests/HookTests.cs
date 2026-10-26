using AltPane.Core.Input;
using AltPane.Core.Models;
using Xunit;

namespace AltPane.Tests;

public class HookInterpreterTests
{
    static HookInput Key(int vk, bool down, ModifierState mods, bool repeat = false) => new()
    {
        Hotkey = HotkeyChord.AltTab(),
        VirtualKey = vk,
        IsKeyDown = down,
        IsRepeat = repeat,
        Modifiers = mods
    };

    static HookInput Open(HookInput input) => input with
    {
        SwitcherOpen = true,
        GestureLatched = true,
        LatchedTrigger = ModifierKeys.Alt
    };

    [Fact]
    public void Alt_tab_starts_a_forward_cycle()
    {
        var result = HookInterpreter.Interpret(Key(VirtualKeys.Tab, true, ModifierState.AltOnly));
        Assert.Equal(HookCommand.Cycle, result.Command);
        Assert.Equal(1, result.Delta);
        Assert.True(result.LatchGesture);
    }

    [Fact]
    public void Alt_shift_tab_reverses()
    {
        var result = HookInterpreter.Interpret(Key(VirtualKeys.Tab, true, ModifierState.AltShift));
        Assert.Equal(-1, result.Delta);
    }

    [Fact]
    public void Ctrl_alt_tab_is_not_our_hotkey()
    {
        var mods = new ModifierState(true, true, false, false);
        var result = HookInterpreter.Interpret(Key(VirtualKeys.Tab, true, mods));
        Assert.Equal(HookCommand.Pass, result.Command);
    }

    [Fact]
    public void Paused_and_recording_pass_the_hotkey_through()
    {
        var paused = Key(VirtualKeys.Tab, true, ModifierState.AltOnly) with { Paused = true };
        var recording = Key(VirtualKeys.Tab, true, ModifierState.AltOnly) with { RecordingHotkey = true };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(paused).Command);
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(recording).Command);
    }

    [Fact]
    public void Win_tab_is_optional()
    {
        var win = new ModifierState(false, false, false, true);
        var ignored = Key(VirtualKeys.Tab, true, win);
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(ignored).Command);
        var captured = ignored with { CaptureWinTab = true };
        var result = HookInterpreter.Interpret(captured);
        Assert.Equal(HookCommand.Cycle, result.Command);
        Assert.Equal(1, result.Delta);
        var reverse = Key(VirtualKeys.Tab, true, new ModifierState(false, false, true, true)) with { CaptureWinTab = true };
        Assert.Equal(-1, HookInterpreter.Interpret(reverse).Delta);
    }

    [Fact]
    public void Custom_hotkey_is_honored()
    {
        var input = new HookInput
        {
            Hotkey = new HotkeyChord { Modifiers = ModifierKeys.Control | ModifierKeys.Alt, VirtualKey = VirtualKeys.Space },
            VirtualKey = VirtualKeys.Space,
            IsKeyDown = true,
            Modifiers = new ModifierState(true, true, false, false)
        };
        Assert.Equal(HookCommand.Cycle, HookInterpreter.Interpret(input).Command);
        var plain = Key(VirtualKeys.Tab, true, ModifierState.AltOnly) with { Hotkey = input.Hotkey };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(plain).Command);
    }

    [Fact]
    public void Release_of_alt_commits_only_while_latched()
    {
        var up = Open(Key(VirtualKeys.Menu, false, ModifierState.None));
        Assert.Equal(HookCommand.Commit, HookInterpreter.Interpret(up).Command);
        var unlatched = up with { GestureLatched = false, LatchedTrigger = ModifierKeys.None };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(unlatched).Command);
    }

    [Fact]
    public void Sticky_release_keeps_the_switcher_and_a_quick_tap_is_optional()
    {
        var shown = Open(Key(VirtualKeys.Menu, false, ModifierState.None)) with
        {
            StickySwitcher = true,
            SwitcherShown = true
        };
        Assert.Equal(HookCommand.ReleaseHold, HookInterpreter.Interpret(shown).Command);

        var hidden = shown with { SwitcherShown = false, QuickTapSwitches = false };
        Assert.Equal(HookCommand.ReleaseHold, HookInterpreter.Interpret(hidden).Command);

        var instant = hidden with { QuickTapSwitches = true };
        Assert.Equal(HookCommand.Commit, HookInterpreter.Interpret(instant).Command);

        var typed = instant with { HasSearchQuery = true };
        Assert.Equal(HookCommand.ReleaseHold, HookInterpreter.Interpret(typed).Command);

        var enter = Open(Key(VirtualKeys.Return, true, ModifierState.AltOnly)) with { StickySwitcher = true };
        Assert.Equal(HookCommand.Commit, HookInterpreter.Interpret(enter).Command);
        var tab = Open(Key(VirtualKeys.Tab, true, ModifierState.AltOnly)) with { StickySwitcher = true };
        Assert.Equal(HookCommand.Cycle, HookInterpreter.Interpret(tab).Command);
    }

    [Fact]
    public void Pending_trigger_release_swallows_the_physical_key_up()
    {
        var up = Key(VirtualKeys.LMenu, false, ModifierState.None) with { PendingTriggerRelease = ModifierKeys.Alt };
        var result = HookInterpreter.Interpret(up);
        Assert.Equal(HookCommand.Swallow, result.Command);
        Assert.True(result.ClearAwaitingRelease);
    }

    [Fact]
    public void Open_switcher_navigation_and_editing()
    {
        Assert.Equal(HookCommand.Cancel, HookInterpreter.Interpret(Open(Key(VirtualKeys.Escape, true, ModifierState.AltOnly))).Command);
        Assert.Equal(HookCommand.Commit, HookInterpreter.Interpret(Open(Key(VirtualKeys.Return, true, ModifierState.AltOnly))).Command);
        Assert.Equal(HookCommand.Cycle, HookInterpreter.Interpret(Open(Key(VirtualKeys.Down, true, ModifierState.AltOnly))).Command);
        Assert.Equal(HookCommand.Home, HookInterpreter.Interpret(Open(Key(VirtualKeys.Home, true, ModifierState.AltOnly))).Command);
        Assert.Equal(HookCommand.End, HookInterpreter.Interpret(Open(Key(VirtualKeys.End, true, ModifierState.AltOnly))).Command);
        Assert.Equal(-1, HookInterpreter.Interpret(Open(Key(VirtualKeys.Prior, true, ModifierState.AltOnly))).Delta);
        Assert.Equal(HookCommand.Type, HookInterpreter.Interpret(Open(Key(VirtualKeys.A, true, ModifierState.AltOnly))).Command);
        Assert.Equal(HookCommand.Backspace, HookInterpreter.Interpret(Open(Key(VirtualKeys.Back, true, ModifierState.AltOnly))).Command);
        Assert.Equal(HookCommand.CloseWindow, HookInterpreter.Interpret(Open(Key(VirtualKeys.Delete, true, ModifierState.AltOnly))).Command);
        var killMods = new ModifierState(true, false, true, false);
        Assert.Equal(HookCommand.KillProcess, HookInterpreter.Interpret(Open(Key(VirtualKeys.Delete, true, killMods))).Command);
        Assert.Equal(HookCommand.EnterSearch, HookInterpreter.Interpret(Open(Key(VirtualKeys.F, true, new ModifierState(true, true, false, false)))).Command);
        Assert.Equal(HookCommand.Minimize, HookInterpreter.Interpret(Open(Key(VirtualKeys.M, true, new ModifierState(true, true, false, false)))).Command);
        Assert.Equal(HookCommand.Maximize, HookInterpreter.Interpret(Open(Key(VirtualKeys.M, true, new ModifierState(true, true, true, false)))).Command);
        Assert.Equal(HookCommand.Restore, HookInterpreter.Interpret(Open(Key(VirtualKeys.R, true, new ModifierState(true, true, false, false)))).Command);
    }

    [Fact]
    public void Space_asks_the_switcher_until_there_is_a_query()
    {
        Assert.Equal(HookCommand.SpaceAction, HookInterpreter.Interpret(Open(Key(VirtualKeys.Space, true, ModifierState.AltOnly))).Command);
        var typing = Open(Key(VirtualKeys.Space, true, ModifierState.AltOnly)) with { HasSearchQuery = true };
        Assert.Equal(HookCommand.Type, HookInterpreter.Interpret(typing).Command);
    }

    [Fact]
    public void Enter_repeat_does_not_commit_twice()
    {
        var repeat = Open(Key(VirtualKeys.Return, true, ModifierState.AltOnly, repeat: true));
        Assert.Equal(HookCommand.Swallow, HookInterpreter.Interpret(repeat).Command);
    }

    [Fact]
    public void Tab_key_up_is_swallowed_while_open()
    {
        var up = Open(Key(VirtualKeys.Tab, false, ModifierState.AltOnly));
        Assert.Equal(HookCommand.Swallow, HookInterpreter.Interpret(up).Command);
    }

    [Fact]
    public void Search_mode_lets_ime_keys_through_but_keeps_the_hotkey()
    {
        var search = Open(Key(VirtualKeys.A, true, ModifierState.None)) with { SearchMode = true, GestureLatched = false };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(search).Command);
        var enter = search with { VirtualKey = VirtualKeys.Return };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(enter).Command);
        var esc = search with { VirtualKey = VirtualKeys.Escape };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(esc).Command);
        var tab = search with { VirtualKey = VirtualKeys.Tab, Modifiers = ModifierState.AltOnly };
        Assert.Equal(HookCommand.Cycle, HookInterpreter.Interpret(tab).Command);
        var close = search with { VirtualKey = VirtualKeys.W, Modifiers = new ModifierState(false, true, false, false) };
        Assert.Equal(HookCommand.CloseWindow, HookInterpreter.Interpret(close).Command);
        var del = search with { VirtualKey = VirtualKeys.Delete, Modifiers = ModifierState.None };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(del).Command);
    }
}

public class GestureTransitionTests
{
    [Fact]
    public void First_tab_opens_and_second_tab_cycles()
    {
        var first = HookInterpreter.Interpret(new HookInput
        {
            Hotkey = HotkeyChord.AltTab(),
            VirtualKey = VirtualKeys.Tab,
            IsKeyDown = true,
            Modifiers = ModifierState.AltOnly
        });
        var opened = GestureTransitions.Apply(GestureSnapshot.Idle, first, null, false, ModifierKeys.Alt);
        Assert.IsType<UiOp.Open>(opened.Op);
        Assert.Equal(1, ((UiOp.Open)opened.Op!).Delta);
        Assert.True(opened.Next.SwitcherOpen);
        Assert.True(opened.Next.GestureLatched);
        Assert.Equal(ModifierKeys.Alt, opened.Next.LatchedTrigger);

        var secondInput = opened.Next.ToHookInput(HotkeyChord.AltTab(), false, false, false, VirtualKeys.Tab, true, false, ModifierState.AltOnly);
        var second = HookInterpreter.Interpret(secondInput);
        var cycled = GestureTransitions.Apply(opened.Next, second, null, true, ModifierKeys.Alt);
        Assert.IsType<UiOp.Cycle>(cycled.Op);
        Assert.True(cycled.Next.GestureLatched);
    }

    [Fact]
    public void Alt_up_commits_and_escape_arms_a_pending_release()
    {
        var open = GestureSnapshot.Idle with
        {
            SwitcherOpen = true,
            GestureLatched = true,
            LatchedTrigger = ModifierKeys.Alt
        };
        var commit = GestureTransitions.Apply(open, HookResult.Commit(), null, false, ModifierKeys.Alt);
        Assert.IsType<UiOp.Commit>(commit.Op);
        Assert.Equal(ModifierKeys.Alt, ((UiOp.Commit)commit.Op!).Trigger);
        Assert.False(commit.Next.SwitcherOpen);

        var cancel = GestureTransitions.Apply(open, HookResult.Cancel(), null, triggerStillDown: true, ModifierKeys.Alt);
        Assert.IsType<UiOp.Cancel>(cancel.Op);
        Assert.Equal(ModifierKeys.Alt, cancel.Next.PendingTriggerRelease);

        var swallowed = GestureTransitions.Apply(cancel.Next, HookResult.Swallow(clearAwaiting: true), null, false, ModifierKeys.None);
        Assert.Equal(ModifierKeys.None, swallowed.Next.PendingTriggerRelease);
        Assert.Null(swallowed.Op);
    }

    [Fact]
    public void Typing_updates_query_length_and_arrows_do_not_drop_the_latch()
    {
        var open = GestureSnapshot.Idle with
        {
            SwitcherOpen = true,
            GestureLatched = true,
            LatchedTrigger = ModifierKeys.Alt
        };
        var typed = GestureTransitions.Apply(open, HookResult.Type(), "a", true, ModifierKeys.Alt);
        Assert.Equal(1, typed.Next.QueryLength);
        Assert.IsType<UiOp.AppendQuery>(typed.Op);
        var arrow = GestureTransitions.Apply(typed.Next, HookResult.Cycle(-1, latch: false), null, true, ModifierKeys.Alt);
        Assert.True(arrow.Next.GestureLatched);
        Assert.Equal(ModifierKeys.Alt, arrow.Next.LatchedTrigger);
    }

    [Fact]
    public void Enter_search_detaches_the_gesture()
    {
        var open = GestureSnapshot.Idle with
        {
            SwitcherOpen = true,
            GestureLatched = true,
            LatchedTrigger = ModifierKeys.Alt,
            QueryLength = 2
        };
        var entered = GestureTransitions.Apply(open, HookResult.EnterSearch(), null, triggerStillDown: true, ModifierKeys.Alt);
        Assert.True(entered.Next.SearchMode);
        Assert.False(entered.Next.GestureLatched);
        Assert.Equal(ModifierKeys.Alt, entered.Next.PendingTriggerRelease);
        Assert.Equal(ModifierKeys.Alt, ((UiOp.EnterSearch)entered.Op!).Trigger);
        Assert.Equal(2, entered.Next.QueryLength);
    }

    [Fact]
    public void Quick_commit_only_for_an_empty_latched_gesture()
    {
        var snap = GestureSnapshot.Idle with
        {
            SwitcherOpen = true,
            GestureLatched = true,
            LatchedTrigger = ModifierKeys.Alt
        };
        Assert.True(GestureTransitions.ShouldQuickCommit(snap, triggerIsDown: false, pendingShow: true, moreOpsQueued: false));
        Assert.False(GestureTransitions.ShouldQuickCommit(snap, triggerIsDown: true, pendingShow: true, moreOpsQueued: false));
        Assert.False(GestureTransitions.ShouldQuickCommit(snap with { QueryLength = 1 }, false, true, false));
        Assert.False(GestureTransitions.ShouldQuickCommit(snap, false, true, moreOpsQueued: true));
        Assert.False(GestureTransitions.ShouldQuickCommit(snap with { GestureLatched = false, LatchedTrigger = ModifierKeys.None }, false, true, false));
    }

    [Fact]
    public void Sticky_release_opens_search_without_arming_another_key_up()
    {
        var open = GestureSnapshot.Idle with
        {
            SwitcherOpen = true,
            GestureLatched = true,
            LatchedTrigger = ModifierKeys.Alt,
            QueryLength = 1
        };
        var released = GestureTransitions.Apply(open, HookResult.ReleaseHold(), null, triggerStillDown: true, ModifierKeys.Alt);
        var hold = Assert.IsType<UiOp.ReleaseHold>(released.Op);
        Assert.Equal(ModifierKeys.Alt, hold.Trigger);
        Assert.True(released.Next.SwitcherOpen);
        Assert.True(released.Next.SearchMode);
        Assert.False(released.Next.GestureLatched);
        Assert.Equal(ModifierKeys.None, released.Next.LatchedTrigger);
        Assert.Equal(ModifierKeys.None, released.Next.PendingTriggerRelease);
        Assert.Equal(1, released.Next.QueryLength);
        Assert.False(GestureTransitions.ShouldQuickCommit(released.Next, triggerIsDown: false, pendingShow: true, moreOpsQueued: false));

        var cycleInput = released.Next.ToHookInput(
            HotkeyChord.AltTab(), false, false, false, VirtualKeys.Tab, true, false, ModifierState.AltOnly, stickySwitcher: true, switcherShown: true);
        var cycle = HookInterpreter.Interpret(cycleInput);
        Assert.Equal(HookCommand.Cycle, cycle.Command);
        var cycled = GestureTransitions.Apply(released.Next, cycle, null, true, ModifierKeys.Alt);
        Assert.True(cycled.Next.SearchMode);
        Assert.True(cycled.Next.SwitcherOpen);
        Assert.False(cycled.Next.GestureLatched);

        var letter = released.Next.ToHookInput(
            HotkeyChord.AltTab(), false, false, false, VirtualKeys.A, true, false, ModifierState.None, stickySwitcher: true, switcherShown: true);
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(letter).Command);
        var enter = letter with { VirtualKey = VirtualKeys.Return };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(enter).Command);
        var arrows = letter with { VirtualKey = VirtualKeys.Down };
        Assert.Equal(HookCommand.Pass, HookInterpreter.Interpret(arrows).Command);
    }

    [Fact]
    public void Instant_tap_is_classic_behavior_unless_sticky_opts_in()
    {
        Assert.True(GestureTransitions.AllowsInstantTap(stickySwitcher: false, quickTapSwitches: false));
        Assert.True(GestureTransitions.AllowsInstantTap(stickySwitcher: false, quickTapSwitches: true));
        Assert.False(GestureTransitions.AllowsInstantTap(stickySwitcher: true, quickTapSwitches: false));
        Assert.True(GestureTransitions.AllowsInstantTap(stickySwitcher: true, quickTapSwitches: true));
    }

    [Fact]
    public void Tray_open_is_sticky_and_a_second_request_brings_it_forward()
    {
        var first = GestureTransitions.OpenSticky(GestureSnapshot.Idle);
        Assert.IsType<UiOp.Open>(first.Op);
        Assert.Equal(0, ((UiOp.Open)first.Op!).Delta);
        Assert.False(first.Next.GestureLatched);
        var second = GestureTransitions.OpenSticky(first.Next);
        Assert.IsType<UiOp.BringToFront>(second.Op);
    }
}
