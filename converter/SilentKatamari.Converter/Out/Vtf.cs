using System.Buffers.Binary;

namespace SilentKatamari.Converter;

/// <summary>Valve Texture Format writer: version 7.2, BGRA8888, full mip chain, no low-res thumbnail.
/// Layout from the VTF spec (developer.valvesoftware.com/wiki/VTF): 80-byte header, then mips smallest first.</summary>
public static class Vtf
{
    public const int HeaderSize = 80;
    public const uint FormatBgra8888 = 12;
    public const uint FlagEightBitAlpha = 0x2000;

    /// <summary>Sides rounded down to powers of two and clamped to maxSide (the engine wants power-of-two textures).</summary>
    public static RgbaImage FitPowerOfTwo(RgbaImage img, int maxSide = 1024)
    {
        static int Pow2(int v, int max) { int p = 1; while (p * 2 <= Math.Min(v, max)) p *= 2; return p; }
        int w = Pow2(img.Width, maxSide), h = Pow2(img.Height, maxSide);
        return w == img.Width && h == img.Height ? img : Resample(img, w, h);
    }

    /// <summary>Box filter to an exact size.</summary>
    public static RgbaImage Resample(RgbaImage src, int w, int h)
    {
        var dst = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int y0 = y * src.Height / h, y1 = Math.Max(y0 + 1, (y + 1) * src.Height / h);
            for (int x = 0; x < w; x++)
            {
                int x0 = x * src.Width / w, x1 = Math.Max(x0 + 1, (x + 1) * src.Width / w);
                long r = 0, g = 0, b = 0, a = 0, n = 0;
                for (int sy = y0; sy < y1; sy++)
                for (int sx = x0; sx < x1; sx++)
                {
                    int i = (sy * src.Width + sx) * 4;
                    r += src.Pixels[i]; g += src.Pixels[i + 1]; b += src.Pixels[i + 2]; a += src.Pixels[i + 3]; n++;
                }
                int o = (y * w + x) * 4;
                dst[o] = (byte)(r / n); dst[o + 1] = (byte)(g / n); dst[o + 2] = (byte)(b / n); dst[o + 3] = (byte)(a / n);
            }
        }
        return new RgbaImage(w, h, dst);
    }

    public static bool HasAlpha(RgbaImage img)
    {
        for (int i = 3; i < img.Pixels.Length; i += 4) if (img.Pixels[i] < 255) return true;
        return false;
    }

    public static byte[] Encode(RgbaImage source, int maxSide = 1024)
    {
        var img = FitPowerOfTwo(source, maxSide);
        var mips = new List<RgbaImage> { img };
        while (mips[^1].Width > 1 || mips[^1].Height > 1)
        {
            var last = mips[^1];
            mips.Add(Resample(last, Math.Max(1, last.Width / 2), Math.Max(1, last.Height / 2)));
        }

        double rr = 0, rg = 0, rb = 0;
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            rr += Linear(img.Pixels[i]); rg += Linear(img.Pixels[i + 1]); rb += Linear(img.Pixels[i + 2]);
        }
        double n = Math.Max(1, img.Pixels.Length / 4);

        var header = new byte[HeaderSize];
        var s = header.AsSpan();
        "VTF\0"u8.CopyTo(s);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], 7);
        BinaryPrimitives.WriteUInt32LittleEndian(s[8..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(s[12..], HeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(s[16..], (ushort)img.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(s[18..], (ushort)img.Height);
        BinaryPrimitives.WriteUInt32LittleEndian(s[20..], HasAlpha(img) ? FlagEightBitAlpha : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(s[24..], 1); // frames
        BinaryPrimitives.WriteUInt16LittleEndian(s[26..], 0); // first frame
        BinaryPrimitives.WriteSingleLittleEndian(s[32..], (float)(rr / n));
        BinaryPrimitives.WriteSingleLittleEndian(s[36..], (float)(rg / n));
        BinaryPrimitives.WriteSingleLittleEndian(s[40..], (float)(rb / n));
        BinaryPrimitives.WriteSingleLittleEndian(s[48..], 1f); // bumpmap scale
        BinaryPrimitives.WriteUInt32LittleEndian(s[52..], FormatBgra8888);
        s[56] = (byte)mips.Count;
        BinaryPrimitives.WriteUInt32LittleEndian(s[57..], 0xFFFFFFFF); // no low-res image
        s[61] = 0; s[62] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(s[63..], 1); // depth

        using var ms = new MemoryStream();
        ms.Write(header);
        for (int m = mips.Count - 1; m >= 0; m--) // smallest first
        {
            var px = mips[m].Pixels;
            var bgra = new byte[px.Length];
            for (int i = 0; i < px.Length; i += 4)
            {
                bgra[i] = px[i + 2]; bgra[i + 1] = px[i + 1]; bgra[i + 2] = px[i]; bgra[i + 3] = px[i + 3];
            }
            ms.Write(bgra);
        }
        return ms.ToArray();
    }

    private static double Linear(byte c) => Math.Pow(c / 255.0, 2.2);
}
