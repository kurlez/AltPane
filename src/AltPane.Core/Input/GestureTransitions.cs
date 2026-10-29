using AltPane.Core.Models;

namespace AltPane.Core.Input;

public abstract record UiOp
{
    public sealed record Open(int Delta) : UiOp;
    public sealed record Cycle(int Delta) : UiOp;
    public sealed record AppendQuery(string Text) : UiOp;
    public sealed record Backspace : UiOp;
    /// <param name="MoveToMonitor">Null follows the setting. False stays. True moves even when the setting is off.</param>
    public sealed record Commit(ModifierKeys Trigger, bool? MoveToMonitor = null) : UiOp;
    public sealed record Cancel(ModifierKeys Trigger) : UiOp;
    public sealed record EnterSearch(ModifierKeys Trigger) : UiOp;
    /// <summary>Trigger was released in sticky mode. The switcher stays open and takes the keyboard.</summary>
    public sealed record ReleaseHold(ModifierKeys Trigger) : UiOp;
    public sealed record AdminLaunch : UiOp;
    public sealed record CloseWindow : UiOp;
    public sealed record KillProcess : UiOp;
    public sealed record Minimize : UiOp;
    public sealed record Maximize : UiOp;
    public sealed record Restore : UiOp;
    public sealed record Home : UiOp;
    public sealed record End : UiOp;
    public sealed record Page(int Direction) : UiOp;
    public sealed record BringToFront : UiOp;
    public sealed record SpaceAction : UiOp;
    public sealed record ClearQuery : UiOp;
    public sealed record PinApp : UiOp;
    public sealed record DemoteApp : UiOp;
    public sealed record RestorePriority : UiOp;
    public sealed record ActivateRow(int Index) : UiOp;
    public sealed record RevealPath : UiOp;
    public sealed record CopyPath : UiOp;
    public sealed record CopyDirectory : UiOp;
    public sealed record OpenMenu : UiOp;
}

/// <summary>
/// Mirror of the switcher that the keyboard hook updates synchronously.
/// UI work is described by <see cref="UiOp"/> and runs later on the dispatcher.
/// </summary>
public readonly record struct GestureSnapshot(
    bool SwitcherOpen,
    bool SearchMode,
    bool GestureLatched,
    int QueryLength,
    ModifierKeys LatchedTrigger,
    ModifierKeys PendingTriggerRelease)
{
    public bool HasSearchQuery => QueryLength > 0;

    public static GestureSnapshot Idle => new(false, false, false, 0, ModifierKeys.None, ModifierKeys.None);

    public HookInput ToHookInput(
        HotkeyChord hotkey,
        bool captureWinTab,
        bool paused,
        bool recording,
        int vk,
        bool isKeyDown,
        bool isRepeat,
        ModifierState mods,
        bool stickySwitcher = false,
        bool quickTapSwitches = false,
        bool switcherShown = false) =>
        new()
        {
            Paused = paused,
            RecordingHotkey = recording,
            SwitcherOpen = SwitcherOpen,
            SearchMode = SearchMode,
            GestureLatched = GestureLatched,
            HasSearchQuery = HasSearchQuery,
            LatchedTrigger = LatchedTrigger,
            PendingTriggerRelease = PendingTriggerRelease,
            Hotkey = hotkey,
            CaptureWinTab = captureWinTab,
            StickySwitcher = stickySwitcher,
            QuickTapSwitches = quickTapSwitches,
            SwitcherShown = switcherShown,
            VirtualKey = vk,
            IsKeyDown = isKeyDown,
            IsRepeat = isRepeat,
            Modifiers = mods
        };
}

public readonly record struct TransitionResult(GestureSnapshot Next, UiOp? Op);

