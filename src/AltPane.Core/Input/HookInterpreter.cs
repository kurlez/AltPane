using AltPane.Core.Models;

namespace AltPane.Core.Input;

public enum HookCommand
{
    Pass,
    Swallow,
    Cycle,
    Commit,
    Cancel,
    Type,
    Backspace,
    CloseWindow,
    KillProcess,
    Minimize,
    Maximize,
    Restore,
    EnterSearch,
    Home,
    End,
    Page,
    SpaceAction,
    ReleaseHold,
    AdminLaunch
}

public readonly record struct HookResult(HookCommand Command, int Delta, bool LatchGesture, bool ClearAwaitingRelease)
{
    public static HookResult Pass() => new(HookCommand.Pass, 0, false, false);
    public static HookResult Swallow(bool clearAwaiting = false) => new(HookCommand.Swallow, 0, false, clearAwaiting);
    public static HookResult Cycle(int delta, bool latch) => new(HookCommand.Cycle, delta, latch, false);
    public static HookResult Commit() => new(HookCommand.Commit, 0, false, false);
    public static HookResult Cancel() => new(HookCommand.Cancel, 0, false, false);
    public static HookResult Type() => new(HookCommand.Type, 0, false, false);
    public static HookResult Backspace() => new(HookCommand.Backspace, 0, false, false);
    public static HookResult Close() => new(HookCommand.CloseWindow, 0, false, false);
    public static HookResult Kill() => new(HookCommand.KillProcess, 0, false, false);
    public static HookResult Minimize() => new(HookCommand.Minimize, 0, false, false);
    public static HookResult Maximize() => new(HookCommand.Maximize, 0, false, false);
    public static HookResult Restore() => new(HookCommand.Restore, 0, false, false);
    public static HookResult EnterSearch() => new(HookCommand.EnterSearch, 0, false, false);
    public static HookResult Home() => new(HookCommand.Home, 0, false, false);
    public static HookResult End() => new(HookCommand.End, 0, false, false);
    public static HookResult Page(int direction) => new(HookCommand.Page, direction < 0 ? -1 : 1, false, false);
    public static HookResult Space() => new(HookCommand.SpaceAction, 0, false, false);
    public static HookResult ReleaseHold() => new(HookCommand.ReleaseHold, 0, false, false);
    public static HookResult AdminLaunch() => new(HookCommand.AdminLaunch, 0, false, false);
}

public readonly record struct HookInput
{
    public bool Paused { get; init; }
    public bool RecordingHotkey { get; init; }
    public bool SwitcherOpen { get; init; }
    public bool SearchMode { get; init; }
    public bool GestureLatched { get; init; }
    public bool HasSearchQuery { get; init; }
    public ModifierKeys LatchedTrigger { get; init; }
    public ModifierKeys PendingTriggerRelease { get; init; }
    public HotkeyChord? Hotkey { get; init; }
    public bool CaptureWinTab { get; init; }
    /// <summary>When true, releasing the trigger leaves the switcher open instead of activating.</summary>
    public bool StickySwitcher { get; init; }
    /// <summary>With <see cref="StickySwitcher"/>, a tap released before the window is shown still activates.</summary>
    public bool QuickTapSwitches { get; init; }
    /// <summary>The switcher window is already on screen. A hidden session is the quick-tap window.</summary>
    public bool SwitcherShown { get; init; }
    public int VirtualKey { get; init; }
    public bool IsKeyDown { get; init; }
    public bool IsRepeat { get; init; }
    public ModifierState Modifiers { get; init; }
}

