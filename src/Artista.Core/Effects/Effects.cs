using Artista.Core.Imaging;

namespace Artista.Core.Effects;

public sealed class GaussianBlurEffect : EffectBase
{
    public override string Name => "Gaussian Blur";
    public override string Category => "Blurs";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("radius", "Radius", 0, 200, 6, "px"),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token) =>
        BlurHelpers.GaussianBlur(src, dst, roi, parameters.GetInt("radius"), token);
}

public sealed class MotionBlurEffect : EffectBase
{
    public override string Name => "Motion Blur";
    public override string Category => "Blurs";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("angle", "Angle", -180, 180, 25, "°"),
        new IntParameter("distance", "Distance", 1, 200, 10, "px"),
        new BoolParameter("centered", "Centered", true),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var r = roi.Intersect(src.Bounds);
        double angle = parameters.GetInt("angle") * Math.PI / 180.0;
        int distance = parameters.GetInt("distance");
        bool centered = parameters.GetBool("centered");
        double dx = Math.Cos(angle), dy = -Math.Sin(angle);
        int samples = Math.Max(2, distance);
        double start = centered ? -(distance - 1) / 2.0 : 0;

        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < r.Width; x++)
            {
                double sb = 0, sg = 0, sr = 0, sa = 0;
                for (int i = 0; i < samples; i++)
                {
                    double t = start + i * (distance - 1.0) / Math.Max(1, samples - 1);
                    uint c = src.GetPixelClamped(
                        (int)Math.Round(r.Left + x + dx * t),
                        (int)Math.Round(y + dy * t));
                    double a = ColorBgra.A(c);
                    sb += ColorBgra.B(c) * a;
                    sg += ColorBgra.G(c) * a;
                    sr += ColorBgra.R(c) * a;
                    sa += a;
                }
                if (sa <= 0.001)
                {
                    dstRow[x] = 0;
                    continue;
                }
                dstRow[x] = ColorBgra.Pack(
                    (byte)Math.Clamp(sb / sa + 0.5, 0, 255),
                    (byte)Math.Clamp(sg / sa + 0.5, 0, 255),
                    (byte)Math.Clamp(sr / sa + 0.5, 0, 255),
                    (byte)Math.Clamp(sa / samples + 0.5, 0, 255));
            }
        });
    }
}

public sealed class SharpenEffect : EffectBase
{
    public override string Name => "Sharpen";
    public override string Category => "Photo";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("amount", "Amount", 1, 20, 2),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var r = roi.Intersect(src.Bounds);
        int amount = parameters.GetInt("amount");
        var blurred = new Surface(src.Width, src.Height);
        BlurHelpers.GaussianBlur(src, blurred, r, 2, token);
        double strength = amount * 0.3;

        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var srcRow = src.GetRowSpan(y, r.Left, r.Width);
            var blurRow = blurred.GetRowSpan(y, r.Left, r.Width);
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < srcRow.Length; x++)
            {
                uint c = srcRow[x], bl = blurRow[x];
                int b = (int)(ColorBgra.B(c) + (ColorBgra.B(c) - ColorBgra.B(bl)) * strength);
                int g = (int)(ColorBgra.G(c) + (ColorBgra.G(c) - ColorBgra.G(bl)) * strength);
                int rr = (int)(ColorBgra.R(c) + (ColorBgra.R(c) - ColorBgra.R(bl)) * strength);
                dstRow[x] = ColorBgra.Pack(
                    (byte)Math.Clamp(b, 0, 255), (byte)Math.Clamp(g, 0, 255),
                    (byte)Math.Clamp(rr, 0, 255), ColorBgra.A(c));
            }
        });
    }
}

public sealed class NoiseEffect : EffectBase
{
    public override string Name => "Add Noise";
    public override string Category => "Noise";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("intensity", "Intensity", 0, 100, 30),
        new IntParameter("colorSat", "Color saturation", 0, 100, 100),
        new IntParameter("seed", "Seed", 0, 9999, 42),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var r = roi.Intersect(src.Bounds);
        int intensity = parameters.GetInt("intensity") * 255 / 100;
        int colorSat = parameters.GetInt("colorSat");
        int seed = parameters.GetInt("seed");

        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var rng = new Random(HashCode.Combine(seed, y));
            var srcRow = src.GetRowSpan(y, r.Left, r.Width);
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < srcRow.Length; x++)
            {
                uint c = srcRow[x];
                int nLum = rng.Next(-intensity, intensity + 1);
                int nR = nLum, nG = nLum, nB = nLum;
                if (colorSat > 0)
                {
                    nR += rng.Next(-intensity, intensity + 1) * colorSat / 100;
                    nG += rng.Next(-intensity, intensity + 1) * colorSat / 100;
                    nB += rng.Next(-intensity, intensity + 1) * colorSat / 100;
                }
                dstRow[x] = ColorBgra.Pack(
                    (byte)Math.Clamp(ColorBgra.B(c) + nB, 0, 255),
                    (byte)Math.Clamp(ColorBgra.G(c) + nG, 0, 255),
                    (byte)Math.Clamp(ColorBgra.R(c) + nR, 0, 255),
                    ColorBgra.A(c));
            }
        });
    }
}

