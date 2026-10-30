using System.Globalization;
using AltPane.Core.Icons;

namespace AltPane;

/// <summary>
/// Resolves one window icon by <see cref="WindowIconPlan"/> and keeps the first
/// source that is sharp enough. Shared hosts and packaged apps keep that source
/// instead of replacing it with a larger host executable.
/// </summary>
internal static class WindowIconLoader
{
    public readonly struct Hit
    {
        public Hit(IconLoader.IconPixels pixels, string cacheKey, string? sharePath, int shareIndex)
        {
            Pixels = pixels;
            CacheKey = cacheKey ?? "";
            SharePath = sharePath;
            ShareIndex = shareIndex;
        }

        public IconLoader.IconPixels Pixels { get; }
        public string CacheKey { get; }
        public string? SharePath { get; }
        public int ShareIndex { get; }
    }

    public static string HandleKey(long hwnd) =>
        "hwnd:" + hwnd.ToString(CultureInfo.InvariantCulture);

    public static bool TryLoad(
        long hwnd,
        int processId,
        int hostedProcessId,
        string? processName,
        string? exePath,
        string? shortcutPath,
        int shortcutIndex,
        int edge,
        out Hit hit)
    {
        hit = default;
        if (edge < IconPixelSize.Minimum) edge = IconPixelSize.Minimum;
        exePath ??= "";
        shortcutPath ??= "";
        var identity = ShellIcons.ReadIdentity((nint)hwnd, processId, hostedProcessId, processName);
        var packaged = identity.Packaged || HostExecutables.IsFrameHost(processName);
        var shared = !packaged && HostExecutables.SharesHost(processName);
        var request = new WindowIconRequest(packaged, shared);
        var preferFirst = packaged || shared;
        Hit? fallback = null;
        Hit? any = null;
        foreach (var origin in WindowIconPlan.Candidates(request))
        {
            if (!TryOrigin(origin, hwnd, exePath, shortcutPath, shortcutIndex, identity, edge, out var pixels, out var sharePath, out var shareIndex))
                continue;
            var got = Math.Min(pixels.Width, pixels.Height);
            var candidate = new Hit(pixels, KeyFor(origin, hwnd, exePath, shortcutPath, shortcutIndex, identity, shared), sharePath, shareIndex);
            if (any is null) any = candidate;
            var definitive = got >= edge || (preferFirst && got >= IconPixelSize.Minimum);
            if (definitive) 
            {
                hit = candidate;
                return true;
            }

            if (fallback is null && got >= IconPixelSize.Minimum)
                fallback = candidate;
        }

        if (fallback is { } saved)
        {
            hit = saved;
            return true;
        }

        if (any is { } small)
        {
            var edgePx = Math.Min(small.Pixels.Width, small.Pixels.Height);
            hit = edgePx >= IconPixelSize.Minimum
                ? small
                : new Hit(small.Pixels, HandleKey(hwnd), null, 0);
            return true;
        }

        return false;
    }

    static bool TryOrigin(
        IconOrigin origin,
        long hwnd,
        string exePath,
        string shortcutPath,
        int shortcutIndex,
        ShellIcons.Identity identity,
        int edge,
        out IconLoader.IconPixels pixels,
        out string? sharePath,
        out int shareIndex)
    {
        pixels = default;
        sharePath = null;
        shareIndex = 0;
        switch (origin)
        {
            case IconOrigin.WindowBig:
                return ShellIcons.TryWindowBig((nint)hwnd, out pixels);
            case IconOrigin.ClassIcon:
                return ShellIcons.TryClassIcon((nint)hwnd, out pixels);
            case IconOrigin.PackageLogo:
                if (!ShellIcons.TryPackage(identity, edge, out pixels)) return false;
                sharePath = PackagePath(identity.Aumid);
                return true;
            case IconOrigin.Relaunch:
                return identity.Relaunch.Length > 0 && ShellIcons.TryRelaunch(identity.Relaunch, edge, out pixels);
            case IconOrigin.ExeImage:
                if (exePath.Length == 0 || HostExecutables.IsFramePath(exePath)) return false;
                if (!ShellIcons.TryFile(exePath, 0, edge, out pixels)) return false;
                sharePath = exePath;
                return true;
            case IconOrigin.Shortcut:
                if (shortcutPath.Length == 0) return false;
                if (!ShellIcons.TryFile(shortcutPath, shortcutIndex, edge, out pixels)) return false;
                sharePath = shortcutPath;
                shareIndex = shortcutIndex;
                return true;
            default:
                return false;
        }
    }

    static string KeyFor(
        IconOrigin origin,
        long hwnd,
        string exePath,
        string shortcutPath,
        int shortcutIndex,
        ShellIcons.Identity identity,
        bool shared)
    {
        switch (origin)
        {
            case IconOrigin.PackageLogo when identity.Aumid.Length > 0:
                return IconCacheKey.For(PackagePath(identity.Aumid));
            case IconOrigin.Relaunch when identity.Relaunch.Length > 0:
                return "relaunch:" + identity.Relaunch;
            case IconOrigin.Shortcut when shortcutPath.Length > 0:
                return IconCacheKey.For(shortcutPath, shortcutIndex);
            case IconOrigin.ExeImage when exePath.Length > 0:
                return IconCacheKey.For(exePath);
            case IconOrigin.WindowBig or IconOrigin.ClassIcon when !shared && exePath.Length > 0 && !HostExecutables.IsFramePath(exePath):
                return IconCacheKey.For(exePath);
            default:
                return HandleKey(hwnd);
        }
    }

    static string? PackagePath(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return null;
        const string prefix = "shell:AppsFolder\\";
        return aumid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? aumid : prefix + aumid;
    }
}
