using Artista.Core.Imaging;

namespace Artista.Core.Selections;

public sealed record SpriteDetectionOptions(
    byte AlphaThreshold = 1,
    int InspectionMargin = 0,
    int ExpectedCellWidth = 0,
    int ExpectedCellHeight = 0,
    int FragmentGap = 0);

/// <summary>A detected visual sprite, potentially made from nearby disconnected components.</summary>
public sealed class DetectedSprite
{
    private readonly int[] _pixelIndices;

    public IReadOnlyList<int> PixelIndices => _pixelIndices;
    public RectInt Bounds { get; }
    public int PixelCount => _pixelIndices.Length;
    public double CentroidX { get; }
    public double CentroidY { get; }
    public int ComponentCount { get; }

    internal DetectedSprite(int[] pixelIndices, RectInt bounds,
        double centroidX, double centroidY, int componentCount)
    {
        _pixelIndices = pixelIndices;
        Bounds = bounds;
        CentroidX = centroidX;
        CentroidY = centroidY;
        ComponentCount = componentCount;
    }
}

/// <summary>
/// Detects sprites from alpha before any grid assignment. Eight-connected
/// opaque regions are found across an expanded selection, then small nearby
/// fragments are conservatively grouped when their combined envelope can fit
/// one expected frame.
/// </summary>
public static class SpriteDetector
{
    public static IReadOnlyList<DetectedSprite> Detect(
        Surface surface, Selection selection, SpriteDetectionOptions? options = null)
    {
        options ??= new SpriteDetectionOptions();
        if (surface.Width != selection.Width || surface.Height != selection.Height)
            throw new ArgumentException("The selection must match the analyzed surface.", nameof(selection));
        if (selection.IsEmpty) return Array.Empty<DetectedSprite>();

        int margin = Math.Max(0, options.InspectionMargin);
        var analysisBounds = selection.Bounds.Inflate(margin).Intersect(surface.Bounds);
        var visited = new bool[(long)analysisBounds.Width * analysisBounds.Height];
        var components = new List<Component>();
        var queue = new Queue<(int X, int Y)>();

        for (int y = analysisBounds.Top; y < analysisBounds.Bottom; y++)
        {
            for (int x = analysisBounds.Left; x < analysisBounds.Right; x++)
            {
                int local = (y - analysisBounds.Top) * analysisBounds.Width + x - analysisBounds.Left;
                if (visited[local] || ColorBgra.A(surface[x, y]) < options.AlphaThreshold)
                    continue;

                var pixels = new List<int>();
                bool touchesSelection = false;
                bool touchesInspectionEdge = false;
                int left = x, right = x, top = y, bottom = y;
                long sumX = 0, sumY = 0;
                visited[local] = true;
                queue.Enqueue((x, y));
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    pixels.Add(cy * surface.Width + cx);
                    sumX += cx;
                    sumY += cy;
                    left = Math.Min(left, cx); right = Math.Max(right, cx);
                    top = Math.Min(top, cy); bottom = Math.Max(bottom, cy);
                    touchesSelection |= selection.MaskAt(cx, cy) > 0;
                    touchesInspectionEdge |=
                        (cx == analysisBounds.Left && analysisBounds.Left > 0) ||
                        (cx == analysisBounds.Right - 1 && analysisBounds.Right < surface.Width) ||
                        (cy == analysisBounds.Top && analysisBounds.Top > 0) ||
                        (cy == analysisBounds.Bottom - 1 && analysisBounds.Bottom < surface.Height);

                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            int nx = cx + ox, ny = cy + oy;
                            if (!analysisBounds.Contains(nx, ny)) continue;
                            int neighbor = (ny - analysisBounds.Top) * analysisBounds.Width + nx - analysisBounds.Left;
                            if (visited[neighbor] || ColorBgra.A(surface[nx, ny]) < options.AlphaThreshold)
                                continue;
                            visited[neighbor] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }

                components.Add(new Component(
                    pixels,
                    RectInt.FromLTRB(left, top, right + 1, bottom + 1),
                    sumX,
                    sumY,
                    touchesSelection,
                    touchesInspectionEdge));
            }
        }

