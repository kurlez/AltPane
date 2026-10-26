using AltPane.Core.Models;

namespace AltPane.Core.Input;

public enum ChordKind
{
    None,
    Primary,
    WinTab
}

public readonly record struct ModifierState(bool Alt, bool Ctrl, bool Shift, bool Win)
{
    public static ModifierState None => new(false, false, false, false);
    public static ModifierState AltOnly => new(true, false, false, false);
    public static ModifierState AltShift => new(true, false, true, false);
}

public static class HotkeyLogic
{
    public static bool IsModifierKey(int vk) => vk is
        VirtualKeys.Shift or VirtualKeys.Control or VirtualKeys.Menu or
        VirtualKeys.LWin or VirtualKeys.RWin or
        VirtualKeys.LShift or VirtualKeys.RShift or
        VirtualKeys.LControl or VirtualKeys.RControl or
        VirtualKeys.LMenu or VirtualKeys.RMenu;

    public static bool IsTextualKey(int vk) =>
        vk is >= 0x30 and <= 0x39 ||
        vk is >= 0x41 and <= 0x5A ||
        vk is >= 0x60 and <= 0x6F ||
        vk is >= 0xBA and <= 0xC0 ||
        vk is >= 0xDB and <= 0xDF ||
        vk == 0xE2;

    public static ModifierKeys TriggerModifier(HotkeyChord chord)
    {
        if (chord.Modifiers.HasFlag(ModifierKeys.Alt)) return ModifierKeys.Alt;
        if (chord.Modifiers.HasFlag(ModifierKeys.Win)) return ModifierKeys.Win;
        if (chord.Modifiers.HasFlag(ModifierKeys.Control)) return ModifierKeys.Control;
        if (chord.Modifiers.HasFlag(ModifierKeys.Shift)) return ModifierKeys.Shift;
        return ModifierKeys.None;
    }

    public static bool IsTriggerModifierKey(int vk, ModifierKeys trigger) => trigger switch
    {
        ModifierKeys.Alt => vk is VirtualKeys.Menu or VirtualKeys.LMenu or VirtualKeys.RMenu,
        ModifierKeys.Control => vk is VirtualKeys.Control or VirtualKeys.LControl or VirtualKeys.RControl,
        ModifierKeys.Shift => vk is VirtualKeys.Shift or VirtualKeys.LShift or VirtualKeys.RShift,
        ModifierKeys.Win => vk is VirtualKeys.LWin or VirtualKeys.RWin,
        _ => false
    };

    public static bool ModifiersSatisfied(HotkeyChord hotkey, ModifierState mods)
    {
        var needAlt = hotkey.Modifiers.HasFlag(ModifierKeys.Alt);
        var needCtrl = hotkey.Modifiers.HasFlag(ModifierKeys.Control);
        var needWin = hotkey.Modifiers.HasFlag(ModifierKeys.Win);
        var needShift = hotkey.Modifiers.HasFlag(ModifierKeys.Shift);
        if (mods.Alt != needAlt) return false;
        if (mods.Ctrl != needCtrl) return false;
        if (mods.Win != needWin) return false;
        if (needShift && !mods.Shift) return false;
        return true;
    }

    public static ChordKind Match(HotkeyChord hotkey, bool captureWinTab, int vk, ModifierState mods)
    {
        if (vk == hotkey.VirtualKey && ModifiersSatisfied(hotkey, mods))
            return ChordKind.Primary;
        if (captureWinTab && vk == VirtualKeys.Tab && mods.Win && !mods.Alt && !mods.Ctrl)
            return ChordKind.WinTab;
        return ChordKind.None;
    }

    public static bool IsReverse(ChordKind kind, HotkeyChord hotkey, ModifierState mods)
    {
        if (kind == ChordKind.None || !mods.Shift) return false;
        if (kind == ChordKind.WinTab) return true;
        return !hotkey.Modifiers.HasFlag(ModifierKeys.Shift);
    }

    public static ModifierKeys TriggerFor(ChordKind kind, HotkeyChord hotkey) =>
        kind == ChordKind.WinTab ? ModifierKeys.Win : TriggerModifier(hotkey);

    public static bool IsValid(HotkeyChord? chord)
    {
        if (chord is null) return false;
        if (chord.VirtualKey is <= 0 or >= 256) return false;
        if (IsModifierKey(chord.VirtualKey)) return false;
        if (chord.Modifiers == ModifierKeys.None) return false;
        return true;
    }

    public static string Format(HotkeyChord chord)
    {
        var parts = new List<string>(5);
        if (chord.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (chord.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (chord.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (chord.Modifiers.HasFlag(ModifierKeys.Win)) parts.Add("Win");
        parts.Add(KeyName(chord.VirtualKey));
        return string.Join(" + ", parts);
    }

    public static string TriggerDisplayName(ModifierKeys trigger) => trigger switch
    {
        ModifierKeys.Control => "Ctrl",
        ModifierKeys.Shift => "Shift",
        ModifierKeys.Win => "Win",
        _ => "Alt"
    };

    public static string KeyName(int vk) => vk switch
    {
        VirtualKeys.Tab => "Tab",
        VirtualKeys.Escape => "Esc",
        VirtualKeys.Return => "Enter",
        VirtualKeys.Space => "Space",
        VirtualKeys.Back => "Backspace",
        VirtualKeys.Delete => "Delete",
        VirtualKeys.Left => "Left",
        VirtualKeys.Up => "Up",
        VirtualKeys.Right => "Right",
        VirtualKeys.Down => "Down",
        VirtualKeys.Home => "Home",
        VirtualKeys.End => "End",
        VirtualKeys.Prior => "PageUp",
        VirtualKeys.Next => "PageDown",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        0xC0 => "`",
        0xBD => "-",
        0xBB => "=",
        0xDB => "[",
        0xDD => "]",
        0xDC => "\\",
        0xBA => ";",
        0xDE => "'",
        0xBC => ",",
        0xBE => ".",
        0xBF => "/",
        _ => $"VK_{vk:X2}"
    };
}
