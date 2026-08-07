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
}
