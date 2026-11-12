namespace AltPane.Core.Input;

/// <summary>
/// OLE formats used when Explorer or the desktop drags files. The switcher
/// only arms hover-to-activate for these; a text or image drag is ignored.
/// Formats are inspected by name only. The data itself is never read, so the
/// drag is not consumed.
/// </summary>
public static class FileDragFormats
{
    public static bool IsFileDrag(IEnumerable<string?>? formats)
    {
        if (formats is null) return false;
        foreach (var format in formats)
        {
            if (IsFileFormat(format)) return true;
        }
        return false;
    }

    public static bool IsFileFormat(string? format)
    {
        if (string.IsNullOrEmpty(format)) return false;
        return format.Equals("FileDrop", StringComparison.OrdinalIgnoreCase)
            || format.Equals("FileNameW", StringComparison.OrdinalIgnoreCase)
            || format.Equals("FileName", StringComparison.OrdinalIgnoreCase)
            || format.Equals("FileGroupDescriptorW", StringComparison.OrdinalIgnoreCase)
            || format.Equals("FileGroupDescriptor", StringComparison.OrdinalIgnoreCase)
            || format.Equals("Shell IDList Array", StringComparison.OrdinalIgnoreCase);
    }
}
