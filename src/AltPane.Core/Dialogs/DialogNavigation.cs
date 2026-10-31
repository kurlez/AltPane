namespace AltPane.Core.Dialogs;

public enum JumpMethod
{
    None,
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
    /// <summary>Legacy and common-item dialogs use the same in-process fallback order.</summary>
    FileDialogKind Kind => FileDialogKind.Legacy;
    /// <summary>Why the last attempt failed. Empty when it succeeded or was not tried.</summary>
    string LastDetail => "";
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

    /// <summary>
    /// Filename edit, then the address bar. Both stay inside this process
    /// (window messages and UI Automation). A shell-view pointer from the dialog
    /// belongs to the other process and must not be dereferenced here.
    /// </summary>
    public static IReadOnlyList<JumpMethod> Plan(FileDialogKind kind)
    {
        _ = kind;
        return new[] { JumpMethod.FileNameEdit, JumpMethod.AddressBar };
    }

    public static NavigationResult Go(
        IDialogSurface surface,
        string? folder,
        bool saveDialog,
        Func<string, bool>? exists = null,
        Action<JumpStep>? onStep = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var steps = new List<JumpStep>(3);
        void Report(string method, bool ok, string detail)
        {
            var step = new JumpStep(method, ok, detail);
            steps.Add(step);
            onStep?.Invoke(step);
        }

        var target = FolderPath.Normalize(folder);
        var previous = surface.FileName ?? "";
        if (!FolderPath.IsAbsolute(target))
            return Failed(steps, "not-absolute", Report);
        if (exists is not null && !exists(target))
            return Failed(steps, "not-found", Report);

        surface.PrepareForJump();
        var before = surface.CurrentLocation ?? "";
        foreach (var method in Plan(surface.Kind))
        {
            var ok = Try(surface, method, target, before);
            var detail = ok ? "ok" : DetailOf(surface);
            Report(method.ToString(), ok, detail);
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

    static string DetailOf(IDialogSurface surface)
    {
        var detail = surface.LastDetail;
        return string.IsNullOrWhiteSpace(detail) ? "miss" : detail;
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
                return surface.TryNavigateAddress(target)
                    && FolderPath.Landed(before, surface.CurrentLocation, target);
            default:
                return false;
        }
    }

    static NavigationResult Failed(List<JumpStep> steps, string detail, Action<string, bool, string> report)
    {
        report(JumpMethod.None.ToString(), false, detail);
        return new NavigationResult(false, false, "")
        {
            Method = JumpMethod.None.ToString(),
            Detail = detail,
            Steps = steps
        };
    }
}