        if (components.Count == 0) return Array.Empty<DetectedSprite>();
        return GroupComponents(components, surface.Width, options);
    }

    private static IReadOnlyList<DetectedSprite> GroupComponents(
        IReadOnlyList<Component> components, int surfaceWidth, SpriteDetectionOptions options)
    {
        int referenceWidth = options.ExpectedCellWidth > 0
            ? options.ExpectedCellWidth
            : Math.Max(4, Percentile(components.Select(c => c.Bounds.Width), 0.75));
        int referenceHeight = options.ExpectedCellHeight > 0
            ? options.ExpectedCellHeight
            : Math.Max(4, Percentile(components.Select(c => c.Bounds.Height), 0.75));
        int gapLimit = options.FragmentGap > 0
            ? options.FragmentGap
            : Math.Max(2, Math.Min(referenceWidth, referenceHeight) / 8);

        var parent = Enumerable.Range(0, components.Count).ToArray();
        for (int i = 0; i < components.Count; i++)
        {
            for (int j = i + 1; j < components.Count; j++)
            {
                var a = components[i];
                var b = components[j];
                int gapX = AxisGap(a.Bounds.Left, a.Bounds.Right, b.Bounds.Left, b.Bounds.Right);
                int gapY = AxisGap(a.Bounds.Top, a.Bounds.Bottom, b.Bounds.Top, b.Bounds.Bottom);
                double distance = Math.Sqrt((double)gapX * gapX + (double)gapY * gapY);
                if (distance > gapLimit) continue;

                var combined = a.Bounds.Union(b.Bounds);
                int widthLimit = options.ExpectedCellWidth > 0
                    ? options.ExpectedCellWidth
                    : (int)Math.Ceiling(referenceWidth * 1.35);
                int heightLimit = options.ExpectedCellHeight > 0
                    ? options.ExpectedCellHeight
                    : (int)Math.Ceiling(referenceHeight * 1.35);
                if (combined.Width > widthLimit || combined.Height > heightLimit) continue;

                int smaller = Math.Min(a.Pixels.Count, b.Pixels.Count);
                int larger = Math.Max(a.Pixels.Count, b.Pixels.Count);
                bool likelySatellite = smaller <= Math.Max(12, (int)(larger * 0.45));
                bool virtuallyConnected = gapX <= 1 && gapY <= 1;
                if (likelySatellite || virtuallyConnected)
                    Union(parent, i, j);
            }
        }

        var grouped = new Dictionary<int, List<Component>>();
        for (int i = 0; i < components.Count; i++)
        {
            int root = Find(parent, i);
            if (!grouped.TryGetValue(root, out var list))
                grouped[root] = list = new List<Component>();
            list.Add(components[i]);
        }

        var sprites = new List<DetectedSprite>();
        foreach (var group in grouped.Values)
        {
            if (!group.Any(c => c.TouchesSelection)) continue;
            // An opaque component reaching an internal inspection edge may
            // continue beyond what was analyzed. Skipping it is safer than
            // moving a truncated sprite and leaving pixels behind.
            if (group.Any(c => c.TouchesInspectionEdge)) continue;
            var pixels = group.SelectMany(c => c.Pixels).Distinct().OrderBy(i => i).ToArray();
            if (pixels.Length == 0) continue;
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            long sumX = 0, sumY = 0;
            foreach (int index in pixels)
            {
                int x = index % surfaceWidth, y = index / surfaceWidth;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                sumX += x; sumY += y;
            }
            sprites.Add(new DetectedSprite(
                pixels,
                RectInt.FromLTRB(left, top, right + 1, bottom + 1),
                (double)sumX / pixels.Length,
                (double)sumY / pixels.Length,
                group.Count));
        }

        return sprites.OrderBy(s => s.Bounds.Top).ThenBy(s => s.Bounds.Left).ToArray();
    }

    private static int AxisGap(int a0, int a1, int b0, int b1) =>
        a1 < b0 ? b0 - a1 : b1 < a0 ? a0 - b1 : 0;

    private static int Percentile(IEnumerable<int> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return 0;
        int index = (int)Math.Round((sorted.Length - 1) * percentile);
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static int Find(int[] parent, int value)
    {
        while (parent[value] != value)
        {
            parent[value] = parent[parent[value]];
            value = parent[value];
        }
        return value;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int rootA = Find(parent, a), rootB = Find(parent, b);
        if (rootA != rootB) parent[rootB] = rootA;
    }

    private sealed record Component(
        List<int> Pixels,
        RectInt Bounds,
        long SumX,
        long SumY,
        bool TouchesSelection,
        bool TouchesInspectionEdge);
}
