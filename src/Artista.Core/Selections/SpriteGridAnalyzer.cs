using Artista.Core.Imaging;

namespace Artista.Core.Selections;

public sealed record SpriteCellAssignment(
    DetectedSprite Sprite,
    SpriteGridCell Cell,
    int OverlapPixels,
    double OverlapRatio,
    double Score);

public sealed record SpriteMovePlan(
    DetectedSprite Sprite,
    SpriteGridCell Cell,
    RectInt DestinationBounds,
    int DeltaX,
    int DeltaY,
    double AssignmentConfidence);

public sealed record SpriteAlignmentPlan(
    IReadOnlyList<SpriteMovePlan> Moves,
    IReadOnlyList<DetectedSprite> SkippedSprites,
    RectInt AffectedBounds)
{
    public int DetectedCount => Moves.Count + SkippedSprites.Count;
}

public sealed record SpriteGridFit(
    SpriteGridLayout Layout,
    double Score,
    int FullyContainedSprites,
    int CrossedSprites,
    double MeanCenterError);

public sealed record SpriteGridInference(
    SpriteGridLayout Layout,
    SpriteGridFit Fit,
    double Confidence);

/// <summary>
/// Assigns already-detected sprites to cells, plans lossless centering moves,
/// scores grid origins, and infers repeating grid dimensions. Detection is
/// intentionally a separate prerequisite so grid lines never clip sprites.
/// </summary>
public static class SpriteGridAnalyzer
{
    private static readonly int[] StandardCellSizes =
        { 8, 12, 16, 20, 24, 32, 40, 48, 64, 80, 96, 128, 160, 192, 256, 384, 512 };

