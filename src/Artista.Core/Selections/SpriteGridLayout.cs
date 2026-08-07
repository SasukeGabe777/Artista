using Artista.Core.Imaging;

namespace Artista.Core.Selections;

/// <summary>
/// Pixel-aligned frame grid shared by the canvas, selection tools, and sprite
/// preview. Only complete cells are selectable so every resulting frame has
/// exactly the configured dimensions.
/// </summary>
public readonly record struct SpriteGridLayout(int CellWidth, int CellHeight)
{
    public bool IsValid => CellWidth > 0 && CellHeight > 0;

    public int Columns(int documentWidth) =>
        IsValid ? Math.Max(0, documentWidth / CellWidth) : 0;

    public int Rows(int documentHeight) =>
        IsValid ? Math.Max(0, documentHeight / CellHeight) : 0;

    public RectInt CellBounds(int column, int row) =>
        new(column * CellWidth, row * CellHeight, CellWidth, CellHeight);

    /// <summary>Returns the inclusive set of complete cells touched by a drag.</summary>
    public RectInt SnapDrag(double startX, double startY, double currentX, double currentY,
        int documentWidth, int documentHeight)
    {
        int columns = Columns(documentWidth), rows = Rows(documentHeight);
        if (columns == 0 || rows == 0) return RectInt.Empty;

        int startColumn = CellIndex(startX, CellWidth, columns);
        int currentColumn = CellIndex(currentX, CellWidth, columns);
        int startRow = CellIndex(startY, CellHeight, rows);
        int currentRow = CellIndex(currentY, CellHeight, rows);

        int leftColumn = Math.Min(startColumn, currentColumn);
        int rightColumn = Math.Max(startColumn, currentColumn) + 1;
        int topRow = Math.Min(startRow, currentRow);
        int bottomRow = Math.Max(startRow, currentRow) + 1;
        return RectInt.FromLTRB(
            leftColumn * CellWidth,
            topRow * CellHeight,
            rightColumn * CellWidth,
            bottomRow * CellHeight);
    }

    public IEnumerable<RectInt> EnumerateCompleteCells(int documentWidth, int documentHeight)
    {
        int columns = Columns(documentWidth), rows = Rows(documentHeight);
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
                yield return CellBounds(column, row);
    }

    private static int CellIndex(double coordinate, int cellSize, int count)
    {
        if (double.IsNaN(coordinate)) return 0;
        if (double.IsNegativeInfinity(coordinate)) return 0;
        if (double.IsPositiveInfinity(coordinate)) return count - 1;
        return Math.Clamp((int)Math.Floor(coordinate / cellSize), 0, count - 1);
    }
}
