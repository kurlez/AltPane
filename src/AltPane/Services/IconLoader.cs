using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AltPane.Core.Models;

namespace AltPane;

internal static class IconLoader
{
    public static ImageSource? TryGetIcon(WindowEntry entry)
    {
        var hwnd = (nint)entry.Handle;
        foreach (var large in new[] { NativeMethods.IconBig, NativeMethods.IconSmall2 })
        {
            var hr = NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WmGetIcon, large, 0, NativeMethods.SmtoAbortIfHung, 40, out var icon);
            if (hr != 0 && icon != 0)
            {
                var source = FromSharedIcon(icon);
                if (source is not null) return source;
            }
        }

        foreach (var index in new[] { NativeMethods.GclHIcon, NativeMethods.GclHIconSm })
        {
            var icon = NativeMethods.GetClassLongPtr(hwnd, index);
            var source = FromSharedIcon(icon);
            if (source is not null) return source;
        }

        if (!string.IsNullOrWhiteSpace(entry.IconPath) && File.Exists(entry.IconPath))
        {
            var extracted = nint.Zero;
            try
            {
                var count = NativeMethods.PrivateExtractIcons(entry.IconPath, 0, 64, 64, out extracted, out _, 1, 0);
                if (count > 0 && extracted != 0)
                    return FromOwnedIcon(extracted);
                if (extracted != 0)
                    NativeMethods.DestroyIcon(extracted);
            }
            catch (Exception ex)
            {
                if (extracted != 0) NativeMethods.DestroyIcon(extracted);
                AppLog.Error(ex);
            }
        }

        return null;
    }

    public static ImageSource Letter(string title)
    {
        var glyph = string.IsNullOrWhiteSpace(title) ? "?" : StringInfo.GetNextTextElement(title.Trim());
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(0x3D, 0x6B, 0xFF)),
                null,
                new Rect(0, 0, 64, 64),
                16,
                16);
            var formatted = new FormattedText(
                glyph,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                28,
                Brushes.White,
                1.0);
            dc.DrawText(formatted, new Point(Math.Max(0, (64 - formatted.Width) / 2), Math.Max(0, (64 - formatted.Height) / 2)));
        }

        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    static ImageSource? FromSharedIcon(nint icon) => icon == 0 ? null : FromOwnedIcon(NativeMethods.CopyIcon(icon), destroy: true);

    static ImageSource? FromOwnedIcon(nint icon, bool destroy = true)
    {
        if (icon == 0) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(64, 64));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (destroy) NativeMethods.DestroyIcon(icon);
        }
    }
}
