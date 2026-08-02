namespace Artista.Core.ColorEngine;

/// <summary>
/// sRGB &lt;-&gt; OKLab conversion (Björn Ottosson's perceptual color space).
/// Used for perceptually uniform color-distance measurements.
/// </summary>
public static class OkLab
{
    private static readonly float[] SrgbToLinearLut = BuildLut();

    private static float[] BuildLut()
    {
        var lut = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            lut[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
        return lut;
    }

    /// <summary>Converts 8-bit sRGB to OKLab (L roughly 0-1).</summary>
    public static (float L, float A, float B) FromSrgb(byte r8, byte g8, byte b8)
    {
        float r = SrgbToLinearLut[r8];
        float g = SrgbToLinearLut[g8];
        float b = SrgbToLinearLut[b8];

        float l = 0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b;
        float m = 0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b;
        float s = 0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b;

        float l_ = MathF.Cbrt(l);
        float m_ = MathF.Cbrt(m);
        float s_ = MathF.Cbrt(s);

        return (
            0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_,
            1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_,
            0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_);
    }

    /// <summary>Euclidean distance between two colors in OKLab.</summary>
    public static float Distance(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        var (l1, a1, bb1) = FromSrgb(r1, g1, b1);
        var (l2, a2, bb2) = FromSrgb(r2, g2, b2);
        float dl = l1 - l2, da = a1 - a2, db = bb1 - bb2;
        return MathF.Sqrt(dl * dl + da * da + db * db);
    }
}
