using Artista.Core.Imaging;

namespace Artista.Core.Selections;

public readonly record struct SpriteGridCell(int Column, int Row, RectInt Bounds);

/// <summary>
/// Pixel-aligned frame grid shared by canvas rendering, selection tools, and
/// sprite analysis. Origin and spacing are first-class so imported sheets do
/// not have to begin at document coordinate zero or pack cells edge-to-edge.
/// </summary>
public readonly record struct SpriteGridLayout(
    int CellWidth,
    int CellHeight,
    int OriginX = 0,
    int OriginY = 0,
    int SpacingX = 0,
    int SpacingY = 0)
{
    public bool IsValid =>
        CellWidth > 0 && CellHeight > 0 && SpacingX >= 0 && SpacingY >= 0;

    public int PitchX => CellWidth + SpacingX;
    public int PitchY => CellHeight + SpacingY;

    public int Columns(int documentWidth) => CompleteColumnRange(documentWidth).Count;
    public int Rows(int documentHeight) => CompleteRowRange(documentHeight).Count;

    public RectInt CellBounds(int column, int row) =>
        new(OriginX + column * PitchX, OriginY + row * PitchY, CellWidth, CellHeight);

    /// <summary>Returns the inclusive set of complete cells touched by a drag.</summary>
    public RectInt SnapDrag(double startX, double startY, double currentX, double currentY,
        int documentWidth, int documentHeight)
    {
        var columns = CompleteColumnRange(documentWidth);
        var rows = CompleteRowRange(documentHeight);
        if (columns.Count == 0 || rows.Count == 0) return RectInt.Empty;

        int startColumn = Math.Clamp(NearestIndex(startX, OriginX, CellWidth, PitchX), columns.Min, columns.Max);
        int currentColumn = Math.Clamp(NearestIndex(currentX, OriginX, CellWidth, PitchX), columns.Min, columns.Max);
        int startRow = Math.Clamp(NearestIndex(startY, OriginY, CellHeight, PitchY), rows.Min, rows.Max);
        int currentRow = Math.Clamp(NearestIndex(currentY, OriginY, CellHeight, PitchY), rows.Min, rows.Max);

        var first = CellBounds(Math.Min(startColumn, currentColumn), Math.Min(startRow, currentRow));
        var last = CellBounds(Math.Max(startColumn, currentColumn), Math.Max(startRow, currentRow));
        return RectInt.FromLTRB(first.Left, first.Top, last.Right, last.Bottom);
    }

    public IEnumerable<SpriteGridCell> CellsTouchedByDrag(
        double startX, double startY, double currentX, double currentY,
        int documentWidth, int documentHeight)
    {
        var columns = CompleteColumnRange(documentWidth);
        var rows = CompleteRowRange(documentHeight);
        if (columns.Count == 0 || rows.Count == 0) yield break;

        int c0 = Math.Clamp(NearestIndex(startX, OriginX, CellWidth, PitchX), columns.Min, columns.Max);
        int c1 = Math.Clamp(NearestIndex(currentX, OriginX, CellWidth, PitchX), columns.Min, columns.Max);
        int r0 = Math.Clamp(NearestIndex(startY, OriginY, CellHeight, PitchY), rows.Min, rows.Max);
        int r1 = Math.Clamp(NearestIndex(currentY, OriginY, CellHeight, PitchY), rows.Min, rows.Max);
        for (int row = Math.Min(r0, r1); row <= Math.Max(r0, r1); row++)
            for (int column = Math.Min(c0, c1); column <= Math.Max(c0, c1); column++)
                yield return new SpriteGridCell(column, row, CellBounds(column, row));
    }

    public IEnumerable<RectInt> EnumerateCompleteCells(int documentWidth, int documentHeight)
    {
        var columns = CompleteColumnRange(documentWidth);
        var rows = CompleteRowRange(documentHeight);
        for (int row = rows.Min; row <= rows.Max; row++)
            for (int column = columns.Min; column <= columns.Max; column++)
                yield return CellBounds(column, row);
    }

    public IEnumerable<SpriteGridCell> EnumerateCellsIntersecting(RectInt region)
    {
        if (!IsValid || region.IsEmpty) yield break;
        int minColumn = FloorDiv(region.Left - OriginX - CellWidth, PitchX) + 1;
        int maxColumn = FloorDiv(region.Right - 1 - OriginX, PitchX);
        int minRow = FloorDiv(region.Top - OriginY - CellHeight, PitchY) + 1;
        int maxRow = FloorDiv(region.Bottom - 1 - OriginY, PitchY);
        for (int row = minRow; row <= maxRow; row++)
        {
            for (int column = minColumn; column <= maxColumn; column++)
            {
                var bounds = CellBounds(column, row);
                if (bounds.IntersectsWith(region))
                    yield return new SpriteGridCell(column, row, bounds);
            }
        }
    }

    public bool TryGetCellAt(int x, int y, out SpriteGridCell cell)
    {
        cell = default;
        if (!IsValid) return false;
        int column = FloorDiv(x - OriginX, PitchX);
        int row = FloorDiv(y - OriginY, PitchY);
        var bounds = CellBounds(column, row);
        if (!bounds.Contains(x, y)) return false;
        cell = new SpriteGridCell(column, row, bounds);
        return true;
    }

    public SpriteGridCell NearestCell(double x, double y)
    {
        int column = NearestIndex(x, OriginX, CellWidth, PitchX);
        int row = NearestIndex(y, OriginY, CellHeight, PitchY);
        return new SpriteGridCell(column, row, CellBounds(column, row));
    }

    public SpriteGridLayout WithNormalizedOrigin() => this with
    {
        OriginX = PositiveMod(OriginX, PitchX),
        OriginY = PositiveMod(OriginY, PitchY),
    };

    public (int Min, int Max, int Count) CompleteColumnRange(int documentWidth) =>
        CompleteRange(documentWidth, OriginX, CellWidth, PitchX);

    public (int Min, int Max, int Count) CompleteRowRange(int documentHeight) =>
        CompleteRange(documentHeight, OriginY, CellHeight, PitchY);

    public (int Min, int Max, int Count) IntersectingColumnRange(int documentWidth) =>
        IntersectingRange(documentWidth, OriginX, CellWidth, PitchX);

    public (int Min, int Max, int Count) IntersectingRowRange(int documentHeight) =>
        IntersectingRange(documentHeight, OriginY, CellHeight, PitchY);

    private static (int Min, int Max, int Count) CompleteRange(
        int documentSize, int origin, int cellSize, int pitch)
    {
        if (documentSize <= 0 || cellSize <= 0 || pitch < cellSize)
            return (0, -1, 0);
        int min = CeilingDiv(-origin, pitch);
        int max = FloorDiv(documentSize - cellSize - origin, pitch);
        return max >= min ? (min, max, max - min + 1) : (0, -1, 0);
    }

    private static (int Min, int Max, int Count) IntersectingRange(
        int documentSize, int origin, int cellSize, int pitch)
    {
        if (documentSize <= 0 || cellSize <= 0 || pitch < cellSize)
            return (0, -1, 0);
        int min = FloorDiv(-origin - cellSize, pitch) + 1;
        int max = FloorDiv(documentSize - 1 - origin, pitch);
        return max >= min ? (min, max, max - min + 1) : (0, -1, 0);
    }

    private static int NearestIndex(double coordinate, int origin, int cellSize, int pitch)
    {
        if (double.IsNaN(coordinate) || double.IsNegativeInfinity(coordinate)) return 0;
        if (double.IsPositiveInfinity(coordinate)) return int.MaxValue / Math.Max(1, pitch);
        int index = (int)Math.Floor((coordinate - origin) / pitch);
        double local = coordinate - (origin + index * pitch);
        if (local > cellSize && local - cellSize > (pitch - local))
            index++;
        return index;
    }

    internal static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static int CeilingDiv(int value, int divisor) => -FloorDiv(-value, divisor);

    private static int PositiveMod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}
