using AltPane.Core.Input;
using Xunit;

namespace AltPane.Tests;

public class WinTapTests
{
    [Fact]
    public void A_lone_left_or_right_win_tap_opens_apps_and_suppresses_start()
    {
        var left = Release(Press(WinTapState.Idle, VirtualKeys.LWin));
        Assert.True(left.OpenApps);
        Assert.True(left.SwallowUp);
        Assert.True(left.InjectDummy);
        Assert.Equal(WinTapState.Idle, left.Next);
        Assert.Equal(0xE8, WinTap.DummyVirtualKey);
        Assert.Equal(0xFF, WinTap.AlternateDummyVirtualKey);

        var right = Release(Press(WinTapState.Idle, VirtualKeys.RWin), VirtualKeys.RWin);
        Assert.True(right.OpenApps);
        Assert.True(right.SwallowUp);
        Assert.True(right.InjectDummy);
    }

    [Fact]
    public void Win_chords_pass_through_including_shift_and_a_key_already_held()
    {
        var winE = Release(Press(Press(WinTapState.Idle, VirtualKeys.LWin), VirtualKeys.E));
        Assert.False(winE.OpenApps);
        Assert.False(winE.SwallowUp);
        Assert.False(winE.InjectDummy);

        var winR = Release(Press(Press(WinTapState.Idle, VirtualKeys.LWin), VirtualKeys.R));
        Assert.False(winR.OpenApps);

        var down = Press(WinTapState.Idle, VirtualKeys.LWin);
        var shift = WinTap.OnKey(down, VirtualKeys.LShift, true, false, true);
        var shot = WinTap.OnKey(shift.Next, VirtualKeys.S, true, false, true);
        var winShiftS = Release(shot.Next);
        Assert.False(winShiftS.OpenApps);
        Assert.False(winShiftS.SwallowUp);

        var held = WinTap.OnKey(WinTapState.Idle, VirtualKeys.LWin, true, otherAlreadyDown: true, enabled: true);
        var tap = Release(held.Next);
        Assert.False(tap.OpenApps);
        Assert.False(tap.SwallowUp);
    }

    [Fact]
    public void The_setting_off_leaves_a_lone_win_tap_alone_and_a_repeat_does_not_open_twice()
    {
        var down = WinTap.OnKey(WinTapState.Idle, VirtualKeys.LWin, true, false, enabled: false);
        var up = WinTap.OnKey(down.Next, VirtualKeys.LWin, false, false, enabled: false);
        Assert.False(up.OpenApps);
        Assert.False(up.SwallowUp);
        Assert.Equal(WinTapState.Idle, up.Next);

        var first = Press(WinTapState.Idle, VirtualKeys.LWin);
        var repeat = WinTap.OnKey(first, VirtualKeys.LWin, true, false, true);
        Assert.Equal(first, repeat.Next);
        Assert.False(repeat.OpenApps);
        var once = Release(repeat.Next);
        Assert.True(once.OpenApps);
    }

    [Fact]
    public void Releasing_one_of_two_win_keys_waits_and_dummy_keys_are_not_a_chord()
    {
        var left = Press(WinTapState.Idle, VirtualKeys.LWin);
        var right = Press(left, VirtualKeys.RWin);
        var one = WinTap.OnKey(right, VirtualKeys.LWin, false, false, true);
        Assert.False(one.OpenApps);
        Assert.True(one.Next.RightDown);
        var both = WinTap.OnKey(one.Next, VirtualKeys.RWin, false, false, true);
        Assert.True(both.OpenApps);

        var dummy = WinTap.OnKey(left, WinTap.DummyVirtualKey, true, false, true);
        Assert.Equal(left, dummy.Next);
        var alternate = WinTap.OnKey(dummy.Next, WinTap.AlternateDummyVirtualKey, true, false, true);
        Assert.True(Release(alternate.Next).OpenApps);
    }

    [Fact]
    public void A_key_that_went_down_and_up_while_win_was_down_is_still_a_chord()
    {
        var down = Press(WinTapState.Idle, VirtualKeys.LWin);
        const int D = 0x44;
        var otherDown = WinTap.OnKey(down, D, true, false, true);
        var otherUp = WinTap.OnKey(otherDown.Next, D, false, false, true);
        Assert.True(otherUp.Next.OtherKey);
        var up = Release(otherUp.Next);
        Assert.False(up.OpenApps);
        Assert.False(up.SwallowUp);
    }

    static WinTapState Press(WinTapState state, int vk) =>
        WinTap.OnKey(state, vk, true, false, true).Next;

    static WinTapDecision Release(WinTapState state, int vk = VirtualKeys.LWin) =>
        WinTap.OnKey(state, vk, false, false, true);
}
