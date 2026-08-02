using Artista.Core.Imaging;

namespace Artista.Core.Selections;

/// <summary>
/// Scanline flood fill used by the Magic Wand and Paint Bucket. Tolerance is
/// 0-100; matching uses a summed per-channel distance like Paint.NET so results
/// feel familiar.
/// </summary>
public static class FloodFill
{
    /// <summary>
    /// Computes the set of pixels matching the color at (startX, startY).
    /// Returns a 0/255 mask. When <paramref name="contiguous"/> is false, all
    /// matching pixels in <paramref name="roi"/> are selected (global mode).
    /// </summary>
    public static byte[] ComputeMask(Surface surface, int startX, int startY, double tolerance, bool contiguous, RectInt? roiOpt = null)
    {
        int w = surface.Width, h = surface.Height;
        var mask = new byte[(long)w * h];
        if (startX < 0 || startY < 0 || startX >= w || startY >= h)
            return mask;

        uint target = surface[startX, startY];
        // Threshold on summed channel distance (incl. alpha); quadratic response
        // gives finer control at low tolerances, 100% matches everything.
        int threshold = (int)((tolerance / 100.0) * (tolerance / 100.0) * 1020.0);

        var roi = (roiOpt ?? surface.Bounds).Intersect(surface.Bounds);

        if (!contiguous)
        {
            Parallel.For(roi.Top, roi.Bottom, y =>
            {
                var row = surface.GetRow(y);
                int rowOff = y * w;
                for (int x = roi.Left; x < roi.Right; x++)
                {
                    if (Matches(row[x], target, threshold))
                        mask[rowOff + x] = 255;
                }
            });
            return mask;
        }

        if (!roi.Contains(startX, startY))
            return mask;

        var stack = new Stack<(int X, int Y)>();
        stack.Push((startX, startY));
        while (stack.Count > 0)
        {
            var (px, py) = stack.Pop();
            int rowOff = py * w;
            if (mask[rowOff + px] != 0 || !Matches(surface[px, py], target, threshold))
                continue;

            // Expand to full matching span on this row.
            int left = px;
            while (left - 1 >= roi.Left && mask[rowOff + left - 1] == 0 && Matches(surface[left - 1, py], target, threshold))
                left--;
            int right = px;
            while (right + 1 < roi.Right && mask[rowOff + right + 1] == 0 && Matches(surface[right + 1, py], target, threshold))
                right++;
            for (int x = left; x <= right; x++)
                mask[rowOff + x] = 255;

            for (int x = left; x <= right; x++)
            {
                if (py - 1 >= roi.Top && mask[(py - 1) * w + x] == 0 && Matches(surface[x, py - 1], target, threshold))
                    stack.Push((x, py - 1));
                if (py + 1 < roi.Bottom && mask[(py + 1) * w + x] == 0 && Matches(surface[x, py + 1], target, threshold))
                    stack.Push((x, py + 1));
            }
        }
        return mask;
    }

    private static bool Matches(uint c, uint target, int threshold)
    {
        if (c == target) return true;
        int db = ColorBgra.B(c) - ColorBgra.B(target);
        int dg = ColorBgra.G(c) - ColorBgra.G(target);
        int dr = ColorBgra.R(c) - ColorBgra.R(target);
        int da = ColorBgra.A(c) - ColorBgra.A(target);
        return Math.Abs(db) + Math.Abs(dg) + Math.Abs(dr) + Math.Abs(da) <= threshold;
    }
}
