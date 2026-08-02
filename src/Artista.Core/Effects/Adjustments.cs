using Artista.Core.Imaging;

namespace Artista.Core.Effects;

/// <summary>Auto Level: stretches each RGB channel's histogram to full range.</summary>
public sealed class AutoLevelAdjustment : EffectBase
{
    public override string Name => "Auto Level";
    public override bool IsAdjustment => true;

    public override IReadOnlyList<EffectParameter> CreateParameters() => Array.Empty<EffectParameter>();

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var r = roi.Intersect(src.Bounds);
        int minB = 255, minG = 255, minR = 255, maxB = 0, maxG = 0, maxR = 0;
        for (int y = r.Top; y < r.Bottom; y++)
        {
            token.ThrowIfCancellationRequested();
            var row = src.GetRowSpan(y, r.Left, r.Width);
            foreach (uint c in row)
            {
                if (ColorBgra.A(c) == 0) continue;
                int b = ColorBgra.B(c), g = ColorBgra.G(c), rr = ColorBgra.R(c);
                if (b < minB) minB = b;
                if (b > maxB) maxB = b;
                if (g < minG) minG = g;
                if (g > maxG) maxG = g;
                if (rr < minR) minR = rr;
                if (rr > maxR) maxR = rr;
            }
        }
        var lutB = BuildStretchLut(minB, maxB);
        var lutG = BuildStretchLut(minG, maxG);
        var lutR = BuildStretchLut(minR, maxR);
        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var srcRow = src.GetRowSpan(y, r.Left, r.Width);
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < srcRow.Length; x++)
            {
                uint c = srcRow[x];
                dstRow[x] = ColorBgra.Pack(lutB[ColorBgra.B(c)], lutG[ColorBgra.G(c)], lutR[ColorBgra.R(c)], ColorBgra.A(c));
            }
        });
    }

    private static byte[] BuildStretchLut(int min, int max)
    {
        var lut = new byte[256];
        int range = Math.Max(1, max - min);
        for (int i = 0; i < 256; i++)
            lut[i] = (byte)Math.Clamp((i - min) * 255 / range, 0, 255);
        return lut;
    }
}

public sealed class BlackAndWhiteAdjustment : PerPixelEffect
{
    public override string Name => "Black and White";
    public override bool IsAdjustment => true;

    public override IReadOnlyList<EffectParameter> CreateParameters() => Array.Empty<EffectParameter>();

    protected override Func<uint, uint> CreateTransform(ParameterSet parameters) => c =>
    {
        byte lum = (byte)((ColorBgra.R(c) * 299 + ColorBgra.G(c) * 587 + ColorBgra.B(c) * 114) / 1000);
        return ColorBgra.Pack(lum, lum, lum, ColorBgra.A(c));
    };
}

public sealed class BrightnessContrastAdjustment : PerPixelEffect
{
    public override string Name => "Brightness / Contrast";
    public override bool IsAdjustment => true;

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("brightness", "Brightness", -100, 100, 0),
        new IntParameter("contrast", "Contrast", -100, 100, 0),
    };

    protected override Func<uint, uint> CreateTransform(ParameterSet parameters)
    {
        int brightness = parameters.GetInt("brightness") * 255 / 100;
        double contrast = parameters.GetInt("contrast") / 100.0;
        double factor = contrast >= 0
            ? 1.0 + contrast * 3.0
            : 1.0 + contrast;
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
            lut[i] = (byte)Math.Clamp((i - 127.5 + brightness) * factor + 127.5, 0, 255);
        return c => ColorBgra.Pack(lut[ColorBgra.B(c)], lut[ColorBgra.G(c)], lut[ColorBgra.R(c)], ColorBgra.A(c));
    }
}

public sealed class CurvesAdjustment : PerPixelEffect
{
    public override string Name => "Curves";
    public override bool IsAdjustment => true;

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new CurvesParameter("curves", "Transfer curve"),
    };

    protected override Func<uint, uint> CreateTransform(ParameterSet parameters)
    {
        var curves = parameters.GetCurves("curves");
        if (!curves.PerChannel)
        {
            var lut = curves.BuildLut(0);
            return c => ColorBgra.Pack(lut[ColorBgra.B(c)], lut[ColorBgra.G(c)], lut[ColorBgra.R(c)], ColorBgra.A(c));
        }
        var lutR = curves.BuildLut(1);
        var lutG = curves.BuildLut(2);
        var lutB = curves.BuildLut(3);
        return c => ColorBgra.Pack(lutB[ColorBgra.B(c)], lutG[ColorBgra.G(c)], lutR[ColorBgra.R(c)], ColorBgra.A(c));
    }
}

