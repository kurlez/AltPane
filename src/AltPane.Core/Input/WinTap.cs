namespace AltPane.Core.Input;

/// <summary>
/// A lone Win press-and-release, versus a Win chord. The hook uses this when
/// “open AltPane on a single Win key” is on. Win+E, Win+R, Win+D, Win+Shift+S,
/// and Win+Tab are chords: another key went down while Win was down, or was
/// already down, so the Win key-up is left for Windows. A lone key-up asks the
/// hook to swallow that key-up and inject an unassigned key (VK_E8, or VK_FF)
/// while Win is still logically down, so Start does not open.
/// </summary>
public readonly record struct WinTapState(bool LeftDown, bool RightDown, bool OtherKey)
{
    public bool AnyDown => LeftDown || RightDown;
    public static WinTapState Idle => new(false, false, false);
}

public readonly record struct WinTapDecision(WinTapState Next, bool OpenApps, bool SwallowUp, bool InjectDummy);

public static class WinTap
{
    /// <summary>Same unassigned key <see cref="WinChordGuard"/> already injects.</summary>
    public const int DummyVirtualKey = WinChordGuard.DummyVirtualKey;

    /// <summary>The other unassigned value Windows treats the same way.</summary>
    public const int AlternateDummyVirtualKey = 0xFF;

    public static bool IsWin(int vk) => vk is VirtualKeys.LWin or VirtualKeys.RWin;

    public static bool IsIgnoredDummy(int vk) => vk is DummyVirtualKey or AlternateDummyVirtualKey;

    public static bool CountsAsCombo(int vk) => !IsWin(vk) && !IsIgnoredDummy(vk);

    public static WinTapDecision OnKey(WinTapState state, int vk, bool isDown, bool otherAlreadyDown, bool enabled)
    {
        if (IsIgnoredDummy(vk))
            return new(state, false, false, false);

        if (IsWin(vk) && isDown)
        {
            var repeat = vk == VirtualKeys.LWin ? state.LeftDown : state.RightDown;
            if (repeat)
                return new(state, false, false, false);
            var next = new WinTapState(
                state.LeftDown || vk == VirtualKeys.LWin,
                state.RightDown || vk == VirtualKeys.RWin,
                state.OtherKey || otherAlreadyDown);
            return new(next, false, false, false);
        }

        if (IsWin(vk) && !isDown)
        {
            var left = vk == VirtualKeys.LWin ? false : state.LeftDown;
            var right = vk == VirtualKeys.RWin ? false : state.RightDown;
            if (left || right)
                return new(new WinTapState(left, right, state.OtherKey), false, false, false);

            var lone = enabled && state.AnyDown && !state.OtherKey;
            return new(WinTapState.Idle, lone, lone, lone);
        }

        if (isDown && state.AnyDown && CountsAsCombo(vk))
            return new(state with { OtherKey = true }, false, false, false);

        return new(state, false, false, false);
    }
}
