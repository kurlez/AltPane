namespace AltPane.Core.Input;

/// <summary>
/// Ctrl+1 through Ctrl+9 select the first nine visible rows. Ctrl+0 selects the tenth.
/// The main row and the numeric keypad use the same indexes.
/// </summary>
public static class DigitSelect
{
    public static int? Index(int virtualKey)
    {
        if (virtualKey is >= 0x31 and <= 0x39) return virtualKey - 0x31;
        if (virtualKey == 0x30) return 9;
        if (virtualKey is >= 0x61 and <= 0x69) return virtualKey - 0x61;
        if (virtualKey == 0x60) return 9;
        return null;
    }

    /// <summary>The badge drawn on that row. Empty past the tenth row.</summary>
    public static string Label(int index) => index switch
    {
        >= 0 and <= 8 => (index + 1).ToString(),
        9 => "0",
        _ => ""
    };
}
