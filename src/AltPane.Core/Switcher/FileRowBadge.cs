namespace AltPane.Core.Switcher;

/// <summary>
/// File rows show the numeric row badge only. The pinyin stream of a file name
/// is not a badge. Window and application badges stay on <see cref="NameIndex"/>.
/// </summary>
public static class FileRowBadge
{
    public const int MaxLength = 4;

    public static string Index(string? fileName) => "";

    /// <summary>A leftover letter code is capped so a filename cannot fill the row.</summary>
    public static string Cap(string? code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        return code.Length <= MaxLength ? code : code[..MaxLength];
    }
}
