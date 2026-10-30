using System.Xml.Linq;

namespace AltPane.Core.Icons;

public static class PackageManifest
{
    public readonly record struct Visual(string? ApplicationId, string? Square44, string? Background);

    public static string? ApplicationId(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return null;
        var text = aumid.Trim();
        const string prefix = "shell:AppsFolder\\";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            text = text[prefix.Length..];
        var bang = text.LastIndexOf('!');
        if (bang < 0 || bang >= text.Length - 1) return null;
        return text[(bang + 1)..];
    }

    public static IReadOnlyList<Visual> Read(string? xml)
    {
        var found = new List<Visual>();
        if (string.IsNullOrWhiteSpace(xml)) return found;
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (System.Xml.XmlException)
        {
            return found;
        }

        foreach (var app in doc.Descendants().Where(element => element.Name.LocalName == "Application"))
        {
            var id = (string?)app.Attribute("Id");
            foreach (var visual in app.Descendants().Where(element => element.Name.LocalName == "VisualElements"))
            {
                var logo = (string?)visual.Attribute("Square44x44Logo");
                var background = (string?)visual.Attribute("BackgroundColor");
                if (string.IsNullOrWhiteSpace(logo)) continue;
                found.Add(new Visual(id, logo, background));
            }
        }

        return found;
    }

    public static Visual? Select(IReadOnlyList<Visual>? visuals, string? applicationId)
    {
        if (visuals is null || visuals.Count == 0) return null;
        if (!string.IsNullOrEmpty(applicationId))
        {
            foreach (var visual in visuals)
            {
                if (string.Equals(visual.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(visual.Square44))
                    return visual;
            }
        }

        foreach (var visual in visuals)
        {
            if (!string.IsNullOrWhiteSpace(visual.Square44)) return visual;
        }

        return null;
    }
}
