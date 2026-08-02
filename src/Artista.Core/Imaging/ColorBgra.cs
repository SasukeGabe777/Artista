using System.Runtime.CompilerServices;

namespace Artista.Core.Imaging;

/// <summary>
/// Helpers for working with 32-bit BGRA pixels packed into a uint
/// (blue in the low byte, alpha in the high byte — matches WPF's Bgra32
/// little-endian memory layout). Alpha is straight (non-premultiplied).
/// </summary>
public static class ColorBgra
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Pack(byte b, byte g, byte r, byte a) =>
        (uint)(b | (g << 8) | (r << 16) | (a << 24));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte B(uint c) => (byte)c;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte G(uint c) => (byte)(c >> 8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte R(uint c) => (byte)(c >> 16);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte A(uint c) => (byte)(c >> 24);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint WithAlpha(uint c, byte a) => (c & 0x00FFFFFFu) | ((uint)a << 24);

    public const uint Transparent = 0x00000000u;
    public const uint Black = 0xFF000000u;
    public const uint White = 0xFFFFFFFFu;

    /// <summary>Linear interpolation between two colors (straight alpha), t in [0,1].</summary>
    public static uint Lerp(uint from, uint to, float t)
    {
        if (t <= 0f) return from;
        if (t >= 1f) return to;
        byte b = (byte)(B(from) + (B(to) - B(from)) * t + 0.5f);
        byte g = (byte)(G(from) + (G(to) - G(from)) * t + 0.5f);
        byte r = (byte)(R(from) + (R(to) - R(from)) * t + 0.5f);
        byte a = (byte)(A(from) + (A(to) - A(from)) * t + 0.5f);
        return Pack(b, g, r, a);
    }

    /// <summary>
    /// Standard "source over destination" for straight-alpha pixels, with the
    /// source additionally scaled by <paramref name="srcAlphaScale"/> (0-255).
    /// </summary>
    public static uint Over(uint dst, uint src, int srcAlphaScale = 255)
    {
        int sa = A(src) * srcAlphaScale / 255;
        if (sa <= 0) return dst;
        int da = A(dst);
        if (sa >= 255 || da == 0)
            return WithAlpha(src, (byte)sa);

        int outA = sa + da * (255 - sa) / 255;
        if (outA == 0) return 0;

        // Weighted average of straight color values.
        int wS = sa * 255;
        int wD = da * (255 - sa);
        int wT = wS + wD;
        byte b = (byte)((B(src) * wS + B(dst) * wD) / wT);
        byte g = (byte)((G(src) * wS + G(dst) * wD) / wT);
        byte r = (byte)((R(src) * wS + R(dst) * wD) / wT);
        return Pack(b, g, r, (byte)outA);
    }

    /// <summary>Composites color over an opaque background, returning an opaque pixel.</summary>
    public static uint OverOpaque(uint background, uint src)
    {
        int sa = A(src);
        if (sa >= 255) return src | 0xFF000000u;
        if (sa <= 0) return background | 0xFF000000u;
        byte b = (byte)((B(src) * sa + B(background) * (255 - sa)) / 255);
        byte g = (byte)((G(src) * sa + G(background) * (255 - sa)) / 255);
        byte r = (byte)((R(src) * sa + R(background) * (255 - sa)) / 255);
        return Pack(b, g, r, 255);
    }
}
