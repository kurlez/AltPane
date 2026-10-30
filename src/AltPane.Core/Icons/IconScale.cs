namespace AltPane.Core.Icons;

/// <summary>
/// Area-average downscale for a jumbo shell image. Never enlarges.
/// </summary>
public static class IconScale
{
    public static byte[] Box(byte[] src, int width, int height, int edge)
    {
        if (src is null || width <= 0 || height <= 0 || edge <= 0) return src ?? Array.Empty<byte>();
        if (src.Length < width * height * 4) return src;
        if (width <= edge && height <= edge) return src;
        var dst = new byte[edge * edge * 4];
        for (var y = 0; y < edge; y++)
        {
            var y0 = y * height / edge;
            var y1 = Math.Max(y0 + 1, (y + 1) * height / edge);
            if (y1 > height) y1 = height;
            for (var x = 0; x < edge; x++)
            {
                var x0 = x * width / edge;
                var x1 = Math.Max(x0 + 1, (x + 1) * width / edge);
                if (x1 > width) x1 = width;
                long b = 0, g = 0, r = 0, a = 0, n = 0;
                for (var yy = y0; yy < y1; yy++)
                {
                    var row = yy * width * 4;
                    for (var xx = x0; xx < x1; xx++)
                    {
                        var i = row + xx * 4;
                        b += src[i];
                        g += src[i + 1];
                        r += src[i + 2];
                        a += src[i + 3];
                        n++;
                    }
                }

                if (n == 0) continue;
                var o = (y * edge + x) * 4;
                dst[o] = (byte)(b / n);
                dst[o + 1] = (byte)(g / n);
                dst[o + 2] = (byte)(r / n);
                dst[o + 3] = (byte)(a / n);
            }
        }

        return dst;
    }
}
