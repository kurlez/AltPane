namespace AltPane.Core.Dialogs;

public interface IDialogSurface
{
    string FileName { get; set; }
    void SubmitFileName();
    string CurrentLocation { get; }
    bool TryNavigateAddress(string folder);
}

public readonly record struct NavigationResult(bool Succeeded, bool UsedAddressBar, string RestoredName);

/// <summary>
/// Types a folder into the filename edit and submits it (the dialog's Enter).
/// When that does not change the current folder, the address bar is tried.
/// A Save dialog gets the previous filename back so the typed name is not lost.
/// </summary>
public static class DialogNavigation
{
    public static string SubmitText(string? folder)
    {
        var normal = FolderPath.Normalize(folder);
        if (normal.Length == 0) return "";
        return normal.EndsWith('\\') ? normal : normal + "\\";
    }

    public static string FileNameToRestore(string? previous, bool saveDialog)
    {
        if (!saveDialog || string.IsNullOrWhiteSpace(previous)) return "";
        var trimmed = previous.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1].Trim();
        if (trimmed.Length == 0) return "";
        var asPath = trimmed.Replace('/', '\\');
        if (asPath.EndsWith('\\')) return "";
        if (asPath.Length == 2 && asPath[1] == ':') return "";
        if (asPath.Contains('\\') || asPath.Contains('/'))
        {
            var name = LeafName(asPath);
            if (name.Length == 0 || !HasExtension(name)) return "";
            return name;
        }
        return trimmed;
    }

    static string LeafName(string path)
    {
        var slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return slash < 0 ? path : path[(slash + 1)..];
    }

    static bool HasExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1;
    }

    public static NavigationResult Go(IDialogSurface surface, string? folder, bool saveDialog)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var target = FolderPath.Normalize(folder);
        var previous = surface.FileName ?? "";
        if (target.Length == 0)
            return new NavigationResult(false, false, "");

        var before = surface.CurrentLocation ?? "";
        surface.FileName = SubmitText(target);
        surface.SubmitFileName();
        var after = surface.CurrentLocation ?? "";
        var usedAddress = false;
        var landed = FolderPath.Landed(before, after, target);
        if (!landed)
        {
            usedAddress = surface.TryNavigateAddress(target);
            after = surface.CurrentLocation ?? "";
            landed = usedAddress && FolderPath.Landed(before, after, target);
        }

        if (!landed)
        {
            surface.FileName = previous;
            return new NavigationResult(false, usedAddress, "");
        }

        var restored = FileNameToRestore(previous, saveDialog);
        if (restored.Length > 0)
            surface.FileName = restored;
        return new NavigationResult(true, usedAddress, restored);
    }
}