    public static IReadOnlyList<SpriteCellAssignment> AssignToCells(
        IReadOnlyList<DetectedSprite> sprites, SpriteGridLayout grid, int surfaceWidth)
    {
        if (!grid.IsValid) return Array.Empty<SpriteCellAssignment>();
        var result = new List<SpriteCellAssignment>(sprites.Count);
        foreach (var sprite in sprites)
        {
            var overlaps = new Dictionary<(int Column, int Row), int>();
            foreach (int index in sprite.PixelIndices)
            {
                int x = index % surfaceWidth, y = index / surfaceWidth;
                if (!grid.TryGetCellAt(x, y, out var cell)) continue;
                var key = (cell.Column, cell.Row);
                overlaps[key] = overlaps.GetValueOrDefault(key) + 1;
            }

            SpriteGridCell target;
            int overlap;
            if (overlaps.Count > 0)
            {
                var best = overlaps
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => CenterDistance(sprite, grid.CellBounds(pair.Key.Column, pair.Key.Row)))
                    .First();
                target = new SpriteGridCell(best.Key.Column, best.Key.Row,
                    grid.CellBounds(best.Key.Column, best.Key.Row));
                overlap = best.Value;
            }
            else
            {
                target = grid.NearestCell(sprite.CentroidX, sprite.CentroidY);
                overlap = 0;
            }

            double ratio = sprite.PixelCount == 0 ? 0 : (double)overlap / sprite.PixelCount;
            double score = ratio * 10_000 - CenterDistance(sprite, target.Bounds);
            result.Add(new SpriteCellAssignment(sprite, target, overlap, ratio, score));
        }
        return result;
    }

    public static SpriteAlignmentPlan PlanAlignment(
        Surface surface, IReadOnlyList<DetectedSprite> sprites, SpriteGridLayout grid)
    {
        var assignments = AssignToCells(sprites, grid, surface.Width);
        var skipped = new HashSet<DetectedSprite>();
        var candidates = new List<SpriteMovePlan>();

        foreach (var collisionGroup in assignments.GroupBy(a => (a.Cell.Column, a.Cell.Row)))
        {
            var ordered = collisionGroup.OrderByDescending(a => a.Score).ToArray();
            var assignment = ordered[0];
            foreach (var extra in ordered.Skip(1)) skipped.Add(extra.Sprite);
            var sprite = assignment.Sprite;
            if (sprite.Bounds.Width > grid.CellWidth || sprite.Bounds.Height > grid.CellHeight)
            {
                skipped.Add(sprite);
                continue;
            }

            int left = assignment.Cell.Bounds.Left + (grid.CellWidth - sprite.Bounds.Width) / 2;
            int top = assignment.Cell.Bounds.Top + (grid.CellHeight - sprite.Bounds.Height) / 2;
            var destination = new RectInt(left, top, sprite.Bounds.Width, sprite.Bounds.Height);
            if (destination.Intersect(surface.Bounds) != destination)
            {
                skipped.Add(sprite);
                continue;
            }
            candidates.Add(new SpriteMovePlan(
                sprite,
                assignment.Cell,
                destination,
                left - sprite.Bounds.Left,
                top - sprite.Bounds.Top,
                assignment.OverlapRatio));
        }

        // Reject moves that would overwrite opaque pixels not belonging to any
        // planned sprite. Re-evaluate until removing one unsafe move cannot
        // expose another collision.
        bool changed;
        do
        {
            changed = false;
            var sourcePixels = candidates.SelectMany(m => m.Sprite.PixelIndices).ToHashSet();
            foreach (var move in candidates.ToArray())
            {
                bool unsafeMove = false;
                foreach (int index in move.Sprite.PixelIndices)
                {
                    int x = index % surface.Width + move.DeltaX;
                    int y = index / surface.Width + move.DeltaY;
                    int destinationIndex = y * surface.Width + x;
                    if (ColorBgra.A(surface.Pixels[destinationIndex]) > 0 &&
                        !sourcePixels.Contains(destinationIndex))
                    {
                        unsafeMove = true;
                        break;
                    }
                }
                if (!unsafeMove) continue;
                candidates.Remove(move);
                skipped.Add(move.Sprite);
                changed = true;
            }
        } while (changed);

        RectInt affected = RectInt.Empty;
        foreach (var move in candidates)
            affected = affected.Union(move.Sprite.Bounds).Union(move.DestinationBounds);
        return new SpriteAlignmentPlan(candidates, skipped.ToArray(), affected);
    }

    public static void ApplyAlignment(Surface surface, SpriteAlignmentPlan plan)
    {
        if (plan.Moves.Count == 0) return;
        var sourceColors = new Dictionary<int, uint>();
        foreach (var move in plan.Moves)
            foreach (int index in move.Sprite.PixelIndices)
                sourceColors[index] = surface.Pixels[index];

        foreach (int index in sourceColors.Keys)
            surface.Pixels[index] = 0;
        foreach (var move in plan.Moves)
        {
            foreach (int sourceIndex in move.Sprite.PixelIndices)
            {
                int x = sourceIndex % surface.Width + move.DeltaX;
                int y = sourceIndex / surface.Width + move.DeltaY;
                surface[x, y] = sourceColors[sourceIndex];
            }
        }
    }

    public static SpriteGridFit FindBestOrigin(
        IReadOnlyList<DetectedSprite> sprites, SpriteGridLayout layout)
    {
        if (!layout.IsValid || sprites.Count == 0)
            return new SpriteGridFit(layout, double.NegativeInfinity, 0, sprites.Count, 0);

        int bestX = FindBestAxisOrigin(sprites, layout.CellWidth, layout.PitchX, horizontal: true);
        int bestY = FindBestAxisOrigin(sprites, layout.CellHeight, layout.PitchY, horizontal: false);
        var fitted = layout with { OriginX = bestX, OriginY = bestY };
        return ScoreFit(sprites, fitted);
    }

    public static SpriteGridInference InferGrid(
        IReadOnlyList<DetectedSprite> sprites, RectInt evidenceRegion)
    {
        if (sprites.Count == 0)
        {
            var fallback = new SpriteGridLayout(32, 32);
            return new SpriteGridInference(fallback,
                new SpriteGridFit(fallback, double.NegativeInfinity, 0, 0, 0), 0);
        }

        int maxWidth = sprites.Max(s => s.Bounds.Width);
        int maxHeight = sprites.Max(s => s.Bounds.Height);
        int pitchX = InferPitch(sprites, evidenceRegion.Width, horizontal: true, maxWidth);
        int pitchY = InferPitch(sprites, evidenceRegion.Height, horizontal: false, maxHeight);

        if (sprites.Count > 1)
        {
            if (pitchX <= 0 && pitchY > 0) pitchX = pitchY;
            if (pitchY <= 0 && pitchX > 0) pitchY = pitchX;
        }
        pitchX = Math.Max(maxWidth, pitchX > 0 ? pitchX : NextStandard(maxWidth));
        pitchY = Math.Max(maxHeight, pitchY > 0 ? pitchY : NextStandard(maxHeight));

        var (cellWidth, spacingX) = SplitPitch(pitchX, maxWidth);
        var (cellHeight, spacingY) = SplitPitch(pitchY, maxHeight);
        var initial = new SpriteGridLayout(cellWidth, cellHeight, 0, 0, spacingX, spacingY);
        var fit = FindBestOrigin(sprites, initial);
        double confidence = sprites.Count == 1
            ? 0.25
            : Math.Clamp((double)fit.FullyContainedSprites / sprites.Count, 0, 1);
        return new SpriteGridInference(fit.Layout, fit, confidence);
    }

    public static SpriteGridFit ScoreFit(
        IReadOnlyList<DetectedSprite> sprites, SpriteGridLayout layout)
    {
        int contained = 0, crossed = 0;
        double centerError = 0, score = 0;
        foreach (var sprite in sprites)
        {
            var cell = layout.NearestCell(sprite.CentroidX, sprite.CentroidY);
            int overflow = Math.Max(0, cell.Bounds.Left - sprite.Bounds.Left) +
                           Math.Max(0, sprite.Bounds.Right - cell.Bounds.Right) +
                           Math.Max(0, cell.Bounds.Top - sprite.Bounds.Top) +
                           Math.Max(0, sprite.Bounds.Bottom - cell.Bounds.Bottom);
            double error = CenterDistance(sprite, cell.Bounds);
            centerError += error;
            if (overflow == 0)
            {
                contained++;
                score += 10_000;
            }
            else
            {
                crossed++;
                score -= overflow * 500;
            }
            score -= error * 8;
        }
        return new SpriteGridFit(layout, score, contained, crossed,
            sprites.Count == 0 ? 0 : centerError / sprites.Count);
    }

    private static int FindBestAxisOrigin(
        IReadOnlyList<DetectedSprite> sprites, int cellSize, int pitch, bool horizontal)
    {
        int bestOrigin = 0;
        double bestScore = double.NegativeInfinity;
        for (int origin = 0; origin < pitch; origin++)
        {
            double score = 0;
            foreach (var sprite in sprites)
            {
                double center = horizontal
                    ? (sprite.Bounds.Left + sprite.Bounds.Right) / 2.0
                    : (sprite.Bounds.Top + sprite.Bounds.Bottom) / 2.0;
                int start = origin + SpriteGridLayout.FloorDiv((int)Math.Floor(center) - origin, pitch) * pitch;
                int min = horizontal ? sprite.Bounds.Left : sprite.Bounds.Top;
                int max = horizontal ? sprite.Bounds.Right : sprite.Bounds.Bottom;
                int overflow = Math.Max(0, start - min) + Math.Max(0, max - (start + cellSize));
                double centerError = Math.Abs(center - (start + cellSize / 2.0));
                score += overflow == 0 ? 10_000 : -overflow * 500;
                score -= centerError * 8;
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestOrigin = origin;
            }
        }
        return bestOrigin;
    }

    private static int InferPitch(
        IReadOnlyList<DetectedSprite> sprites, int evidenceSpan, bool horizontal, int maximumExtent)
    {
        if (sprites.Count < 2) return 0;
        var positions = sprites
            .Select(s => horizontal ? s.CentroidX : s.CentroidY)
            .OrderBy(v => v)
            .ToArray();
        int upper = Math.Min(Math.Max(maximumExtent, evidenceSpan), 1024);
        int lower = Math.Max(2, maximumExtent);
        int bestPitch = 0;
        double bestScore = 0;
        for (int pitch = lower; pitch <= upper; pitch++)
        {
            double score = 0;
            int evidence = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                for (int j = i + 1; j < positions.Length; j++)
                {
                    double difference = positions[j] - positions[i];
                    if (difference < pitch * 0.65) continue;
                    int multiple = Math.Max(1, (int)Math.Round(difference / pitch));
                    double residual = Math.Abs(difference - multiple * pitch);
                    if (residual > Math.Max(2, pitch * 0.08)) continue;
                    score += 1.0 / (1.0 + residual) / multiple;
                    evidence++;
                }
            }
            if (evidence == 0) continue;
            score -= pitch * 0.0001;
            if (score > bestScore)
            {
                bestScore = score;
                bestPitch = pitch;
            }
        }
        return bestPitch;
    }

    private static (int CellSize, int Spacing) SplitPitch(int pitch, int maximumExtent)
    {
        int maximumLikelySpacing = Math.Max(4, pitch / 10);
        int standard = StandardCellSizes
            .Where(size => size >= maximumExtent && size <= pitch && pitch - size <= maximumLikelySpacing)
            .DefaultIfEmpty(pitch)
            .Max();
        return (standard, pitch - standard);
    }

    private static int NextStandard(int extent) =>
        StandardCellSizes.FirstOrDefault(size => size >= extent, Math.Max(1, extent));

    private static double CenterDistance(DetectedSprite sprite, RectInt cell)
    {
        double spriteCenterX = (sprite.Bounds.Left + sprite.Bounds.Right) / 2.0;
        double spriteCenterY = (sprite.Bounds.Top + sprite.Bounds.Bottom) / 2.0;
        double cellCenterX = cell.Left + cell.Width / 2.0;
        double cellCenterY = cell.Top + cell.Height / 2.0;
        return Math.Abs(spriteCenterX - cellCenterX) + Math.Abs(spriteCenterY - cellCenterY);
    }
}