/// <summary>
/// Pure keyboard policy. The Win32 hook translates scan codes and then applies
/// <see cref="GestureTransitions"/>; this type never calls into the OS.
/// </summary>
public static class HookInterpreter
{
    public static HookResult Interpret(in HookInput input)
    {
        var hotkey = input.Hotkey ?? HotkeyChord.AltTab();

        // A synthetic key-up was already injected. Swallow the matching physical
        // key-up so the system does not see a second one (or open a menu).
        if (input.PendingTriggerRelease != ModifierKeys.None && !input.IsKeyDown &&
            HotkeyLogic.IsTriggerModifierKey(input.VirtualKey, input.PendingTriggerRelease))
            return HookResult.Swallow(clearAwaiting: true);

        if (input.RecordingHotkey && !input.SwitcherOpen)
            return HookResult.Pass();
        if (input.Paused && !input.SwitcherOpen)
            return HookResult.Pass();

        var kind = HotkeyLogic.Match(hotkey, input.CaptureWinTab, input.VirtualKey, input.Modifiers);
        var isHotkey = input.IsKeyDown && kind != ChordKind.None;
        var direction = HotkeyLogic.IsReverse(kind, hotkey, input.Modifiers) ? -1 : 1;

        if (!input.SwitcherOpen)
            return isHotkey ? HookResult.Cycle(direction, latch: true) : HookResult.Pass();

        if (input.SearchMode)
            return InterpretSearch(input, hotkey, isHotkey, direction);

        if (!input.IsKeyDown)
        {
            var trigger = input.LatchedTrigger != ModifierKeys.None
                ? input.LatchedTrigger
                : HotkeyLogic.TriggerModifier(hotkey);
            if (input.GestureLatched && HotkeyLogic.IsTriggerModifierKey(input.VirtualKey, trigger))
            {
                // Classic mode activates on release. Sticky mode keeps the switcher up so the
                // search box can take real focus (IME included). A quick tap that is released
                // before the window is drawn can still activate when that option is on.
                if (!input.StickySwitcher)
                    return HookResult.Commit();
                if (input.QuickTapSwitches && !input.SwitcherShown && !input.HasSearchQuery)
                    return HookResult.Commit();
                return HookResult.ReleaseHold();
            }
            if (HotkeyLogic.IsModifierKey(input.VirtualKey))
                return HookResult.Pass();
            return HookResult.Swallow();
        }

        if (isHotkey)
            return HookResult.Cycle(direction, latch: true);

        if (input.IsRepeat && IsNonRepeating(input))
            return HookResult.Swallow();

        switch (input.VirtualKey)
        {
            case VirtualKeys.Escape: return HookResult.Cancel();
            case VirtualKeys.Return when input.Modifiers.Ctrl && !input.Modifiers.Win:
                return HookResult.AdminLaunch();
            case VirtualKeys.Return: return HookResult.Commit();
            case VirtualKeys.Space when !input.HasSearchQuery: return HookResult.Space();
            case VirtualKeys.Space: return HookResult.Type();
            case VirtualKeys.Back: return HookResult.Backspace();
            case VirtualKeys.Up:
            case VirtualKeys.Left: return HookResult.Cycle(-1, latch: false);
            case VirtualKeys.Down:
            case VirtualKeys.Right: return HookResult.Cycle(1, latch: false);
            case VirtualKeys.Home: return HookResult.Home();
            case VirtualKeys.End: return HookResult.End();
            case VirtualKeys.Prior: return HookResult.Page(-1);
            case VirtualKeys.Next: return HookResult.Page(1);
            case VirtualKeys.Delete when input.Modifiers.Shift && !input.Modifiers.Ctrl:
                return HookResult.Kill();
            case VirtualKeys.Delete:
                return HookResult.Close();
        }

        if (input.Modifiers.Ctrl && !input.Modifiers.Win)
        {
            switch (input.VirtualKey)
            {
                case VirtualKeys.F: return HookResult.EnterSearch();
                case VirtualKeys.W: return HookResult.Close();
                case VirtualKeys.M when input.Modifiers.Shift: return HookResult.Maximize();
                case VirtualKeys.M: return HookResult.Minimize();
                case VirtualKeys.R: return HookResult.Restore();
                default: return HookResult.Swallow();
            }
        }

        if (HotkeyLogic.IsModifierKey(input.VirtualKey))
            return HookResult.Pass();
        if (HotkeyLogic.IsTextualKey(input.VirtualKey) && !input.Modifiers.Win)
            return HookResult.Type();
        return HookResult.Swallow();
    }

    static HookResult InterpretSearch(in HookInput input, HotkeyChord hotkey, bool isHotkey, int direction)
    {
        if (!input.IsKeyDown)
        {
            if (input.VirtualKey == hotkey.VirtualKey)
                return HookResult.Swallow();
            return HookResult.Pass();
        }

        if (isHotkey)
            return HookResult.Cycle(direction, latch: false);

        if (input.Modifiers.Ctrl && !input.Modifiers.Win)
        {
            switch (input.VirtualKey)
            {
                case VirtualKeys.F: return HookResult.EnterSearch();
                case VirtualKeys.W when !input.IsRepeat: return HookResult.Close();
                case VirtualKeys.M when input.Modifiers.Shift && !input.IsRepeat: return HookResult.Maximize();
                case VirtualKeys.M when !input.IsRepeat: return HookResult.Minimize();
                case VirtualKeys.R when !input.IsRepeat: return HookResult.Restore();
            }
        }

        if (!input.IsRepeat && input.VirtualKey == VirtualKeys.Delete && input.Modifiers.Shift && !input.Modifiers.Ctrl)
            return HookResult.Kill();
        if (input.VirtualKey == VirtualKeys.Return && input.Modifiers.Ctrl && !input.Modifiers.Win)
            return input.IsRepeat ? HookResult.Swallow() : HookResult.AdminLaunch();

        // Letters, Enter, Esc, arrows, and Backspace pass through so the text box and IME receive them.
        return HookResult.Pass();
    }

    static bool IsNonRepeating(in HookInput input)
    {
        if (input.VirtualKey is VirtualKeys.Escape or VirtualKeys.Return or VirtualKeys.Delete or VirtualKeys.Space)
            return true;
        if (!input.Modifiers.Ctrl) return false;
        return input.VirtualKey is VirtualKeys.W or VirtualKeys.M or VirtualKeys.R or VirtualKeys.F;
    }
}
