namespace AltPane.Core.Input;

public enum WinGuardPhase
{
    Idle,
    /// <summary>The Win key was forwarded. No Win chord has been swallowed yet.</summary>
    Down,
    /// <summary>A Win chord was swallowed and the dummy key has been requested.</summary>
    DummySent
}

/// <summary>
/// Tracks a Win key so a swallowed Win+Tab does not look like a lone Win tap.
/// Windows opens Start on a Win key-up that had no other key, and opens Task View
/// on Win+Tab. The chord key is swallowed by the hook. This guard asks for one
/// unassigned key (VK_E8) while Win is still down, then requires a Win key-up so
/// the key is not left stuck. A plain Win tap and Win plus any other key are left alone.
/// </summary>
public readonly record struct WinGuardState(WinGuardPhase Phase, bool LeftDown, bool RightDown)
{
    public bool AnyDown => LeftDown || RightDown;
    public static WinGuardState Idle => new(WinGuardPhase.Idle, false, false);
}

public readonly record struct WinGuardDecision(WinGuardState Next, bool InjectDummy, bool ReleaseWin);

public static class WinChordGuard
{
    /// <summary>Unassigned virtual key. VK_FF is the other value Windows ignores the same way.</summary>
    public const int DummyVirtualKey = 0xE8;

    public static WinGuardDecision OnKey(WinGuardState state, int vk, bool isDown, bool consumedChord)
    {
        if (IsWin(vk) && isDown)
        {
            var phase = state.Phase == WinGuardPhase.DummySent ? WinGuardPhase.DummySent : WinGuardPhase.Down;
            return new(new WinGuardState(phase, state.LeftDown || vk == VirtualKeys.LWin, state.RightDown || vk == VirtualKeys.RWin), false, false);
        }

        if (consumedChord && isDown)
        {
            if (state.Phase == WinGuardPhase.DummySent)
                return new(state, false, false);
            return new(new WinGuardState(WinGuardPhase.DummySent, state.LeftDown, state.RightDown), true, false);
        }

        if (IsWin(vk) && !isDown)
        {
            var left = vk == VirtualKeys.LWin ? false : state.LeftDown;
            var right = vk == VirtualKeys.RWin ? false : state.RightDown;
            if (left || right)
                return new(new WinGuardState(state.Phase, left, right), false, false);

            var owed = state.Phase == WinGuardPhase.DummySent;
            return new(WinGuardState.Idle, false, owed);
        }

        return new(state, false, false);
    }

    public static bool IsWin(int vk) => vk is VirtualKeys.LWin or VirtualKeys.RWin;
}
