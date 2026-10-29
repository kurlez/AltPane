namespace AltPane.Core.Apps;

/// <summary>
/// Turns an Everything file list into catalog rows. Nothing here talks to Everything;
/// the query string, the noise filter, and de-duplication are pure.
/// </summary>
public static class EverythingIndex
{
    public static readonly string[] DefaultExtensions = { "exe", "lnk", "url", "appref-ms" };

    static readonly string[] BuiltinPathExclusions =
    {
        "windows\\winsxs",
        "windows\\installer",
        "windows\\servicing",
        "windows\\softwaredistribution",
        "node_modules",
        "$recycle.bin",
        "windows\\temp",
        "appdata\\local\\temp"
    };

    static readonly string[] NoisyStems =
    {
        "uninstall",
        "uninstaller",
        "uninst",
        "unins000",
        "setup",
        "installer",
        "updater",
        "update",
        "crashhandler",
        "crashpad",
        "crashreporter",
        "crashsender"
    };

    public static string? Build(string? query, string? extensions, IReadOnlyList<string>? excludedPaths) =>
        Build(query, SplitExtensions(extensions), excludedPaths);

    public static string? Build(string? query, IReadOnlyList<string>? extensions, IReadOnlyList<string>? excludedPaths) =>
        Build(query, extensions, excludedPaths, allowAnyExtension: false);

    /// <param name="allowAnyExtension">File search for every type. App search still requires an extension list.</param>
    public static string? Build(string? query, IReadOnlyList<string>? extensions, IReadOnlyList<string>? excludedPaths, bool allowAnyExtension)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var exts = new List<string>();
        if (extensions is not null)
        {
            foreach (var raw in extensions)
            {
                var ext = raw.Trim().TrimStart('.').ToLowerInvariant();
                if (ext.Length == 0 || ext.IndexOfAny(new[] { ' ', ';', ',', '|' }) >= 0) continue;
                if (!exts.Contains(ext)) exts.Add(ext);
            }
        }
        if (exts.Count == 0 && !allowAnyExtension) return null;

        var parts = new List<string>();
        foreach (var token in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            parts.Add(Quote(token));
        if (parts.Count == 0) return null;
        if (exts.Count > 0) parts.Add("ext:" + string.Join(';', exts));
        foreach (var path in BuiltinPathExclusions)
            parts.Add("!path:" + path);
        if (excludedPaths is not null)
        {
            foreach (var raw in excludedPaths)
            {
                var path = raw.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
                if (path.Length == 0) continue;
                parts.Add("!path:" + Quote(path));
            }
        }
        return string.Join(' ', parts);
    }

    public static IReadOnlyList<string> SplitExtensions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DefaultExtensions;
        var list = new List<string>();
        foreach (var part in text.Split(new[] { ',', ';', ' ', '\r', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var ext = part.Trim().TrimStart('.').ToLowerInvariant();
            if (ext.Length == 0 || list.Contains(ext)) continue;
            list.Add(ext);
        }
        return list.Count == 0 ? DefaultExtensions : list;
    }

    public static string Quote(string token)
    {
        if (token.IndexOfAny(new[] { '!', '"', '*', '\\', '|', '<', '>', '(', ')', ':', ' ' }) < 0)
            return token;
        return "\"" + token.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static bool IsNoise(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        if (AppCatalog.IsNoisyPathExecutable(path)) return true;
        var parts = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return true;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (IsNoiseSegment(parts[i])) return true;
        }
        var file = parts[^1];
        var dot = file.LastIndexOf('.');
        var stem = dot > 0 ? file[..dot] : file;
        return IsNoisyStem(stem);
    }

    public static AppSource ClassifyPath(string? path)
    {
        var norm = (path ?? "").Replace('/', '\\').ToLowerInvariant();
        if (norm.Contains("\\start menu\\", StringComparison.Ordinal)) return AppSource.StartMenu;
        if (norm.Contains("\\desktop\\", StringComparison.Ordinal)) return AppSource.Desktop;
        if (norm.Contains("\\program files\\", StringComparison.Ordinal)
            || norm.Contains("\\program files (x86)\\", StringComparison.Ordinal)
            || norm.Contains("\\windowsapps\\", StringComparison.Ordinal)
            || norm.Contains("\\appdata\\local\\programs\\", StringComparison.Ordinal))
            return AppSource.Installed;
        return AppSource.Everything;
    }

    public static AppEntry? FromFile(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || IsNoise(fullPath)) return null;
        var ext = Extension(fullPath);
        var kind = ext switch
        {
            "lnk" or "url" or "appref-ms" => AppKind.Shortcut,
            "exe" => AppKind.Executable,
            _ => (AppKind?)null
        };
        if (kind is not AppKind resolvedKind) return null;
        var name = Stem(fullPath);
        if (name.Length == 0) return null;
        var resolved = resolvedKind == AppKind.Executable ? fullPath : "";
        return AppCatalog.Create(name, fullPath, resolvedKind, ClassifyPath(fullPath), resolved, "", "", fullPath, 0);
    }

