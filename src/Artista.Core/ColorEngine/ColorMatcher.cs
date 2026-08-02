using Artista.Core.Imaging;

namespace Artista.Core.ColorEngine;

/// <summary>
/// The shared color-matching engine used by the global Remove Color effect and
/// the Color Remover brush tool.
///
/// Given a target color, a tolerance (0-100) and an edge softness (0-100), it
/// answers "how strongly does this pixel match the target?" as a factor in
/// [0,1]. Distances are measured in OKLab so tolerance behaves perceptually.
///
/// Semantics:
///  - tolerance 0  => only exact RGB matches (factor 1), everything else 0.
///  - tolerance t  => colors within the threshold distance match fully.
///  - softness s   => beyond the threshold, the factor falls smoothly to 0
///                    over an extra distance band instead of cutting off.
/// </summary>
public sealed class ColorMatcher
{
    /// <summary>
    /// OKLab distance mapped to tolerance 100. Black-to-white is 1.0;
    /// 0.9 makes the slider span "everything reasonable".
    /// </summary>
    private const float MaxDistance = 0.9f;

    private readonly byte _targetR;
    private readonly byte _targetG;
    private readonly byte _targetB;
    private readonly float _targetL;
    private readonly float _targetA;
    private readonly float _targetLabB;
    private readonly float _threshold;
    private readonly float _softWidth;

    public ColorMatcher(byte r, byte g, byte b, double tolerance, double softness)
    {
        _targetR = r;
        _targetG = g;
        _targetB = b;
        (_targetL, _targetA, _targetLabB) = OkLab.FromSrgb(r, g, b);

        tolerance = Math.Clamp(tolerance, 0, 100);
        softness = Math.Clamp(softness, 0, 100);
        // Quadratic tolerance response: fine control near 0.
        double t = tolerance / 100.0;
        _threshold = (float)(t * t * MaxDistance);
        _softWidth = (float)(softness / 100.0 * 0.25 * MaxDistance);
    }

    public static ColorMatcher FromBgra(uint targetBgra, double tolerance, double softness) =>
        new(ColorBgra.R(targetBgra), ColorBgra.G(targetBgra), ColorBgra.B(targetBgra), tolerance, softness);

    /// <summary>
    /// Match factor in [0,1] for a BGRA pixel: 1 = full match (remove
    /// completely), 0 = no match. Alpha does not affect the match; matching is
    /// on the pixel's color.
    /// </summary>
    public float Match(uint bgra)
    {
        byte r = ColorBgra.R(bgra), g = ColorBgra.G(bgra), b = ColorBgra.B(bgra);

        if (r == _targetR && g == _targetG && b == _targetB)
            return 1f;
        if (_threshold <= 0f && _softWidth <= 0f)
            return 0f;

        var (l, a, labB) = OkLab.FromSrgb(r, g, b);
        float dl = l - _targetL, da = a - _targetA, db = labB - _targetLabB;
        float dist = MathF.Sqrt(dl * dl + da * da + db * db);

        if (dist <= _threshold)
            return 1f;
        if (_softWidth <= 0f || dist >= _threshold + _softWidth)
            return 0f;

        // Smoothstep falloff across the softness band.
        float t = 1f - (dist - _threshold) / _softWidth;
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Applies removal to a single pixel: reduces alpha by the match factor
    /// scaled by <paramref name="strength"/> (0-1). Preserves existing partial
    /// transparency (multiplies alpha down, never up).
    /// </summary>
    public uint RemoveFrom(uint bgra, float strength = 1f)
    {
        byte alpha = ColorBgra.A(bgra);
        if (alpha == 0) return bgra;
        float f = Match(bgra) * strength;
        if (f <= 0f) return bgra;
        byte newAlpha = (byte)Math.Clamp(alpha * (1f - f) + 0.5f, 0, 255);
        return ColorBgra.WithAlpha(bgra, newAlpha);
    }
}
