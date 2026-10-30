using AltPane.Core.Input;
using AltPane.Core.Models;

namespace AltPane.Core.Dialogs;

public static class FileDialogHotkey
{
    public static bool Matches(HotkeyChord? chord, int virtualKey, ModifierState mods)
    {
        if (!HotkeyLogic.IsValid(chord)) return false;
        if (virtualKey != chord!.VirtualKey) return false;
        var flags = chord.Modifiers;
        if (mods.Alt != flags.HasFlag(ModifierKeys.Alt)) return false;
        if (mods.Ctrl != flags.HasFlag(ModifierKeys.Control)) return false;
        if (mods.Shift != flags.HasFlag(ModifierKeys.Shift)) return false;
        if (mods.Win != flags.HasFlag(ModifierKeys.Win)) return false;
        return true;
    }
}
