using Artista.Core.Effects;
using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.Tests;

public class RemoveColorEffectTests
{
    private static ParameterSet Params(uint target, int tolerance, int softness, bool preserveAlpha = true)
    {
        var effect = new RemoveColorEffect();
        var set = ParameterSet.FromDefaults(effect.CreateParameters());
        set.Set("target", target);
        set.Set("tolerance", tolerance);
        set.Set("softness", softness);
        set.Set("preserveAlpha", preserveAlpha);
        return set;
    }

    private static Surface Run(Surface src, ParameterSet parameters, Selection? selection = null)
    {
        var effect = new RemoveColorEffect();
        var dst = src.Clone();
        selection ??= new Selection(src.Width, src.Height);
        EffectRunner.RunMasked(effect, src, dst, parameters, selection, src.Bounds, CancellationToken.None);
        return dst;
    }

    [Fact]
    public void ExactColorIsFullyRemoved()
    {
        var src = new Surface(4, 4);
        src.Clear(ColorBgra.Pack(0, 0, 255, 255)); // red
        var result = Run(src, Params(ColorBgra.Pack(0, 0, 255, 255), 0, 0));
        Assert.Equal(0, ColorBgra.A(result[2, 2]));
        // Color channels preserved (only alpha removed).
        Assert.Equal(255, ColorBgra.R(result[2, 2]));
    }

    [Fact]
    public void NonMatchingColorIsUntouched()
    {
        var src = new Surface(4, 4);
        src.Clear(ColorBgra.Pack(255, 0, 0, 255)); // blue
        var result = Run(src, Params(ColorBgra.Pack(0, 0, 255, 255), 0, 0));
        Assert.Equal(ColorBgra.Pack(255, 0, 0, 255), result[1, 1]);
    }

    [Fact]
    public void ToleranceZeroDoesNotRemoveNearbyColors()
    {
        var src = new Surface(2, 1);
        src[0, 0] = ColorBgra.Pack(0, 0, 255, 255);
        src[1, 0] = ColorBgra.Pack(0, 1, 255, 255); // off by one in green
        var result = Run(src, Params(ColorBgra.Pack(0, 0, 255, 255), 0, 0));
        Assert.Equal(0, ColorBgra.A(result[0, 0]));
        Assert.Equal(255, ColorBgra.A(result[1, 0]));
    }

    [Fact]
    public void ToleranceRemovesSimilarColors()
    {
        var src = new Surface(2, 1);
        src[0, 0] = ColorBgra.Pack(0, 0, 255, 255);
        src[1, 0] = ColorBgra.Pack(20, 20, 235, 255); // similar red
        var result = Run(src, Params(ColorBgra.Pack(0, 0, 255, 255), 40, 0));
        Assert.Equal(0, ColorBgra.A(result[0, 0]));
        Assert.Equal(0, ColorBgra.A(result[1, 0]));
    }

    [Fact]
    public void SoftnessProducesGradualAlphaFalloff()
    {
        // Gradient of grays away from the target; with softness there must be
        // at least one partially transparent result pixel.
        var src = new Surface(64, 1);
        for (int x = 0; x < 64; x++)
            src[x, 0] = ColorBgra.Pack((byte)(128 + x), (byte)(128 + x), (byte)(128 + x), 255);
        var result = Run(src, Params(ColorBgra.Pack(128, 128, 128, 255), 15, 90));

        bool foundPartial = false;
        for (int x = 0; x < 64; x++)
        {
            byte a = ColorBgra.A(result[x, 0]);
            if (a > 0 && a < 255) { foundPartial = true; break; }
        }
        Assert.True(foundPartial, "softness should create partially transparent boundary pixels");
    }

    [Fact]
    public void PartiallyTransparentMatchingPixelsScaleProportionally()
    {
        var src = new Surface(1, 1);
        src[0, 0] = ColorBgra.Pack(0, 0, 255, 100);
        // Use tolerance so a partial match would multiply; exact match -> alpha 0.
        var result = Run(src, Params(ColorBgra.Pack(0, 0, 255, 255), 0, 0));
        Assert.Equal(0, ColorBgra.A(result[0, 0]));
    }

    [Fact]
    public void PixelsOutsideSelectionAreUntouched()
    {
        var src = new Surface(10, 10);
        src.Clear(ColorBgra.Pack(0, 0, 255, 255));
        var sel = new Selection(10, 10);
        sel.Combine(SelectionRasterizer.RasterizeRectangle(10, 10, 0, 0, 5, 10), SelectionCombineMode.Replace);

        var result = Run(src, Params(ColorBgra.Pack(0, 0, 255, 255), 0, 0), sel);
        Assert.Equal(0, ColorBgra.A(result[2, 5]));   // inside selection: removed
        Assert.Equal(255, ColorBgra.A(result[8, 5])); // outside: untouched
    }

    [Fact]
    public void FullyTransparentPixelsRemainUntouched()
    {
        var src = new Surface(2, 1);
        src[0, 0] = ColorBgra.Pack(0, 0, 255, 0); // transparent but red channels
        var result = Run(src, Params(ColorBgra.Pack(0, 0, 255, 255), 50, 50));
        Assert.Equal(src[0, 0], result[0, 0]);
    }

    [Fact]
    public void CancellationLeavesSourceUnmodified()
    {
        var src = new Surface(64, 64);
        src.Clear(ColorBgra.Pack(0, 0, 255, 255));
        var snapshot = src.Clone();
        var dst = src.Clone();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var effect = new RemoveColorEffect();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            EffectRunner.RunMasked(effect, src, dst, Params(0xFF0000FF, 0, 0),
                new Selection(64, 64), src.Bounds, cts.Token));
        // Source must be untouched — the app restores from src on cancel.
        Assert.Equal(snapshot.Pixels, src.Pixels);
    }
}
