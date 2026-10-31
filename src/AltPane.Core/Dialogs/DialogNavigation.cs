namespace AltPane.Core.Dialogs;

public enum JumpMethod
{
    None,
    ShellBrowser,
    FileNameEdit,
    AddressBar
}

public readonly record struct JumpStep(string Method, bool Succeeded, string Detail);

public interface IDialogSurface
{
    string FileName { get; set; }
    void SubmitFileName();
    string CurrentLocation { get; }
    bool TryNavigateAddress(string folder);
    /// <summary>Common item dialogs prefer the shell view. Legacy dialogs leave this unused.</summary>
    FileDialogKind Kind => FileDialogKind.Legacy;
    /// <summary>IShellBrowser::BrowseObject, or an equivalent. False when that view is not available.</summary>
    bool TryBrowseShell(string folder) => false;
    /// <summary>Bring the dialog forward so it will accept the navigation. Fakes do nothing.</summary>
    void PrepareForJump() { }
}

public readonly record struct NavigationResult(bool Succeeded, bool UsedAddressBar, string RestoredName)
{
    public string Method { get; init; } = "";
    public string Detail { get; init; } = "";
    public IReadOnlyList<JumpStep> Steps { get; init; } = Array.Empty<JumpStep>();
}

/// <summary>
/// Which folder a jump should use: the highlighted row, otherwise the first row,
/// otherwise a typed absolute path.
/// </summary>
public static class JumpTarget
{
    public static string? Choose(string? selectedPath, string? firstRowPath, string? query)
    {
        if (!string.IsNullOrWhiteSpace(selectedPath)) return selectedPath.Trim();
        if (!string.IsNullOrWhiteSpace(firstRowPath)) return firstRowPath.Trim();
        var typed = FolderPath.Normalize(query);
        return FolderPath.IsAbsolute(typed) ? typed : null;
    }
}

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

    public static IReadOnlyList<JumpMethod> Plan(FileDialogKind kind) => kind switch
    {
        FileDialogKind.CommonItem => new[] { JumpMethod.ShellBrowser, JumpMethod.FileNameEdit, JumpMethod.AddressBar },
        _ => new[] { JumpMethod.FileNameEdit, JumpMethod.AddressBar, JumpMethod.ShellBrowser }
    };

    public static NavigationResult Go(IDialogSurface surface, string? folder, bool saveDialog, Func<string, bool>? exists = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var steps = new List<JumpStep>(3);
        var target = FolderPath.Normalize(folder);
        var previous = surface.FileName ?? "";
        if (!FolderPath.IsAbsolute(target))
            return Failed(steps, "not-absolute");
        if (exists is not null && !exists(target))
            return Failed(steps, "not-found");

        surface.PrepareForJump();
        var before = surface.CurrentLocation ?? "";
        foreach (var method in Plan(surface.Kind))
        {
            var ok = Try(surface, method, target, before);
            steps.Add(new JumpStep(method.ToString(), ok, ok ? "ok" : "miss"));
            if (!ok) continue;
            var restored = FileNameToRestore(previous, saveDialog);
            if (restored.Length > 0)
                surface.FileName = restored;
            return new NavigationResult(true, method == JumpMethod.AddressBar, restored)
            {
                Method = method.ToString(),
                Detail = "ok",
                Steps = steps
            };
        }

        surface.FileName = previous;
        return new NavigationResult(false, false, "")
        {
            Method = JumpMethod.None.ToString(),
            Detail = "miss",
            Steps = steps
        };
    }

    static bool Try(IDialogSurface surface, JumpMethod method, string target, string before)
    {
        switch (method)
        {
            case JumpMethod.FileNameEdit:
                surface.FileName = SubmitText(target);
                surface.SubmitFileName();
                return FolderPath.Landed(before, surface.CurrentLocation, target);
            case JumpMethod.AddressBar:
                if (!surface.TryNavigateAddress(target)) return false;
                return FolderPath.Landed(before, surface.CurrentLocation, target);
            case JumpMethod.ShellBrowser:
                if (!surface.TryBrowseShell(target)) return false;
                var after = surface.CurrentLocation ?? "";
                if (string.IsNullOrWhiteSpace(after)) return true;
                return FolderPath.Landed(before, after, target) || FolderPath.Same(after, target);
            default:
                return false;
        }
    }

    static NavigationResult Failed(List<JumpStep> steps, string detail)
    {
        steps.Add(new JumpStep(JumpMethod.None.ToString(), false, detail));
        return new NavigationResult(false, false, "")
        {
            Method = JumpMethod.None.ToString(),
            Detail = detail,
            Steps = steps
        };
    }
}
