using AltPane.Core.Models;

namespace AltPane.Core.Apps;

/// <summary>
/// The Start-menu replacement list: user and common Start Menu shortcuts plus
/// Store apps. Desktop, PATH, and Everything hits stay out. An empty query lists
/// names. A query filters by name and by the same pinyin initials the app search uses.
/// </summary>
public static class AppsModeList
{
    public const int Limit = 40;

    public static bool IsLauncherSource(AppSource source) =>
        source is AppSource.StartMenu or AppSource.Store;

    public static List<AppHit> List(
        IReadOnlyList<AppEntry>? apps,
        string? query,
        IReadOnlyList<WindowEntry>? windows,
        IReadOnlyList<string>? exclusions,
        int limit = Limit)
    {
        var hits = new List<AppHit>();
        if (apps is null || apps.Count == 0 || limit <= 0) return hits;
        var text = query?.Trim() ?? "";
        var open = windows ?? Array.Empty<WindowEntry>();
        foreach (var app in apps)
        {
            if (app is null || !IsLauncherSource(app.Source)) continue;
            if (AppCatalog.IsExcluded(app, exclusions)) continue;
            var quality = text.Length == 0 ? 0 : AppCatalog.Quality(text, app);
            if (quality < 0) continue;
            var handle = open.Count == 0 ? 0 : AppCatalog.FindOpenWindow(app, open);
            hits.Add(new AppHit(app, quality, handle));
        }

        if (text.Length == 0)
        {
            hits.Sort(static (a, b) => string.Compare(a.App.Name, b.App.Name, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            hits.Sort(static (a, b) =>
            {
                var byScore = b.Quality.CompareTo(a.Quality);
                return byScore != 0
                    ? byScore
                    : string.Compare(a.App.Name, b.App.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        if (hits.Count > limit) hits.RemoveRange(limit, hits.Count - limit);
        return hits;
    }
}
