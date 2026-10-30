namespace AltPane.Core.Dialogs;

public enum FileDialogKind
{
    None,
    CommonItem,
    Legacy
}

/// <summary>One control discovered under a top-level dialog. The probe fills this; classification does not touch Win32.</summary>
public readonly record struct DialogControl(string ClassName, int ControlId);

/// <summary>A dialog as seen by the classifier: class, title, and descendant controls.</summary>
public sealed class DialogSnapshot
{
    public string ClassName { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyList<DialogControl> Descendants { get; init; } = Array.Empty<DialogControl>();
}

/// <summary>
/// Decides whether a top-level <c>#32770</c> is a file dialog.
/// A common item dialog hosts <c>DUIViewWndClassName</c> and <c>DirectUIHWND</c> plus an address bar.
/// A legacy GetOpenFileName dialog has the filename control (0x47C or edt1 0x480) and a file list.
/// Message boxes are the same class and are ignored.
/// </summary>
public static class FileDialogClassifier
{
    public const string DialogClass = "#32770";
    public const string DuiViewClass = "DUIViewWndClassName";
    public const string DirectUiClass = "DirectUIHWND";
    /// <summary>cmb13, the filename combo in the Explorer-style and common item dialogs. 1148.</summary>
    public const int FileNameControlId = 0x47C;
    /// <summary>edt1, the filename edit in the old-style dialog.</summary>
    public const int LegacyFileNameEditId = 0x480;
    /// <summary>lst1, the file list in the old-style dialog.</summary>
    public const int FileListId = 0x460;

    static readonly HashSet<string> AddressClasses = new(StringComparer.Ordinal)
    {
        "Breadcrumb Parent",
        "Address Band Root",
        "TravelBand",
        "UniversalSearchBand",
        "ComboBoxEx32"
    };

    public static FileDialogKind Classify(DialogSnapshot? snapshot)
    {
        if (snapshot is null) return FileDialogKind.None;
        if (!string.Equals(snapshot.ClassName, DialogClass, StringComparison.Ordinal))
            return FileDialogKind.None;

        var descendants = snapshot.Descendants ?? Array.Empty<DialogControl>();
        if (IsCommonItem(descendants)) return FileDialogKind.CommonItem;
        if (HasClass(descendants, DuiViewClass)) return FileDialogKind.None;
        if (IsLegacy(descendants)) return FileDialogKind.Legacy;
        return FileDialogKind.None;
    }

    public static bool IsSaveDialog(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (title.Contains("另存", StringComparison.Ordinal)) return true;
        if (title.Contains("保存", StringComparison.Ordinal)) return true;
        var index = 0;
        while ((index = title.IndexOf("save", index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(title[index - 1]);
            if (before) return true;
            index += 4;
        }
        return false;
    }

    static bool IsCommonItem(IReadOnlyList<DialogControl> descendants)
    {
        if (!HasClass(descendants, DuiViewClass) || !HasClass(descendants, DirectUiClass))
            return false;
        if (descendants.Any(static control => AddressClasses.Contains(control.ClassName)))
            return true;
        // The breadcrumb is a toolbar. Require the filename combo so a DUI dialog
        // that merely has a toolbar is not treated as a file dialog.
        return HasClass(descendants, "ToolbarWindow32") && HasId(descendants, FileNameControlId);
    }

    static bool IsLegacy(IReadOnlyList<DialogControl> descendants)
    {
        var hasName = HasId(descendants, FileNameControlId) || HasId(descendants, LegacyFileNameEditId);
        if (!hasName) return false;
        return HasId(descendants, FileListId)
            || HasClass(descendants, "SysListView32")
            || HasClass(descendants, "SHELLDLL_DefView");
    }

    static bool HasClass(IReadOnlyList<DialogControl> descendants, string className)
    {
        foreach (var control in descendants)
        {
            if (string.Equals(control.ClassName, className, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    static bool HasId(IReadOnlyList<DialogControl> descendants, int id)
    {
        foreach (var control in descendants)
        {
            if (control.ControlId == id) return true;
        }
        return false;
    }
}
