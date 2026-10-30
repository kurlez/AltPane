namespace AltPane.Core.Icons;

/// <summary>
/// Composites an unplated package logo over the manifest background color.
/// Bytes are BGRA.
/// </summary>
public static class IconPlate
{
    public static byte[] Over(byte[] bgra, int width, int height, byte b, byte g, byte r, bool premultiplied)
    {
        if (bgra is null || width <= 0 || height <= 0) return bgra ?? Array.Empty<byte>();
        var count = width * height * 4;
        if (bgra.Length < count) return bgra;
        var output = new byte[count];
        for (var i = 0; i < count; i += 4)
        {
            var sb = bgra[i];
            var sg = bgra[i + 1];
            var sr = bgra[i + 2];
            var sa = bgra[i + 3];
            if (premultiplied)
            {
                var inv = 255 - sa;
                output[i] = (byte)(sb + (b * inv + 127) / 255);
                output[i + 1] = (byte)(sg + (g * inv + 127) / 255);
                output[i + 2] = (byte)(sr + (r * inv + 127) / 255);
            }
            else
            {
                output[i] = (byte)((sb * sa + b * (255 - sa) + 127) / 255);
                output[i + 1] = (byte)((sg * sa + g * (255 - sa) + 127) / 255);
                output[i + 2] = (byte)((sr * sa + r * (255 - sa) + 127) / 255);
            }

            output[i + 3] = 255;
        }

        return output;
    }
}
