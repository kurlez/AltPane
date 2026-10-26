using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace AltPane;

internal static class PreviewCapture
{
    public static BitmapSource? TryCapture(nint hwnd)
    {
        if (hwnd == 0 || NativeMethods.IsIconic(hwnd) || NativeMethods.IsHungAppWindow(hwnd))
            return null;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return null;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 16 || height < 16 || width > 4096 || height > 4096)
            return null;

        var screen = NativeMethods.GetDC(0);
        if (screen == 0) return null;
        var memory = NativeMethods.CreateCompatibleDC(screen);
        var bitmap = NativeMethods.CreateCompatibleBitmap(screen, width, height);
        var previous = nint.Zero;
        try
        {
            if (memory == 0 || bitmap == 0) return null;
            previous = NativeMethods.SelectObject(memory, bitmap);
            if (!NativeMethods.PrintWindow(hwnd, memory, 2))
                return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return null;
        }
        finally
        {
            if (previous != 0 && memory != 0)
                NativeMethods.SelectObject(memory, previous);
            if (bitmap != 0) NativeMethods.DeleteObject(bitmap);
            if (memory != 0) NativeMethods.DeleteDC(memory);
            NativeMethods.ReleaseDC(0, screen);
        }
    }
}
