using Artista.Core.Imaging;

namespace Artista.Core.Selections;

/// <summary>
/// Rasterizes selection shapes (rectangle, ellipse, polygon) into coverage
/// masks with 4x vertical supersampling and fractional horizontal coverage,
/// producing antialiased edges.
/// </summary>
public static class SelectionRasterizer
{
    public static byte[] RasterizeRectangle(int width, int height, double x0, double y0, double x1, double y1)
    {
        var pts = new[]
        {
            (Math.Min(x0, x1), Math.Min(y0, y1)),
            (Math.Max(x0, x1), Math.Min(y0, y1)),
            (Math.Max(x0, x1), Math.Max(y0, y1)),
            (Math.Min(x0, x1), Math.Max(y0, y1)),
        };
        return RasterizePolygon(width, height, pts.Select(p => (p.Item1, p.Item2)).ToArray());
    }

    public static byte[] RasterizeEllipse(int width, int height, double x0, double y0, double x1, double y1)
    {
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        double rx = Math.Abs(x1 - x0) / 2, ry = Math.Abs(y1 - y0) / 2;
        const int segments = 256;
        var pts = new (double, double)[segments];
        for (int i = 0; i < segments; i++)
        {
            double t = i * (Math.PI * 2) / segments;
            pts[i] = (cx + rx * Math.Cos(t), cy + ry * Math.Sin(t));
        }
        return RasterizePolygon(width, height, pts);
    }

    /// <summary>
    /// Even-odd scanline polygon fill. Each pixel row is sampled at 4
    /// sub-scanlines; horizontal coverage is fractional at span ends.
    /// </summary>
    public static byte[] RasterizePolygon(int width, int height, IReadOnlyList<(double X, double Y)> points)
    {
        var mask = new byte[(long)width * height];
        if (points.Count < 3) return mask;

        int n = points.Count;
        const int subsamples = 4;
        double subStep = 1.0 / subsamples;

        var coverage = new float[width];
        var crossings = new List<double>(16);

        for (int y = 0; y < height; y++)
        {
            Array.Clear(coverage);
            bool any = false;
            for (int s = 0; s < subsamples; s++)
            {
                double sy = y + (s + 0.5) * subStep;
                crossings.Clear();
                for (int i = 0; i < n; i++)
                {
                    var (ax, ay) = points[i];
                    var (bx, by) = points[(i + 1) % n];
                    if ((ay <= sy && by > sy) || (by <= sy && ay > sy))
                    {
                        double t = (sy - ay) / (by - ay);
                        crossings.Add(ax + t * (bx - ax));
                    }
                }
                if (crossings.Count < 2) continue;
                crossings.Sort();
                for (int i = 0; i + 1 < crossings.Count; i += 2)
                {
                    double sx0 = Math.Max(0, crossings[i]);
                    double sx1 = Math.Min(width, crossings[i + 1]);
                    if (sx1 <= sx0) continue;
                    any = true;
                    int ix0 = (int)Math.Floor(sx0);
                    int ix1 = (int)Math.Ceiling(sx1);
                    for (int x = ix0; x < ix1 && x < width; x++)
                    {
                        double covered = Math.Min(x + 1.0, sx1) - Math.Max(x, sx0);
                        if (covered > 0)
                            coverage[x] += (float)(covered * subStep);
                    }
                }
            }
            if (!any) continue;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (coverage[x] > 0)
                    mask[row + x] = (byte)Math.Clamp(coverage[x] * 255f + 0.5f, 0, 255);
            }
        }
        return mask;
    }
}
