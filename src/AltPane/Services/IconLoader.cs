using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AltPane;

internal static class IconLoader
{
    public const int Edge = 32;
    static readonly object Gate = new();
    static readonly Dictionary<string, ImageSource> Hits = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> Misses = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, ImageSource> Letters = new(StringComparer.Ordinal);

    public readonly struct IconPixels
    {
        public IconPixels(int width, int height, byte[] bgra)
        {
            Width = width;
            Height = height;
            Bgra = bgra;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Bgra { get; }
    }

    public static ImageSource? Cached(string? path, int index)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        lock (Gate)
            return Hits.TryGetValue(Key(path, index), out var icon) ? icon : null;
    }

    public static bool TryDecode(string? path, int index, out IconPixels pixels)
    {
        pixels = default;
        if (string.IsNullOrWhiteSpace(path)) return false;
        var key = Key(path, index);
        lock (Gate)
        {
            if (Hits.ContainsKey(key) || Misses.Contains(key)) return false;
        }

        IconPixels decoded;
        try
        {
            if (!DecodeFile(path, index, out decoded))
            {
                lock (Gate) Misses.Add(key);
                return false;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            lock (Gate) Misses.Add(key);
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
            PixelFormats.Bgra32,
            null,
            pixels.Bgra,
            stride);
        source.Freeze();
        return source;
    }

    public static void Store(string? path, int index, ImageSource? source)
    {
        if (string.IsNullOrWhiteSpace(path) || source is null) return;
        if (source.CanFreeze && !source.IsFrozen) source.Freeze();
        lock (Gate)
        {
            var key = Key(path, index);
            Hits[key] = source;
            Misses.Remove(key);
        }
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

    static bool DecodeFile(string path, int index, out IconPixels pixels)
    {
        pixels = default;
        if (!File.Exists(path)) return false;
        var extracted = nint.Zero;
        try
        {
            var count = NativeMethods.PrivateExtractIcons(path, index, Edge, Edge, out extracted, out _, 1, 0);
            if (count <= 0 || extracted == 0) return false;
            return DecodeIcon(extracted, out pixels);
        }
        finally
        {
            if (extracted != 0) NativeMethods.DestroyIcon(extracted);
        }
    }

    static bool DecodeIcon(nint icon, out IconPixels pixels)
    {
        pixels = default;
        if (!NativeMethods.GetIconInfo(icon, out var info)) return false;
        try
        {
            if (info.Color == 0) return false;
            var dc = NativeMethods.GetDC(0);
            try
            {
                var header = new BitmapInfo { Header = { Size = 40 } };
                if (NativeMethods.GetDIBits(dc, info.Color, 0, 0, null, ref header, NativeMethods.DibRgbColors) == 0)
                    return false;
                var width = header.Header.Width;
                var height = Math.Abs(header.Header.Height);
                if (width <= 0 || height <= 0 || width > 256 || height > 256) return false;
                header.Header.Width = width;
                header.Header.Height = -height;
                header.Header.Planes = 1;
                header.Header.BitCount = 32;
                header.Header.Compression = 0;
                header.Header.SizeImage = width * height * 4;
                var bgra = new byte[header.Header.SizeImage];
                var rows = NativeMethods.GetDIBits(dc, info.Color, 0, (uint)height, bgra, ref header, NativeMethods.DibRgbColors);
                if (rows == 0) return false;
                FillOpaque(bgra);
                pixels = new IconPixels(width, height, bgra);
                return true;
            }
            finally
            {
                if (dc != 0) NativeMethods.ReleaseDC(0, dc);
            }
        }
        finally
        {
            if (info.Color != 0) NativeMethods.DeleteObject(info.Color);
            if (info.Mask != 0) NativeMethods.DeleteObject(info.Mask);
        }
    }

    static void FillOpaque(byte[] bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 0) return;
        }
        for (var i = 3; i < bgra.Length; i += 4)
            bgra[i] = 255;
    }

    static string Key(string path, int index) =>
        index == 0 ? path : path + "\n" + index.ToString(CultureInfo.InvariantCulture);
}
