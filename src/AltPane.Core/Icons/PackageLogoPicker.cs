namespace AltPane.Core.Icons;

/// <summary>
/// Picks a Square44x44Logo scale or targetsize. Unplated assets are preferred.
/// An unplated asset is plated by the caller when the manifest background is a color.
/// Contrast and theme variants are used only when nothing else matches.
/// </summary>
public static class PackageLogoPicker
{
    public readonly record struct Choice(string RelativePath, int Edge, bool NeedsPlate, string? Background);

    readonly record struct Asset(
        string RelativePath,
        int Edge,
        bool Unplated,
        bool LightUnplated,
        bool Contrast,
        bool Theme,
        bool TargetSize,
        bool Scale);

    public static Choice? Choose(string? square44, string? background, IEnumerable<string>? files, int targetEdge)
    {
        if (string.IsNullOrWhiteSpace(square44) || files is null) return null;
        if (targetEdge < 1) targetEdge = IconPixelSize.Minimum;
        var parsed = new List<Asset>();
        foreach (var file in files)
        {
            if (TryParse(square44, file, out var asset)) parsed.Add(asset);
        }

        if (parsed.Count == 0) return null;
        var best = Best(parsed, targetEdge, skipSpecial: true) ?? Best(parsed, targetEdge, skipSpecial: false);
        if (best is null) return null;
        var chosen = best.Value;
        var needsPlate = chosen.Unplated && IconColor.TryParse(background, out _, out _, out _);
        return new Choice(chosen.RelativePath, chosen.Edge, needsPlate, background);
    }

    static Asset? Best(List<Asset> parsed, int targetEdge, bool skipSpecial)
    {
        Asset? best = null;
        var penalty = int.MaxValue;
        foreach (var asset in parsed)
        {
            if (skipSpecial && (asset.Contrast || asset.Theme)) continue;
            var score = Penalty(asset, targetEdge);
            if (score >= penalty) continue;
            penalty = score;
            best = asset;
        }

        return best;
    }

    static int Penalty(Asset asset, int target)
    {
        var size = asset.Edge >= target ? asset.Edge - target : 1000 + (target - asset.Edge);
        var form = asset.Unplated ? 0 : asset.LightUnplated ? 40 : 70;
        var kind = asset.TargetSize ? 0 : asset.Scale ? 8 : 16;
        return size * 10 + form + kind;
    }

    static bool TryParse(string square44, string file, out Asset asset)
    {
        asset = default;
        if (string.IsNullOrWhiteSpace(file)) return false;
        var logo = square44.Replace('\\', '/').Trim().TrimStart('/');
        var candidate = file.Replace('\\', '/').Trim().TrimStart('/');
        var logoDir = DirectoryOf(logo);
        var candDir = DirectoryOf(candidate);
        if (!logoDir.Equals(candDir, StringComparison.OrdinalIgnoreCase)) return false;
        var logoStem = Path.GetFileNameWithoutExtension(logo);
        var stem = Path.GetFileNameWithoutExtension(candidate);
        if (logoStem.Length == 0 || stem.Length == 0) return false;
        if (!stem.Equals(logoStem, StringComparison.OrdinalIgnoreCase)
            && !stem.StartsWith(logoStem + ".", StringComparison.OrdinalIgnoreCase))
            return false;

        var qualifiers = stem.Length == logoStem.Length
            ? ""
            : stem[(logoStem.Length + 1)..];
        var edge = 44;
        var unplated = false;
        var light = false;
        var contrast = false;
        var theme = false;
        var targetSize = false;
        var scale = false;
        if (qualifiers.Length > 0)
        {
            foreach (var part in qualifiers.Split('_', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.StartsWith("targetsize-", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(part.AsSpan("targetsize-".Length), out var size) && size > 0)
                {
                    edge = size;
                    targetSize = true;
                }
                else if (part.StartsWith("scale-", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(part.AsSpan("scale-".Length), out var percent) && percent > 0)
                {
                    edge = (int)Math.Round(44 * (percent / 100d));
                    scale = true;
                }
                else if (part.Equals("altform-unplated", StringComparison.OrdinalIgnoreCase))
                    unplated = true;
                else if (part.Equals("altform-lightunplated", StringComparison.OrdinalIgnoreCase))
                    light = true;
                else if (part.StartsWith("contrast-", StringComparison.OrdinalIgnoreCase))
                    contrast = true;
                else if (part.StartsWith("theme-", StringComparison.OrdinalIgnoreCase))
                    theme = true;
            }
        }

        if (edge < 1) edge = 44;
        asset = new Asset(candidate, edge, unplated, light, contrast, theme, targetSize, scale);
        return true;
    }

    static string DirectoryOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }
}
