using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.Tests;

public class SelectionTests
{
    [Fact]
    public void EmptySelectionMeansEverythingEditable()
    {
        var sel = new Selection(10, 10);
        Assert.True(sel.IsEmpty);
        Assert.Equal(255, sel.CoverageAt(5, 5));
        Assert.Equal(new RectInt(0, 0, 10, 10), sel.EffectiveBounds);
    }

    [Fact]
    public void RectangleSelectionHasCorrectBoundsAndCoverage()
    {
        var sel = new Selection(20, 20);
        var mask = SelectionRasterizer.RasterizeRectangle(20, 20, 5, 5, 15, 15);
        sel.Combine(mask, SelectionCombineMode.Replace);

        Assert.False(sel.IsEmpty);
        Assert.Equal(255, sel.MaskAt(10, 10));
        Assert.Equal(0, sel.MaskAt(2, 2));
        var b = sel.Bounds;
        Assert.Equal(5, b.Left);
        Assert.Equal(5, b.Top);
        Assert.Equal(15, b.Right);
        Assert.Equal(15, b.Bottom);
    }

    [Fact]
    public void AddCombineUnionsRegions()
    {
        var sel = new Selection(20, 20);
        sel.Combine(SelectionRasterizer.RasterizeRectangle(20, 20, 0, 0, 5, 5), SelectionCombineMode.Replace);
        sel.Combine(SelectionRasterizer.RasterizeRectangle(20, 20, 10, 10, 15, 15), SelectionCombineMode.Add);
        Assert.Equal(255, sel.MaskAt(2, 2));
        Assert.Equal(255, sel.MaskAt(12, 12));
        Assert.Equal(0, sel.MaskAt(7, 7));
    }

    [Fact]
    public void SubtractCombineRemovesRegion()
    {
        var sel = new Selection(20, 20);
        sel.Combine(SelectionRasterizer.RasterizeRectangle(20, 20, 0, 0, 20, 20), SelectionCombineMode.Replace);
        sel.Combine(SelectionRasterizer.RasterizeRectangle(20, 20, 5, 5, 15, 15), SelectionCombineMode.Subtract);
        Assert.Equal(255, sel.MaskAt(2, 2));
        Assert.Equal(0, sel.MaskAt(10, 10));
    }

    [Fact]
    public void IntersectCombineKeepsOverlapOnly()
    {
        var sel = new Selection(20, 20);
        sel.Combine(SelectionRasterizer.RasterizeRectangle(20, 20, 0, 0, 12, 12), SelectionCombineMode.Replace);
        sel.Combine(SelectionRasterizer.RasterizeRectangle(20, 20, 8, 8, 20, 20), SelectionCombineMode.Intersect);
        Assert.Equal(0, sel.MaskAt(2, 2));
        Assert.Equal(0, sel.MaskAt(15, 15));
        Assert.Equal(255, sel.MaskAt(10, 10));
    }

    [Fact]
    public void InvertFlipsCoverage()
    {
        var sel = new Selection(10, 10);
        sel.Combine(SelectionRasterizer.RasterizeRectangle(10, 10, 0, 0, 5, 10), SelectionCombineMode.Replace);
        sel.Invert();
        Assert.Equal(0, sel.MaskAt(2, 5));
        Assert.Equal(255, sel.MaskAt(7, 5));
    }

    [Fact]
    public void EllipseSelectionHasAntialiasedEdge()
    {
        var mask = SelectionRasterizer.RasterizeEllipse(40, 40, 4, 4, 36, 36);
        // Center fully covered.
        Assert.Equal(255, mask[20 * 40 + 20]);
        // Far corner not covered.
        Assert.Equal(0, mask[1 * 40 + 1]);
        // Some partial coverage exists along the boundary.
        bool anyPartial = mask.Any(v => v > 0 && v < 255);
        Assert.True(anyPartial, "expected antialiased boundary pixels");
    }

    [Fact]
    public void TranslateMovesSelection()
    {
        var sel = new Selection(20, 20);
        sel.Combine(SelectionRasterizer.RasterizeRectangle(20, 20, 0, 0, 5, 5), SelectionCombineMode.Replace);
        sel.Translate(10, 10);
        Assert.Equal(0, sel.MaskAt(2, 2));
        Assert.Equal(255, sel.MaskAt(12, 12));
    }

    [Fact]
    public void FeatherSoftensEdges()
    {
        var sel = new Selection(40, 40);
        sel.Combine(SelectionRasterizer.RasterizeRectangle(40, 40, 10, 10, 30, 30), SelectionCombineMode.Replace);
        sel.Feather(4);
        // Center still strongly selected, boundary now partial.
        Assert.True(sel.MaskAt(20, 20) > 200);
        byte edge = sel.MaskAt(10, 20);
        Assert.InRange<int>(edge, 1, 254);
    }

    [Fact]
    public void MagicWandSelectsContiguousRegion()
    {
        var surface = new Surface(10, 10);
        surface.Clear(ColorBgra.Pack(255, 255, 255, 255));
        // Two red squares, not touching.
        surface.FillRect(new RectInt(0, 0, 3, 3), ColorBgra.Pack(0, 0, 255, 255));
        surface.FillRect(new RectInt(7, 7, 3, 3), ColorBgra.Pack(0, 0, 255, 255));

        var mask = FloodFill.ComputeMask(surface, 1, 1, 0, contiguous: true);
        Assert.Equal(255, mask[1 * 10 + 1]);
        Assert.Equal(0, mask[8 * 10 + 8]);
    }

    [Fact]
    public void MagicWandGlobalModeSelectsAllMatchingPixels()
    {
        var surface = new Surface(10, 10);
        surface.Clear(ColorBgra.Pack(255, 255, 255, 255));
        surface.FillRect(new RectInt(0, 0, 3, 3), ColorBgra.Pack(0, 0, 255, 255));
        surface.FillRect(new RectInt(7, 7, 3, 3), ColorBgra.Pack(0, 0, 255, 255));

        var mask = FloodFill.ComputeMask(surface, 1, 1, 0, contiguous: false);
        Assert.Equal(255, mask[1 * 10 + 1]);
        Assert.Equal(255, mask[8 * 10 + 8]);
        Assert.Equal(0, mask[5 * 10 + 5]);
    }

    [Fact]
    public void MagicWandToleranceExpandsMatch()
    {
        var surface = new Surface(4, 1);
        surface[0, 0] = ColorBgra.Pack(100, 100, 100, 255);
        surface[1, 0] = ColorBgra.Pack(110, 110, 110, 255);
        surface[2, 0] = ColorBgra.Pack(200, 200, 200, 255);
        surface[3, 0] = ColorBgra.Pack(100, 100, 100, 255);

        var strict = FloodFill.ComputeMask(surface, 0, 0, 0, contiguous: true);
        Assert.Equal(255, strict[0]);
        Assert.Equal(0, strict[1]);

        var loose = FloodFill.ComputeMask(surface, 0, 0, 25, contiguous: true);
        Assert.Equal(255, loose[1]);
        Assert.Equal(0, loose[2]);
    }
}
