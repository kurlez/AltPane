namespace AltPane.Core.Input;

/// <summary>
/// Esc has to work whenever the switcher window is still on screen.
/// A click-outside or deactivate path can clear the gesture snapshot before the
/// window finishes hiding. That mismatch is repaired for this key only.
/// </summary>
public static class EscVisibility
{
    public readonly record struct State(bool SwitcherOpen, bool SearchMode, int QueryLength, bool Repaired);

    public static State Repair(bool snapshotOpen, bool windowVisible, bool searchMode, int queryLength)
    {
        var length = Math.Max(0, queryLength);
        if (snapshotOpen || !windowVisible)
            return new State(snapshotOpen, searchMode, length, false);
        return new State(true, searchMode || length > 0, length, true);
    }
}
