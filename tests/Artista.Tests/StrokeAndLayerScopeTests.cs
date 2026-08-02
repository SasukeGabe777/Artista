using Artista.Core.ColorEngine;
using Artista.Core.Documents;
using Artista.Core.Drawing;
using Artista.Core.Effects;
using Artista.Core.Imaging;
using Artista.Core.Layers;
using Artista.Core.Selections;

namespace Artista.Tests;

public class StrokeBufferTests
{
    [Fact]
    public void PaintAppliesColorWithinDab()
    {
        var surface = new Surface(20, 20);
        var original = surface.Clone();
        var stroke = new StrokeBuffer(20, 20);
        stroke.StampDab(10, 10, 5, 1.0);
        stroke.ApplyPaint(surface, original, new Selection(20, 20), stroke.DirtyRect,
            ColorBgra.Pack(0, 0, 255, 255), 1f, alphaLock: false);

        Assert.Equal(ColorBgra.Pack(0, 0, 255, 255), surface[10, 10]);
        Assert.Equal(0u, surface[1, 1]);
    }

    [Fact]
    public void OverlappingDabsInOneStrokeDoNotStack()
    {
        var surface = new Surface(20, 20);
        var original = surface.Clone();
        var stroke = new StrokeBuffer(20, 20);
        stroke.StampDab(10, 10, 5, 1.0);
        stroke.StampDab(11, 10, 5, 1.0);
        stroke.ApplyPaint(surface, original, new Selection(20, 20), stroke.DirtyRect,
            ColorBgra.Pack(0, 0, 255, 128), 0.5f, alphaLock: false);

        // Alpha should be exactly one application of 128*0.5, not doubled.
        byte a = ColorBgra.A(surface[10, 10]);
        Assert.InRange<int>(a, 60, 68);
    }

    [Fact]
    public void EraseReducesAlpha()
    {
        var surface = new Surface(20, 20);
        surface.Clear(ColorBgra.Pack(0, 0, 255, 255));
        var original = surface.Clone();
        var stroke = new StrokeBuffer(20, 20);
        stroke.StampDab(10, 10, 4, 1.0);
        stroke.ApplyErase(surface, original, new Selection(20, 20), stroke.DirtyRect, 1f);

        Assert.Equal(0, ColorBgra.A(surface[10, 10]));
        Assert.Equal(255, ColorBgra.A(surface[1, 1]));
    }

    [Fact]
    public void StrokeRespectsSelectionMask()
    {
        var surface = new Surface(20, 20);
        var original = surface.Clone();
        var selection = new Selection(20, 20);
        selection.Combine(SelectionRasterizer.RasterizeRectangle(20, 20, 0, 0, 10, 20), SelectionCombineMode.Replace);

        var stroke = new StrokeBuffer(20, 20);
        stroke.StampDab(10, 10, 6, 1.0);
        stroke.ApplyPaint(surface, original, selection, stroke.DirtyRect,
            ColorBgra.Pack(0, 0, 255, 255), 1f, alphaLock: false);

        Assert.NotEqual(0u, surface[7, 10]);  // inside selection
        Assert.Equal(0u, surface[13, 10]);    // outside selection
    }

    [Fact]
    public void ColorRemoverBrushOnlyRemovesTargetColor()
    {
        var surface = new Surface(20, 20);
        surface.FillRect(new RectInt(0, 0, 10, 20), ColorBgra.Pack(0, 0, 255, 255));   // red left
        surface.FillRect(new RectInt(10, 0, 10, 20), ColorBgra.Pack(255, 0, 0, 255));  // blue right
        var original = surface.Clone();

        var stroke = new StrokeBuffer(20, 20);
        stroke.StampDab(10, 10, 8, 1.0); // brush covers both colors
        var matcher = new ColorMatcher(255, 0, 0, 10, 0); // target = red
        stroke.ApplyColorRemove(surface, original, new Selection(20, 20), stroke.DirtyRect, matcher, 1f);

        Assert.Equal(0, ColorBgra.A(surface[8, 10]));    // red under brush: removed
        Assert.Equal(255, ColorBgra.A(surface[12, 10])); // blue under brush: intact
        Assert.Equal(255, ColorBgra.A(surface[1, 1]));   // red outside brush: intact
    }

    [Fact]
    public void AlphaLockedPaintKeepsAlphaChannel()
    {
        var surface = new Surface(10, 10);
        surface.FillRect(new RectInt(0, 0, 10, 5), ColorBgra.Pack(0, 0, 255, 200));
        var original = surface.Clone();
        var stroke = new StrokeBuffer(10, 10);
        stroke.StampDab(5, 2, 4, 1.0);
        stroke.StampDab(5, 8, 4, 1.0);
        stroke.ApplyPaint(surface, original, new Selection(10, 10), stroke.DirtyRect,
            ColorBgra.Pack(0, 255, 0, 255), 1f, alphaLock: true);

        Assert.Equal(200, ColorBgra.A(surface[5, 2])); // alpha preserved where painted
        Assert.Equal(0, ColorBgra.A(surface[5, 8]));   // transparent stays transparent
    }
}