    /// <summary>
    /// Catalog entries win. Extra rows that point at the same file are dropped.
    /// Among extras that share a resolved path, a higher source rank wins, and a shortcut wins a tie.
    /// </summary>
    public static IReadOnlyList<AppEntry> Combine(IReadOnlyList<AppEntry> catalog, IReadOnlyList<AppEntry> extra)
    {
        var catalogIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var catalogPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in catalog)
        {
            if (app is null) continue;
            if (!string.IsNullOrWhiteSpace(app.Id)) catalogIds.Add(app.Id);
            Remember(catalogPaths, app.ResolvedPath);
            Remember(catalogPaths, app.LaunchPath);
        }

        var kept = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in extra)
        {
            if (app is null || string.IsNullOrWhiteSpace(app.Name)) continue;
            if (!string.IsNullOrWhiteSpace(app.Id) && catalogIds.Contains(app.Id)) continue;
            var path = PreferPath(app);
            if (path.Length > 0 && catalogPaths.Contains(Norm(path))) continue;
            var key = path.Length > 0 ? Norm(path) : (string.IsNullOrWhiteSpace(app.Id) ? app.Name : app.Id);
            if (!kept.TryGetValue(key, out var have) || PreferExtra(app, have))
                kept[key] = app;
        }

        var merged = new List<AppEntry>(catalog.Count + kept.Count);
        foreach (var app in catalog)
        {
            if (app is not null) merged.Add(app);
        }
        merged.AddRange(kept.Values);
        return AppCatalog.Merge(merged);
    }

    static bool PreferExtra(AppEntry candidate, AppEntry current)
    {
        var rank = AppCatalog.SourceRank(candidate.Source).CompareTo(AppCatalog.SourceRank(current.Source));
        if (rank > 0) return true;
        if (rank < 0) return false;
        return candidate.Kind == AppKind.Shortcut && current.Kind != AppKind.Shortcut;
    }

    static bool IsNoiseSegment(string segment)
    {
        return segment.Equals("winsxs", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("installer", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("servicing", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("softwaredistribution", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("$recycle.bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("temp", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("tmp", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsNoisyStem(string stem)
    {
        var lower = stem.Trim().ToLowerInvariant();
        if (lower.Length == 0) return false;
        foreach (var name in NoisyStems)
        {
            if (lower == name) return true;
            if (lower.EndsWith("-" + name, StringComparison.Ordinal)
                || lower.EndsWith("_" + name, StringComparison.Ordinal)
                || lower.EndsWith(" " + name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    static void Remember(HashSet<string> paths, string? path)
    {
        var norm = Norm(path);
        if (norm.Length > 0) paths.Add(norm);
    }

    static string PreferPath(AppEntry app)
    {
        if (!string.IsNullOrWhiteSpace(app.ResolvedPath)) return app.ResolvedPath;
        return app.LaunchPath ?? "";
    }

    static string Norm(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "" : path.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

    static string Extension(string path)
    {
        var file = FileName(path);
        var dot = file.LastIndexOf('.');
        return dot >= 0 && dot < file.Length - 1 ? file[(dot + 1)..].ToLowerInvariant() : "";
    }

    static string Stem(string path)
    {
        var file = FileName(path);
        var dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    static string FileName(string path)
    {
        var trimmed = path.Trim().Trim('"').TrimEnd('\\', '/');
        var cut = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        return cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
    }
}