public sealed class ReduceNoiseEffect : EffectBase
{
    public override string Name => "Reduce Noise";
    public override string Category => "Noise";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("radius", "Radius", 1, 3, 1, "px"),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var r = roi.Intersect(src.Bounds);
        int radius = parameters.GetInt("radius");
        int windowSize = (2 * radius + 1) * (2 * radius + 1);

        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            Span<byte> bs = stackalloc byte[49];
            Span<byte> gs = stackalloc byte[49];
            Span<byte> rs = stackalloc byte[49];
            Span<byte> asp = stackalloc byte[49];
            for (int x = 0; x < r.Width; x++)
            {
                int n = 0;
                for (int oy = -radius; oy <= radius; oy++)
                {
                    for (int ox = -radius; ox <= radius; ox++)
                    {
                        uint c = src.GetPixelClamped(r.Left + x + ox, y + oy);
                        bs[n] = ColorBgra.B(c);
                        gs[n] = ColorBgra.G(c);
                        rs[n] = ColorBgra.R(c);
                        asp[n] = ColorBgra.A(c);
                        n++;
                    }
                }
                var bSlice = bs[..windowSize]; bSlice.Sort();
                var gSlice = gs[..windowSize]; gSlice.Sort();
                var rSlice = rs[..windowSize]; rSlice.Sort();
                var aSlice = asp[..windowSize]; aSlice.Sort();
                int mid = windowSize / 2;
                dstRow[x] = ColorBgra.Pack(bSlice[mid], gSlice[mid], rSlice[mid], aSlice[mid]);
            }
        });
    }
}

public sealed class PixelateEffect : EffectBase
{
    public override string Name => "Pixelate";
    public override string Category => "Distort";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("cellSize", "Cell size", 2, 100, 8, "px"),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var r = roi.Intersect(src.Bounds);
        int cell = parameters.GetInt("cellSize");

        // Average each cell (cells are aligned to the surface origin so results
        // are stable regardless of selection position).
        int cy0 = r.Top / cell, cy1 = (r.Bottom - 1) / cell;
        Parallel.For(cy0, cy1 + 1, new ParallelOptions { CancellationToken = token }, cyIdx =>
        {
            int yStart = Math.Max(cyIdx * cell, r.Top);
            int yEnd = Math.Min(cyIdx * cell + cell, r.Bottom);
            for (int cxIdx = r.Left / cell; cxIdx <= (r.Right - 1) / cell; cxIdx++)
            {
                int xStart = Math.Max(cxIdx * cell, r.Left);
                int xEnd = Math.Min(cxIdx * cell + cell, r.Right);
                long sb = 0, sg = 0, sr = 0, sa = 0;
                int count = 0;
                for (int y = yStart; y < yEnd; y++)
                {
                    var row = src.GetRowSpan(y, xStart, xEnd - xStart);
                    foreach (uint c in row)
                    {
                        int a = ColorBgra.A(c);
                        sb += ColorBgra.B(c) * a;
                        sg += ColorBgra.G(c) * a;
                        sr += ColorBgra.R(c) * a;
                        sa += a;
                        count++;
                    }
                }
                uint avg;
                if (sa <= 0) avg = 0;
                else
                {
                    avg = ColorBgra.Pack(
                        (byte)(sb / sa), (byte)(sg / sa), (byte)(sr / sa), (byte)(sa / count));
                }
                for (int y = yStart; y < yEnd; y++)
                    dst.GetRowSpan(y, xStart, xEnd - xStart).Fill(avg);
            }
        });
    }
}