public static class GestureTransitions
{
    public static TransitionResult Apply(
        GestureSnapshot state,
        HookResult result,
        string? typed,
        bool triggerStillDown,
        ModifierKeys activationTrigger)
    {
        switch (result.Command)
        {
            case HookCommand.Pass:
                return new(state, null);
            case HookCommand.Swallow:
                return result.ClearAwaitingRelease
                    ? new(state with { PendingTriggerRelease = ModifierKeys.None }, null)
                    : new(state, null);
            case HookCommand.Cycle:
                return Cycle(state, result, activationTrigger);
            case HookCommand.Commit:
                if (!state.SwitcherOpen) return new(state, null);
                bool? move = result.MoveChoice switch
                {
                    0 => false,
                    1 => true,
                    _ => null
                };
                return new(GestureSnapshot.Idle, new UiOp.Commit(state.LatchedTrigger, move));
            case HookCommand.ClearQuery:
                if (!state.SwitcherOpen || state.QueryLength == 0) return new(state, null);
                return new(state with { QueryLength = 0 }, new UiOp.ClearQuery());
            case HookCommand.Cancel:
                if (!state.SwitcherOpen) return new(state, null);
                var cancelTrigger = state.LatchedTrigger;
                var pending = triggerStillDown && state.GestureLatched ? cancelTrigger : ModifierKeys.None;
                return new(GestureSnapshot.Idle with { PendingTriggerRelease = pending }, new UiOp.Cancel(cancelTrigger));
            case HookCommand.Type:
                if (string.IsNullOrEmpty(typed)) return new(state, null);
                return new(state with { QueryLength = state.QueryLength + typed.Length }, new UiOp.AppendQuery(typed));
            case HookCommand.Backspace:
                return new(state with { QueryLength = Math.Max(0, state.QueryLength - 1) }, new UiOp.Backspace());
            case HookCommand.EnterSearch:
                if (!state.SwitcherOpen) return new(state, null);
                var searchTrigger = state.LatchedTrigger;
                var searchPending = triggerStillDown && searchTrigger != ModifierKeys.None
                    ? searchTrigger
                    : state.PendingTriggerRelease;
                return new(state with
                {
                    SearchMode = true,
                    GestureLatched = false,
                    LatchedTrigger = ModifierKeys.None,
                    PendingTriggerRelease = searchPending
                }, new UiOp.EnterSearch(searchTrigger));
            case HookCommand.ReleaseHold:
                if (!state.SwitcherOpen) return new(state, null);
                // This event is the physical trigger key-up, and the hook swallows it.
                // Do not arm PendingTriggerRelease: the matching up will not arrive again,
                // and arming it would swallow the next Alt+Tab's key-up.
                var holdTrigger = state.LatchedTrigger != ModifierKeys.None
                    ? state.LatchedTrigger
                    : activationTrigger;
                return new(state with
                {
                    SearchMode = true,
                    GestureLatched = false,
                    LatchedTrigger = ModifierKeys.None,
                    PendingTriggerRelease = ModifierKeys.None
                }, new UiOp.ReleaseHold(holdTrigger));
            case HookCommand.CloseWindow:
                return state.SwitcherOpen ? new(state, new UiOp.CloseWindow()) : new(state, null);
            case HookCommand.KillProcess:
                return state.SwitcherOpen ? new(state, new UiOp.KillProcess()) : new(state, null);
            case HookCommand.Minimize:
                return state.SwitcherOpen ? new(state, new UiOp.Minimize()) : new(state, null);
            case HookCommand.Maximize:
                return state.SwitcherOpen ? new(state, new UiOp.Maximize()) : new(state, null);
            case HookCommand.Restore:
                return state.SwitcherOpen ? new(state, new UiOp.Restore()) : new(state, null);
            case HookCommand.Home:
                return state.SwitcherOpen ? new(state, new UiOp.Home()) : new(state, null);
            case HookCommand.End:
                return state.SwitcherOpen ? new(state, new UiOp.End()) : new(state, null);
            case HookCommand.Page:
                return state.SwitcherOpen ? new(state, new UiOp.Page(result.Delta)) : new(state, null);
            case HookCommand.SpaceAction:
                return state.SwitcherOpen ? new(state, new UiOp.SpaceAction()) : new(state, null);
            case HookCommand.AdminLaunch:
                return state.SwitcherOpen ? new(state, new UiOp.AdminLaunch()) : new(state, null);
            case HookCommand.PinApp:
                return state.SwitcherOpen ? new(state, new UiOp.PinApp()) : new(state, null);
            case HookCommand.DemoteApp:
                return state.SwitcherOpen ? new(state, new UiOp.DemoteApp()) : new(state, null);
            case HookCommand.RestorePriority:
                return state.SwitcherOpen ? new(state, new UiOp.RestorePriority()) : new(state, null);
            case HookCommand.ActivateRow:
                return state.SwitcherOpen ? new(state, new UiOp.ActivateRow(result.Delta)) : new(state, null);
            case HookCommand.RevealPath:
                return state.SwitcherOpen ? new(state, new UiOp.RevealPath()) : new(state, null);
            case HookCommand.CopyPath:
                return state.SwitcherOpen ? new(state, new UiOp.CopyPath()) : new(state, null);
            case HookCommand.CopyDirectory:
                return state.SwitcherOpen ? new(state, new UiOp.CopyDirectory()) : new(state, null);
            case HookCommand.OpenMenu:
                return state.SwitcherOpen ? new(state, new UiOp.OpenMenu()) : new(state, null);
            default:
                return new(state, null);
        }
    }

    public static TransitionResult OpenSticky(GestureSnapshot state)
    {
        if (state.SwitcherOpen) return new(state, new UiOp.BringToFront());
        return new(state with
        {
            SwitcherOpen = true,
            SearchMode = false,
            GestureLatched = false,
            QueryLength = 0,
            LatchedTrigger = ModifierKeys.None
        }, new UiOp.Open(0));
    }

    /// <summary>
    /// Classic mode activates a tap released before the switcher is drawn.
    /// Sticky mode leaves that tap open unless <paramref name="quickTapSwitches"/> is set.
    /// </summary>
    public static bool AllowsInstantTap(bool stickySwitcher, bool quickTapSwitches) =>
        !stickySwitcher || quickTapSwitches;

    public static bool ShouldQuickCommit(GestureSnapshot snap, bool triggerIsDown, bool pendingShow, bool moreOpsQueued) =>
        pendingShow
        && snap.SwitcherOpen
        && snap.GestureLatched
        && !snap.SearchMode
        && snap.QueryLength == 0
        && snap.LatchedTrigger != ModifierKeys.None
        && !triggerIsDown
        && !moreOpsQueued;

    static TransitionResult Cycle(GestureSnapshot state, HookResult result, ModifierKeys activationTrigger)
    {
        if (!state.SwitcherOpen)
        {
            return new(state with
            {
                SwitcherOpen = true,
                SearchMode = false,
                GestureLatched = result.LatchGesture,
                QueryLength = 0,
                LatchedTrigger = result.LatchGesture ? activationTrigger : ModifierKeys.None,
                PendingTriggerRelease = ModifierKeys.None
            }, new UiOp.Open(result.Delta));
        }

        return new(state with
        {
            GestureLatched = state.GestureLatched || result.LatchGesture,
            PendingTriggerRelease = ModifierKeys.None,
            LatchedTrigger = result.LatchGesture ? activationTrigger : state.LatchedTrigger
        }, new UiOp.Cycle(result.Delta));
    }
}
