using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AltPane.Core.Icons;

namespace AltPane;

internal static class IconLoader
{
    public const int Edge = 32;
    const int HitCapacity = 96;
    const int PendingCapacity = 24;
    const int MissCapacity = 256;

    static readonly object Gate = new();
    static readonly LruCache<ImageSource> Hits = new(HitCapacity);
    static readonly LruCache<IconPixels> Pending = new(PendingCapacity);
    static readonly HashSet<string> Misses = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, ImageSource> Letters = new(StringComparer.Ordinal);
    static readonly IconPathBindings Bindings = new();
    static int _edge;
    static long _edgeTick;

    public readonly struct IconPixels
    {
        public IconPixels(int width, int height, byte[] bgra, bool premultiplied = false)
        {
            Width = width;
            Height = height;
            Bgra = bgra;
            Premultiplied = premultiplied;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Bgra { get; }
        public bool Premultiplied { get; }
    }

    public static int RequestEdge()
    {
        var now = Environment.TickCount64;
        if (_edge >= IconPixelSize.Minimum && now - _edgeTick < 5000) return _edge;
        var dpi = 96;
        try
        {
            foreach (var monitor in MonitorUtil.Live())
            {
                if (monitor.Bounds.DpiX > dpi) dpi = monitor.Bounds.DpiX;
                if (monitor.Bounds.DpiY > dpi) dpi = monitor.Bounds.DpiY;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }

        _edge = IconPixelSize.ForDpi(dpi);
        _edgeTick = now;
        return _edge;
    }

    public static ImageSource? Cached(string? path, int index)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return CachedKey(Key(path, index));
    }

    public static ImageSource? CachedKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        lock (Gate)
            return Hits.TryGet(key, out var icon) ? icon : null;
    }

    public static bool TryDecode(string? path, int index, out IconPixels pixels)
    {
        pixels = default;
        if (string.IsNullOrWhiteSpace(path)) return false;
        var key = Key(path, index);
        lock (Gate)
        {
            if (Hits.TryGet(key, out _) || Misses.Contains(key) || Pending.TryGet(key, out _)) return false;
        }

        IconPixels decoded;
        try
        {
            if (!DecodeFile(path, index, out decoded))
            {
                RememberMiss(key);
                return false;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            RememberMiss(key);
            return false;
        }

        pixels = decoded;
        return true;
    }

    public static ImageSource? Freeze(in IconPixels pixels)
    {
        if (pixels.Width <= 0 || pixels.Height <= 0 || pixels.Bgra is null) return null;
        var stride = pixels.Width * 4;
        if (pixels.Bgra.Length < stride * pixels.Height) return null;
        var source = BitmapSource.Create(
            pixels.Width,
            pixels.Height,
            96,
            96,
            pixels.Premultiplied ? PixelFormats.Pbgra32 : PixelFormats.Bgra32,
            null,
            pixels.Bgra,
            stride);
        source.Freeze();
        return source;
    }

    /// <summary>Keep decoded pixels from a background thread. The UI thread freezes them.</summary>
    public static void Keep(string? path, int index, in IconPixels pixels)
    {
        if (string.IsNullOrWhiteSpace(path) || pixels.Width <= 0 || pixels.Bgra is null) return;
        lock (Gate)
        {
            var key = Key(path, index);
            if (Hits.TryGet(key, out _)) return;
            Pending.Set(key, pixels);
            Misses.Remove(key);
        }
    }

    /// <summary>Turn a background prefetch into a frozen image. Safe on the UI thread.</summary>
    public static ImageSource? Materialize(string? path, int index)
    {
        var cached = Cached(path, index);
        if (cached is not null) return cached;
        if (string.IsNullOrWhiteSpace(path)) return null;
        var key = Key(path, index);
        IconPixels pixels;
        lock (Gate)
        {
            if (!Pending.TryGet(key, out pixels)) return null;
        }

        var source = Freeze(pixels);
        if (source is null) return null;
        StoreKey(key, source);
        lock (Gate) Pending.Remove(key);
        return source;
    }

    public static void Store(string? path, int index, ImageSource? source)
    {
        if (string.IsNullOrWhiteSpace(path) || source is null) return;
        StoreKey(Key(path, index), source);
    }

    public static void StoreKey(string? key, ImageSource? source)
    {
        if (string.IsNullOrEmpty(key) || source is null) return;
        if (source.CanFreeze && !source.IsFrozen) source.Freeze();
        lock (Gate)
        {
            Hits.Set(key, source);
            Pending.Remove(key);
            Misses.Remove(key);
        }
    }

    public static void InvalidatePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (Gate)
        {
            Hits.RemoveWhere(key => IconCacheKey.SamePath(key, path));
            Pending.RemoveWhere(key => IconCacheKey.SamePath(key, path));
            if (Misses.Count > 0)
            {
                var stale = new List<string>();
                foreach (var key in Misses)
                {
                    if (IconCacheKey.SamePath(key, path)) stale.Add(key);
                }
                foreach (var key in stale) Misses.Remove(key);
            }
        }
    }

    /// <summary>Drop decoded pixel buffers kept for a later UI pass. Frozen images stay in the small LRU.</summary>
    public static void ReleaseIdle()
    {
        lock (Gate)
        {
            Pending.Clear();
            Misses.Clear();
        }
    }

    public static void RemoveKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        lock (Gate)
        {
            Hits.Remove(key);
            Pending.Remove(key);
            Misses.Remove(key);
        }
    }

    /// <summary>
    /// Drops the icon cached for the previous executable when an app's path changes.
    /// </summary>
    public static void RebindApp(string? appKey, string? path)
    {
        var previous = Bindings.Rebind(appKey, path);
        if (!string.IsNullOrEmpty(previous)) InvalidatePath(previous);
    }

    public static ImageSource Letter(string title)
    {
        var glyph = string.IsNullOrWhiteSpace(title) ? "?" : StringInfo.GetNextTextElement(title.Trim());
        if (Letters.TryGetValue(glyph, out var cached)) return cached;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(0x3D, 0x6B, 0xFF)),
                null,
                new Rect(0, 0, Edge, Edge),
                8,
                8);
            var formatted = new FormattedText(
                glyph,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                14,
                Brushes.White,
                1.0);
            dc.DrawText(formatted, new Point(Math.Max(0, (Edge - formatted.Width) / 2), Math.Max(0, (Edge - formatted.Height) / 2)));
        }

        var bitmap = new RenderTargetBitmap(Edge, Edge, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        Letters[glyph] = bitmap;
        return bitmap;
    }

    static void RememberMiss(string key)
    {
        lock (Gate)
        {
            if (Misses.Count >= MissCapacity) Misses.Clear();
            Misses.Add(key);
        }
    }

    static bool DecodeFile(string path, int index, out IconPixels pixels) =>
        ShellIcons.TryFile(path, index, RequestEdge(), out pixels);

    static string Key(string path, int index) => IconCacheKey.For(path, index);
}