public class LayerScopeTests
{
    /// <summary>
    /// Mirrors the app-level Remove Color scope logic: apply to multiple layers,
    /// skipping locked layers, optionally including hidden ones.
    /// </summary>
    private static void ApplyRemoveColorToScope(Document doc, ParameterSet parameters, bool includeHidden)
    {
        var effect = new RemoveColorEffect();
        foreach (var layer in doc.Layers)
        {
            if (layer.Locked) continue;
            if (!layer.Visible && !includeHidden) continue;
            var src = layer.Surface.Clone();
            EffectRunner.RunMasked(effect, src, layer.Surface, parameters, doc.Selection, src.Bounds, CancellationToken.None);
        }
    }

    private static ParameterSet RedRemovalParams()
    {
        var effect = new RemoveColorEffect();
        var set = ParameterSet.FromDefaults(effect.CreateParameters());
        set.Set("target", ColorBgra.Pack(0, 0, 255, 255));
        set.Set("tolerance", 5);
        set.Set("softness", 0);
        return set;
    }

    [Fact]
    public void RemoveColorAcrossVisibleLayersSkipsHiddenAndLocked()
    {
        var doc = new Document(4, 4);
        var visible = new Layer(4, 4, "visible");
        visible.Surface.Clear(ColorBgra.Pack(0, 0, 255, 255));
        var hidden = new Layer(4, 4, "hidden") { Visible = false };
        hidden.Surface.Clear(ColorBgra.Pack(0, 0, 255, 255));
        var locked = new Layer(4, 4, "locked") { Locked = true };
        locked.Surface.Clear(ColorBgra.Pack(0, 0, 255, 255));
        doc.Layers.AddRange(new[] { visible, hidden, locked });

        ApplyRemoveColorToScope(doc, RedRemovalParams(), includeHidden: false);

        Assert.Equal(0, ColorBgra.A(visible.Surface[0, 0]));
        Assert.Equal(255, ColorBgra.A(hidden.Surface[0, 0]));
        Assert.Equal(255, ColorBgra.A(locked.Surface[0, 0]));
    }

    [Fact]
    public void RemoveColorIncludingHiddenLayersProcessesThem()
    {
        var doc = new Document(4, 4);
        var hidden = new Layer(4, 4, "hidden") { Visible = false };
        hidden.Surface.Clear(ColorBgra.Pack(0, 0, 255, 255));
        doc.Layers.Add(hidden);

        ApplyRemoveColorToScope(doc, RedRemovalParams(), includeHidden: true);
        Assert.Equal(0, ColorBgra.A(hidden.Surface[0, 0]));
    }

    [Fact]
    public void RemoveColorInSelectionAcrossLayersLeavesOutsideUntouched()
    {
        var doc = new Document(10, 10);
        var l1 = new Layer(10, 10, "l1");
        l1.Surface.Clear(ColorBgra.Pack(0, 0, 255, 255));
        var l2 = new Layer(10, 10, "l2");
        l2.Surface.Clear(ColorBgra.Pack(0, 0, 255, 255));
        doc.Layers.AddRange(new[] { l1, l2 });
        doc.Selection.Combine(SelectionRasterizer.RasterizeRectangle(10, 10, 0, 0, 5, 10), SelectionCombineMode.Replace);

        ApplyRemoveColorToScope(doc, RedRemovalParams(), includeHidden: false);

        foreach (var layer in doc.Layers)
        {
            Assert.Equal(0, ColorBgra.A(layer.Surface[2, 5]));
            Assert.Equal(255, ColorBgra.A(layer.Surface[8, 5]));
        }
    }
}

public class EffectCancellationTests
{
    [Fact]
    public void CancelledGaussianBlurThrowsAndPreservesSource()
    {
        var src = new Surface(128, 128);
        src.Clear(ColorBgra.Pack(50, 100, 150, 255));
        var snapshot = src.Clone();
        var dst = new Surface(128, 128);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var effect = new GaussianBlurEffect();
        var parameters = ParameterSet.FromDefaults(effect.CreateParameters());
        Assert.ThrowsAny<OperationCanceledException>(() =>
            effect.Render(src, dst, src.Bounds, parameters, cts.Token));
        Assert.Equal(snapshot.Pixels, src.Pixels);
    }

    [Fact]
    public void AdjustmentsProduceExpectedResults()
    {
        var src = new Surface(2, 2);
        src.Clear(ColorBgra.Pack(100, 100, 100, 255));
        var dst = new Surface(2, 2);

        var invert = new InvertColorsAdjustment();
        invert.Render(src, dst, src.Bounds, ParameterSet.FromDefaults(invert.CreateParameters()), CancellationToken.None);
        Assert.Equal(ColorBgra.Pack(155, 155, 155, 255), dst[0, 0]);

        var bw = new BlackAndWhiteAdjustment();
        src.Clear(ColorBgra.Pack(0, 0, 255, 255)); // pure red
        bw.Render(src, dst, src.Bounds, ParameterSet.FromDefaults(bw.CreateParameters()), CancellationToken.None);
        Assert.Equal(ColorBgra.G(dst[0, 0]), ColorBgra.B(dst[0, 0]));
        Assert.Equal(ColorBgra.G(dst[0, 0]), ColorBgra.R(dst[0, 0]));
        Assert.InRange<int>(ColorBgra.R(dst[0, 0]), 70, 80); // luma of red ≈ 76
    }
}