public sealed class OutlineEffect : EffectBase
{
    public override string Name => "Outline";
    public override string Category => "Stylize";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("thickness", "Thickness", 1, 10, 3),
        new IntParameter("intensity", "Intensity", 0, 100, 50),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var r = roi.Intersect(src.Bounds);
        int thickness = parameters.GetInt("thickness");
        double intensity = parameters.GetInt("intensity") / 50.0;

        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < r.Width; x++)
            {
                int px = r.Left + x;
                // Local min/max luminance over the thickness window (morphological edge).
                int minL = 255, maxL = 0;
                for (int oy = -thickness; oy <= thickness; oy++)
                {
                    for (int ox = -thickness; ox <= thickness; ox++)
                    {
                        uint c = src.GetPixelClamped(px + ox, y + oy);
                        int lum = (ColorBgra.R(c) * 299 + ColorBgra.G(c) * 587 + ColorBgra.B(c) * 114) / 1000;
                        if (lum < minL) minL = lum;
                        if (lum > maxL) maxL = lum;
                    }
                }
                int edge = (int)((maxL - minL) * intensity);
                byte v = (byte)Math.Clamp(255 - edge, 0, 255);
                dstRow[x] = ColorBgra.Pack(v, v, v, ColorBgra.A(src[px, y]));
            }
        });
    }
}

public sealed class DropShadowEffect : EffectBase
{
    public override string Name => "Drop Shadow";
    public override string Category => "Stylize";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("offsetX", "Offset X", -100, 100, 6, "px"),
        new IntParameter("offsetY", "Offset Y", -100, 100, 6, "px"),
        new IntParameter("blur", "Blur radius", 0, 100, 6, "px"),
        new IntParameter("opacity", "Shadow opacity", 0, 100, 60, "%"),
        new ColorParameter("color", "Shadow color", 0xFF000000),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        int offsetX = parameters.GetInt("offsetX");
        int offsetY = parameters.GetInt("offsetY");
        int blur = parameters.GetInt("blur");
        int opacity = parameters.GetInt("opacity");
        uint color = parameters.GetColor("color");

        // Build the shadow silhouette over the whole surface (shadows extend
        // beyond the ROI), blur it, then composite source over it.
        var shadow = new Surface(src.Width, src.Height);
        Parallel.For(0, src.Height, new ParallelOptions { CancellationToken = token }, y =>
        {
            int sy = y - offsetY;
            var row = shadow.GetRow(y);
            if (sy < 0 || sy >= src.Height) return;
            for (int x = 0; x < src.Width; x++)
            {
                int sx = x - offsetX;
                if (sx < 0 || sx >= src.Width) continue;
                byte a = ColorBgra.A(src[sx, sy]);
                if (a == 0) continue;
                row[x] = ColorBgra.WithAlpha(color, (byte)(a * ColorBgra.A(color) / 255 * opacity / 100));
            }
        });
        if (blur > 0)
        {
            var blurred = new Surface(src.Width, src.Height);
            BlurHelpers.GaussianBlur(shadow, blurred, shadow.Bounds, blur, token);
            shadow = blurred;
        }

        var r = roi.Intersect(src.Bounds);
        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var srcRow = src.GetRowSpan(y, r.Left, r.Width);
            var shRow = shadow.GetRowSpan(y, r.Left, r.Width);
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < srcRow.Length; x++)
                dstRow[x] = ColorBgra.Over(shRow[x], srcRow[x]);
        });
    }
}

public sealed class GlowEffect : EffectBase
{
    public override string Name => "Glow";
    public override string Category => "Photo";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("radius", "Radius", 1, 50, 6, "px"),
        new IntParameter("brightness", "Brightness", -100, 100, 10),
        new IntParameter("contrast", "Contrast", -100, 100, 10),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var r = roi.Intersect(src.Bounds);
        var blurred = new Surface(src.Width, src.Height);
        BlurHelpers.GaussianBlur(src, blurred, r, parameters.GetInt("radius"), token);

        int brightness = parameters.GetInt("brightness") * 255 / 100;
        double contrast = parameters.GetInt("contrast") / 100.0;
        double factor = contrast >= 0 ? 1.0 + contrast * 2.0 : 1.0 + contrast;
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
            lut[i] = (byte)Math.Clamp((i - 127.5 + brightness) * factor + 127.5, 0, 255);

        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var srcRow = src.GetRowSpan(y, r.Left, r.Width);
            var blRow = blurred.GetRowSpan(y, r.Left, r.Width);
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < srcRow.Length; x++)
            {
                uint s = srcRow[x], bl = blRow[x];
                // Screen the brightened blur over the original.
                byte b = ScreenChannel(ColorBgra.B(s), lut[ColorBgra.B(bl)]);
                byte g = ScreenChannel(ColorBgra.G(s), lut[ColorBgra.G(bl)]);
                byte rr = ScreenChannel(ColorBgra.R(s), lut[ColorBgra.R(bl)]);
                dstRow[x] = ColorBgra.Pack(b, g, rr, ColorBgra.A(s));
            }
        });
    }

    private static byte ScreenChannel(int a, int b) => (byte)(255 - (255 - a) * (255 - b) / 255);
}