public sealed class HueSaturationAdjustment : PerPixelEffect
{
    public override string Name => "Hue / Saturation";
    public override bool IsAdjustment => true;

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("hue", "Hue", -180, 180, 0, "°"),
        new IntParameter("saturation", "Saturation", 0, 200, 100, "%"),
        new IntParameter("lightness", "Lightness", -100, 100, 0),
    };

    protected override Func<uint, uint> CreateTransform(ParameterSet parameters)
    {
        double hueShift = parameters.GetInt("hue");
        double satScale = parameters.GetInt("saturation") / 100.0;
        double lightness = parameters.GetInt("lightness") / 100.0;
        return c =>
        {
            var (h, s, v) = RgbToHsv(ColorBgra.R(c), ColorBgra.G(c), ColorBgra.B(c));
            h = (h + hueShift + 360) % 360;
            s = Math.Clamp(s * satScale, 0, 1);
            var (r, g, b) = HsvToRgb(h, s, v);
            if (lightness > 0)
            {
                r = (byte)(r + (255 - r) * lightness);
                g = (byte)(g + (255 - g) * lightness);
                b = (byte)(b + (255 - b) * lightness);
            }
            else if (lightness < 0)
            {
                r = (byte)(r * (1 + lightness));
                g = (byte)(g * (1 + lightness));
                b = (byte)(b * (1 + lightness));
            }
            return ColorBgra.Pack(b, g, r, ColorBgra.A(c));
        };
    }

    public static (double H, double S, double V) RgbToHsv(byte r8, byte g8, byte b8)
    {
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        double s = max <= 0 ? 0 : d / max;
        return (h, s, max);
    }

    public static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = ((int)(h / 60) % 6) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return (
            (byte)Math.Clamp((r + m) * 255 + 0.5, 0, 255),
            (byte)Math.Clamp((g + m) * 255 + 0.5, 0, 255),
            (byte)Math.Clamp((b + m) * 255 + 0.5, 0, 255));
    }
}

public sealed class InvertColorsAdjustment : PerPixelEffect
{
    public override string Name => "Invert Colors";
    public override bool IsAdjustment => true;

    public override IReadOnlyList<EffectParameter> CreateParameters() => Array.Empty<EffectParameter>();

    protected override Func<uint, uint> CreateTransform(ParameterSet parameters) =>
        c => (c ^ 0x00FFFFFFu);
}

public sealed class LevelsAdjustment : PerPixelEffect
{
    public override string Name => "Levels";
    public override bool IsAdjustment => true;

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("inBlack", "Input black", 0, 254, 0),
        new IntParameter("inWhite", "Input white", 1, 255, 255),
        new DoubleParameter("gamma", "Gamma", 0.1, 10.0, 1.0),
        new IntParameter("outBlack", "Output black", 0, 254, 0),
        new IntParameter("outWhite", "Output white", 1, 255, 255),
    };

    protected override Func<uint, uint> CreateTransform(ParameterSet parameters)
    {
        int inB = parameters.GetInt("inBlack");
        int inW = Math.Max(inB + 1, parameters.GetInt("inWhite"));
        double gamma = parameters.GetDouble("gamma");
        int outB = parameters.GetInt("outBlack");
        int outW = parameters.GetInt("outWhite");
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            double t = Math.Clamp((i - inB) / (double)(inW - inB), 0, 1);
            t = Math.Pow(t, 1.0 / gamma);
            lut[i] = (byte)Math.Clamp(outB + t * (outW - outB), 0, 255);
        }
        return c => ColorBgra.Pack(lut[ColorBgra.B(c)], lut[ColorBgra.G(c)], lut[ColorBgra.R(c)], ColorBgra.A(c));
    }
}

public sealed class PosterizeAdjustment : PerPixelEffect
{
    public override string Name => "Posterize";
    public override bool IsAdjustment => true;

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("levels", "Levels", 2, 64, 4),
    };

    protected override Func<uint, uint> CreateTransform(ParameterSet parameters)
    {
        int levels = parameters.GetInt("levels");
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            int bucket = Math.Min(levels - 1, i * levels / 256);
            lut[i] = (byte)(bucket * 255 / (levels - 1));
        }
        return c => ColorBgra.Pack(lut[ColorBgra.B(c)], lut[ColorBgra.G(c)], lut[ColorBgra.R(c)], ColorBgra.A(c));
    }
}

public sealed class SepiaAdjustment : PerPixelEffect
{
    public override string Name => "Sepia";
    public override bool IsAdjustment => true;

    public override IReadOnlyList<EffectParameter> CreateParameters() => Array.Empty<EffectParameter>();

    protected override Func<uint, uint> CreateTransform(ParameterSet parameters) => c =>
    {
        int r = ColorBgra.R(c), g = ColorBgra.G(c), b = ColorBgra.B(c);
        int tr = (r * 393 + g * 769 + b * 189) / 1000;
        int tg = (r * 349 + g * 686 + b * 168) / 1000;
        int tb = (r * 272 + g * 534 + b * 131) / 1000;
        return ColorBgra.Pack(
            (byte)Math.Min(255, tb), (byte)Math.Min(255, tg), (byte)Math.Min(255, tr), ColorBgra.A(c));
    };
}

public sealed class TransparencyAdjustment : PerPixelEffect
{
    public override string Name => "Transparency";
    public override bool IsAdjustment => true;

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new IntParameter("opacity", "Opacity", 0, 100, 100, "%"),
    };

    protected override Func<uint, uint> CreateTransform(ParameterSet parameters)
    {
        int opacity = parameters.GetInt("opacity");
        return c => ColorBgra.WithAlpha(c, (byte)(ColorBgra.A(c) * opacity / 100));
    }
}
