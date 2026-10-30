using System.Runtime.InteropServices;

namespace AltPane;

internal static class IconBits
{
    [StructLayout(LayoutKind.Sequential)]
    struct BitmapNative
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public nint Bits;
    }

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    static extern int GetObject(nint h, int c, ref BitmapNative bm);

    public static bool FromIcon(nint icon, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        if (icon == 0 || !NativeMethods.GetIconInfo(icon, out var info)) return false;
        try
        {
            if (info.Color == 0) return false;
            if (!SizeOf(info.Color, out var width, out var height)) return false;
            return Read(info.Color, width, height, premultiplied: false, out pixels);
        }
        finally
        {
            if (info.Color != 0) NativeMethods.DeleteObject(info.Color);
            if (info.Mask != 0) NativeMethods.DeleteObject(info.Mask);
        }
    }

    public static bool FromBitmap(nint bitmap, bool premultiplied, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        if (bitmap == 0 || !SizeOf(bitmap, out var width, out var height)) return false;
        return Read(bitmap, width, height, premultiplied, out pixels);
    }

    static bool SizeOf(nint bitmap, out int width, out int height)
    {
        width = 0;
        height = 0;
        var info = new BitmapNative();
        if (GetObject(bitmap, Marshal.SizeOf<BitmapNative>(), ref info) == 0) return false;
        width = info.Width;
        height = Math.Abs(info.Height);
        return width > 0 && height > 0 && width <= 1024 && height <= 1024;
    }

    static bool Read(nint bitmap, int width, int height, bool premultiplied, out IconLoader.IconPixels pixels)
    {
        pixels = default;
        var dc = NativeMethods.GetDC(0);
        try
        {
            var header = new BitmapInfo { Header = { Size = 40 } };
            if (NativeMethods.GetDIBits(dc, bitmap, 0, 0, null, ref header, NativeMethods.DibRgbColors) == 0
                && (header.Header.Width == 0 || header.Header.Height == 0))
                return false;
            if (header.Header.Width > 0) width = header.Header.Width;
            if (header.Header.Height != 0) height = Math.Abs(header.Header.Height);
            if (width <= 0 || height <= 0 || width > 1024 || height > 1024) return false;
            header.Header.Width = width;
            header.Header.Height = -height;
            header.Header.Planes = 1;
            header.Header.BitCount = 32;
            header.Header.Compression = 0;
            header.Header.SizeImage = width * height * 4;
            var bgra = new byte[header.Header.SizeImage];
            var rows = NativeMethods.GetDIBits(dc, bitmap, 0, (uint)height, bgra, ref header, NativeMethods.DibRgbColors);
            if (rows == 0) return false;
            if (FillOpaque(bgra)) premultiplied = false;
            pixels = new IconLoader.IconPixels(width, height, bgra, premultiplied);
            return true;
        }
        finally
        {
            if (dc != 0) NativeMethods.ReleaseDC(0, dc);
        }
    }

    static bool FillOpaque(byte[] bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 0) return false;
        }

        for (var i = 3; i < bgra.Length; i += 4)
            bgra[i] = 255;
        return true;
    }
}