public sealed class EmbossEffect : EffectBase
{
    public override string Name => "Emboss";
    public override string Category => "Stylize";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("angle", "Angle", -180, 180, 45, "°"),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var r = roi.Intersect(src.Bounds);
        double angle = parameters.GetInt("angle") * Math.PI / 180.0;
        int dx = (int)Math.Round(Math.Cos(angle));
        int dy = -(int)Math.Round(Math.Sin(angle));

        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < r.Width; x++)
            {
                int px = r.Left + x;
                uint c1 = src.GetPixelClamped(px - dx, y - dy);
                uint c2 = src.GetPixelClamped(px + dx, y + dy);
                int l1 = (ColorBgra.R(c1) * 299 + ColorBgra.G(c1) * 587 + ColorBgra.B(c1) * 114) / 1000;
                int l2 = (ColorBgra.R(c2) * 299 + ColorBgra.G(c2) * 587 + ColorBgra.B(c2) * 114) / 1000;
                byte v = (byte)Math.Clamp(128 + (l1 - l2), 0, 255);
                dstRow[x] = ColorBgra.Pack(v, v, v, ColorBgra.A(src[px, y]));
            }
        });
    }
}

public sealed class EdgeDetectEffect : EffectBase
{
    public override string Name => "Edge Detect";
    public override string Category => "Stylize";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("intensity", "Intensity", 1, 100, 50),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var r = roi.Intersect(src.Bounds);
        double intensity = parameters.GetInt("intensity") / 25.0;

        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < r.Width; x++)
            {
                int px = r.Left + x;
                double gxB = 0, gyB = 0, gxG = 0, gyG = 0, gxR = 0, gyR = 0;
                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        uint c = src.GetPixelClamped(px + ox, y + oy);
                        int kx = SobelX[oy + 1, ox + 1], ky = SobelY[oy + 1, ox + 1];
                        gxB += ColorBgra.B(c) * kx; gyB += ColorBgra.B(c) * ky;
                        gxG += ColorBgra.G(c) * kx; gyG += ColorBgra.G(c) * ky;
                        gxR += ColorBgra.R(c) * kx; gyR += ColorBgra.R(c) * ky;
                    }
                }
                byte b = (byte)Math.Clamp(Math.Sqrt(gxB * gxB + gyB * gyB) * intensity, 0, 255);
                byte g = (byte)Math.Clamp(Math.Sqrt(gxG * gxG + gyG * gyG) * intensity, 0, 255);
                byte rr = (byte)Math.Clamp(Math.Sqrt(gxR * gxR + gyR * gyR) * intensity, 0, 255);
                dstRow[x] = ColorBgra.Pack(b, g, rr, ColorBgra.A(src[px, y]));
            }
        });
    }

    private static readonly int[,] SobelX = { { -1, 0, 1 }, { -2, 0, 2 }, { -1, 0, 1 } };
    private static readonly int[,] SobelY = { { -1, -2, -1 }, { 0, 0, 0 }, { 1, 2, 1 } };
}

public sealed class VignetteEffect : EffectBase
{
    public override string Name => "Vignette";
    public override string Category => "Photo";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("radius", "Radius", 10, 150, 70, "%"),
        new IntParameter("strength", "Strength", 0, 100, 60, "%"),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var r = roi.Intersect(src.Bounds);
        double radius = parameters.GetInt("radius") / 100.0;
        double strength = parameters.GetInt("strength") / 100.0;
        double cx = src.Width / 2.0, cy = src.Height / 2.0;
        double maxDist = Math.Sqrt(cx * cx + cy * cy) * radius;

        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var srcRow = src.GetRowSpan(y, r.Left, r.Width);
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < srcRow.Length; x++)
            {
                double dx = r.Left + x - cx, dy = y - cy;
                double d = Math.Sqrt(dx * dx + dy * dy) / maxDist;
                uint c = srcRow[x];
                if (d <= 1.0)
                {
                    dstRow[x] = c;
                    continue;
                }
                double falloff = Math.Min(1.0, (d - 1.0) * 2.0);
                double keep = 1.0 - falloff * strength;
                dstRow[x] = ColorBgra.Pack(
                    (byte)(ColorBgra.B(c) * keep), (byte)(ColorBgra.G(c) * keep),
                    (byte)(ColorBgra.R(c) * keep), ColorBgra.A(c));
            }
        });
    }
}
