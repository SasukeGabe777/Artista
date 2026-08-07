using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.Tests;

public class SpriteAnalysisTests
{
    private static readonly uint OpaqueRed = ColorBgra.Pack(20, 30, 220, 255);

    [Fact]
    public void DetectionHappensAcrossGridLinesBeforeCellAssignment()
    {
        var surface = new Surface(32, 16);
        surface.FillRect(new RectInt(10, 4, 8, 8), OpaqueRed);
        var selection = Select(surface, new RectInt(0, 0, 16, 16));

        var sprites = SpriteDetector.Detect(surface, selection,
            new SpriteDetectionOptions(InspectionMargin: 8, ExpectedCellWidth: 16, ExpectedCellHeight: 16));
        var assignments = SpriteGridAnalyzer.AssignToCells(
            sprites, new SpriteGridLayout(16, 16), surface.Width);

        Assert.Single(sprites);
        Assert.Equal(new RectInt(10, 4, 8, 8), sprites[0].Bounds);
        Assert.Equal(0, assignments[0].Cell.Column);
        Assert.Equal(0.75, assignments[0].OverlapRatio, 2);
    }

    [Fact]
    public void AlignmentMovesEverySpritePixelExactlyAndCentersIt()
    {
        var surface = new Surface(32, 16);
        for (int y = 4; y < 12; y++)
            for (int x = 10; x < 18; x++)
                surface[x, y] = ColorBgra.Pack((byte)x, (byte)y, 180, (byte)(150 + (x + y) % 106));
        var originalColors = surface.Pixels.Where(p => ColorBgra.A(p) > 0).OrderBy(p => p).ToArray();
        var selection = Select(surface, new RectInt(0, 0, 16, 16));
        var sprites = SpriteDetector.Detect(surface, selection,
            new SpriteDetectionOptions(InspectionMargin: 8, ExpectedCellWidth: 16, ExpectedCellHeight: 16));

        var plan = SpriteGridAnalyzer.PlanAlignment(surface, sprites, new SpriteGridLayout(16, 16));
        SpriteGridAnalyzer.ApplyAlignment(surface, plan);

        Assert.Single(plan.Moves);
        Assert.Equal(new RectInt(4, 4, 8, 8), plan.Moves[0].DestinationBounds);
        Assert.Equal(0, ColorBgra.A(surface[17, 8]));
        Assert.Equal(originalColors,
            surface.Pixels.Where(p => ColorBgra.A(p) > 0).OrderBy(p => p).ToArray());
    }

    [Fact]
    public void NearbySmallDisconnectedFragmentCanJoinMainSprite()
    {
        var surface = new Surface(32, 20);
        surface.FillRect(new RectInt(4, 5, 6, 8), OpaqueRed);
        surface.FillRect(new RectInt(12, 7, 1, 2), OpaqueRed);
        var selection = Select(surface, new RectInt(3, 4, 8, 10));

        var sprites = SpriteDetector.Detect(surface, selection,
            new SpriteDetectionOptions(InspectionMargin: 4, ExpectedCellWidth: 16, ExpectedCellHeight: 16));

        Assert.Single(sprites);
        Assert.Equal(2, sprites[0].ComponentCount);
        Assert.Equal(new RectInt(4, 5, 9, 8), sprites[0].Bounds);
    }

    [Fact]
    public void ComponentCutByInspectionMarginIsSkippedRatherThanPartiallyMoved()
    {
        var surface = new Surface(64, 16);
        surface.FillRect(new RectInt(8, 4, 30, 8), OpaqueRed);
        var selection = Select(surface, new RectInt(8, 4, 8, 8));

        var sprites = SpriteDetector.Detect(surface, selection,
            new SpriteDetectionOptions(InspectionMargin: 4, ExpectedCellWidth: 16, ExpectedCellHeight: 16));

        Assert.Empty(sprites);
    }

    [Fact]
    public void GridOriginFitUsesAllDetectedSprites()
    {
        var surface = new Surface(48, 20);
        surface.FillRect(new RectInt(9, 5, 8, 8), OpaqueRed);
        surface.FillRect(new RectInt(25, 5, 8, 8), OpaqueRed);
        var sprites = SpriteDetector.Detect(surface, Select(surface, surface.Bounds),
            new SpriteDetectionOptions(ExpectedCellWidth: 16, ExpectedCellHeight: 16));

        var fit = SpriteGridAnalyzer.FindBestOrigin(sprites, new SpriteGridLayout(16, 16));

        Assert.Equal(5, fit.Layout.OriginX);
        Assert.Equal(1, fit.Layout.OriginY);
        Assert.Equal(2, fit.FullyContainedSprites);
        Assert.Equal(0, fit.CrossedSprites);
    }

    [Fact]
    public void GridInferenceFindsStandardCellSpacingAndOffset()
    {
        var surface = new Surface(150, 150);
        foreach (int y in new[] { 16, 82 })
            foreach (int x in new[] { 16, 82 })
                surface.FillRect(new RectInt(x, y, 40, 40), OpaqueRed);
        var sprites = SpriteDetector.Detect(surface, Select(surface, surface.Bounds),
            new SpriteDetectionOptions(InspectionMargin: 0));

        var inferred = SpriteGridAnalyzer.InferGrid(sprites, surface.Bounds);

        Assert.Equal(64, inferred.Layout.CellWidth);
        Assert.Equal(64, inferred.Layout.CellHeight);
        Assert.Equal(2, inferred.Layout.SpacingX);
        Assert.Equal(2, inferred.Layout.SpacingY);
        Assert.Equal(4, inferred.Layout.OriginX);
        Assert.Equal(4, inferred.Layout.OriginY);
        Assert.Equal(4, inferred.Fit.FullyContainedSprites);
    }

    private static Selection Select(Surface surface, RectInt rect)
    {
        var selection = new Selection(surface.Width, surface.Height);
        selection.Combine(SelectionRasterizer.RasterizeRectangle(
            surface.Width, surface.Height, rect.Left, rect.Top, rect.Right, rect.Bottom),
            SelectionCombineMode.Replace);
        return selection;
    }
}
