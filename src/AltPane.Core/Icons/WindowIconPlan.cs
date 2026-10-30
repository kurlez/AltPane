namespace AltPane.Core.Icons;

public enum IconOrigin
{
    WindowBig,
    ClassIcon,
    PackageLogo,
    Relaunch,
    ExeImage,
    Shortcut
}

/// <summary>
/// Which window the icon is for. Packaged apps (including ApplicationFrameHost)
/// resolve through the package logo. A shared host such as chrome.exe prefers
/// the window's own icon or the relaunch icon over that host executable.
/// </summary>
public readonly record struct WindowIconRequest(bool Packaged, bool SharedHost);

public static class WindowIconPlan
{
    public static IReadOnlyList<IconOrigin> Candidates(in WindowIconRequest request)
    {
        if (request.Packaged)
        {
            return new[]
            {
                IconOrigin.PackageLogo,
                IconOrigin.WindowBig,
                IconOrigin.ClassIcon,
                IconOrigin.Relaunch,
                IconOrigin.Shortcut,
                IconOrigin.ExeImage
            };
        }

        if (request.SharedHost)
        {
            return new[]
            {
                IconOrigin.WindowBig,
                IconOrigin.ClassIcon,
                IconOrigin.Relaunch,
                IconOrigin.Shortcut,
                IconOrigin.ExeImage
            };
        }

        return new[]
        {
            IconOrigin.WindowBig,
            IconOrigin.ClassIcon,
            IconOrigin.ExeImage,
            IconOrigin.Shortcut
        };
    }

    /// <summary>
    /// First origin the probe accepts. The probe is the fake in tests and the
    /// shell retrieval in the app. A probe that returns false is skipped.
    /// </summary>
    public static IconOrigin? Select(in WindowIconRequest request, Func<IconOrigin, bool> available)
    {
        foreach (var origin in Candidates(request))
        {
            if (available(origin)) return origin;
        }

        return null;
    }

    /// <summary>
    /// Edges are in candidate order. Zero means that source produced nothing.
    /// A normal app skips a below-request window icon when a later source is
    /// sharp. A packaged app or shared host keeps the first adequate icon so
    /// the host executable does not replace it. A smaller icon is used only
    /// when nothing reaches <see cref="IconPixelSize.Minimum"/>.
    /// </summary>
    public static int Pick(IReadOnlyList<int> edges, int requested, bool preferFirstAdequate)
    {
        if (edges is null || edges.Count == 0) return -1;
        if (requested < IconPixelSize.Minimum) requested = IconPixelSize.Minimum;
        var adequate = -1;
        var any = -1;
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (edge <= 0) continue;
            if (any < 0) any = i;
            if (edge < IconPixelSize.Minimum) continue;
            if (preferFirstAdequate || edge >= requested) return i;
            if (adequate < 0) adequate = i;
        }

        return adequate >= 0 ? adequate : any;
    }
}
