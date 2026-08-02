using Artista.Core.Imaging;

namespace Artista.Core.Layers;

public enum BlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    Difference,
    Additive,
}

public static class BlendModeOps
{
    public static readonly BlendMode[] All = (BlendMode[])Enum.GetValues(typeof(BlendMode));

    public static string DisplayName(this BlendMode mode) => mode switch
    {
        BlendMode.Normal => "Normal",
        BlendMode.Multiply => "Multiply",
        BlendMode.Screen => "Screen",
        BlendMode.Overlay => "Overlay",
        BlendMode.Darken => "Darken",
        BlendMode.Lighten => "Lighten",
        BlendMode.Difference => "Difference",
        BlendMode.Additive => "Additive",
        _ => mode.ToString(),
    };

    /// <summary>Separable per-channel blend function on 0-255 values.</summary>
    public static int BlendChannel(BlendMode mode, int cd, int cs) => mode switch
    {
        BlendMode.Multiply => cd * cs / 255,
        BlendMode.Screen => 255 - (255 - cd) * (255 - cs) / 255,
        BlendMode.Overlay => cd <= 127
            ? 2 * cd * cs / 255
            : 255 - 2 * (255 - cd) * (255 - cs) / 255,
        BlendMode.Darken => Math.Min(cd, cs),
        BlendMode.Lighten => Math.Max(cd, cs),
        BlendMode.Difference => Math.Abs(cd - cs),
        BlendMode.Additive => Math.Min(255, cd + cs),
        _ => cs,
    };

    /// <summary>
    /// Composites a straight-alpha source pixel over a straight-alpha destination
    /// pixel using the given blend mode and an extra source alpha scale (layer
    /// opacity, 0-255). Follows the standard W3C compositing model where the
    /// blend function applies only where source and destination overlap.
    /// </summary>
    public static uint Composite(BlendMode mode, uint dst, uint src, int opacity)
    {
        int sa = ColorBgra.A(src) * opacity / 255;
        if (sa <= 0) return dst;
        int da = ColorBgra.A(dst);

        if (mode == BlendMode.Normal || da == 0)
            return ColorBgra.Over(dst, src, opacity);

        int sb = ColorBgra.B(src), sg = ColorBgra.G(src), sr = ColorBgra.R(src);
        int db = ColorBgra.B(dst), dg = ColorBgra.G(dst), dr = ColorBgra.R(dst);

        // Effective source color: blended where the destination is present.
        int bb = BlendChannel(mode, db, sb);
        int bg = BlendChannel(mode, dg, sg);
        int br = BlendChannel(mode, dr, sr);
        int eb = sb + (bb - sb) * da / 255;
        int eg = sg + (bg - sg) * da / 255;
        int er = sr + (br - sr) * da / 255;

        int outA = sa + da * (255 - sa) / 255;
        int wS = sa * 255;
        int wD = da * (255 - sa);
        int wT = wS + wD;
        byte ob = (byte)((eb * wS + db * wD) / wT);
        byte og = (byte)((eg * wS + dg * wD) / wT);
        byte or_ = (byte)((er * wS + dr * wD) / wT);
        return ColorBgra.Pack(ob, og, or_, (byte)outA);
    }
}
