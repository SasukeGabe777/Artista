using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.Tests;

public class SpriteGridTests
{
    [Fact]
    public void DragSnapsToEveryTouchedCompleteCell()
    {
        var grid = new SpriteGridLayout(16, 12);

        var bounds = grid.SnapDrag(19.5, 14, 50, 29, 96, 60);

        Assert.Equal(new RectInt(16, 12, 48, 24), bounds);
    }

    [Fact]
    public void ReverseAndOutsideDragClampsToCompleteGrid()
    {
        var grid = new SpriteGridLayout(16, 16);

        var bounds = grid.SnapDrag(39, 39, -50, -20, 50, 50);

        Assert.Equal(new RectInt(0, 0, 48, 48), bounds);
    }

    [Fact]
    public void CompleteCellsExcludePartialDocumentEdges()
    {
        var grid = new SpriteGridLayout(16, 12);

        var cells = grid.EnumerateCompleteCells(35, 25).ToList();

        Assert.Equal(4, cells.Count);
        Assert.Equal(new RectInt(16, 12, 16, 12), cells[^1]);
    }

    [Fact]
    public void OriginAndSpacingAreIncludedInCellGeometry()
    {
        var grid = new SpriteGridLayout(16, 12, 3, 5, 2, 4);

        Assert.Equal(new RectInt(21, 21, 16, 12), grid.CellBounds(1, 1));
        Assert.True(grid.TryGetCellAt(22, 22, out var cell));
        Assert.Equal((1, 1), (cell.Column, cell.Row));
        Assert.False(grid.TryGetCellAt(19, 10, out _));
    }

    [Fact]
    public void SpacedGridDragReturnsCellsWithoutSelectingGaps()
    {
        var grid = new SpriteGridLayout(8, 8, 2, 2, 2, 2);

        var cells = grid.CellsTouchedByDrag(3, 3, 17, 7, 40, 30).ToList();

        Assert.Equal(2, cells.Count);
        Assert.Equal(new RectInt(2, 2, 8, 8), cells[0].Bounds);
        Assert.Equal(new RectInt(12, 2, 8, 8), cells[1].Bounds);
    }

    [Fact]
    public void NormalizedOriginPreservesSameInfiniteGrid()
    {
        var grid = new SpriteGridLayout(16, 16, -17, 34, 2, 2).WithNormalizedOrigin();

        Assert.Equal(1, grid.OriginX);
        Assert.Equal(16, grid.OriginY);
        Assert.Equal(18, grid.PitchX);
    }
}
