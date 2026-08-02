using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.Core.Drawing;

/// <summary>
/// Builds coverage masks for vector shapes (line, curve, rectangle, rounded
/// rectangle, ellipse, freeform) that the shape tools stamp into a
/// <see cref="StrokeBuffer"/>. Outlines are stroked by stamping round dabs
/// along the path; fills use the shared polygon rasterizer.
/// </summary>
public static class ShapeRenderer
{
    public static List<(double X, double Y)> RectanglePath(double x0, double y0, double x1, double y1)
    {
        double l = Math.Min(x0, x1), t = Math.Min(y0, y1);
        double r = Math.Max(x0, x1), b = Math.Max(y0, y1);
        return new List<(double, double)> { (l, t), (r, t), (r, b), (l, b) };
    }

    public static List<(double X, double Y)> RoundedRectanglePath(double x0, double y0, double x1, double y1, double radius)
    {
        double l = Math.Min(x0, x1), t = Math.Min(y0, y1);
        double r = Math.Max(x0, x1), b = Math.Max(y0, y1);
        radius = Math.Min(radius, Math.Min((r - l) / 2, (b - t) / 2));
        if (radius <= 0.01) return RectanglePath(x0, y0, x1, y1);

        var pts = new List<(double, double)>();
        const int segs = 16;
        void Arc(double cx, double cy, double startDeg)
        {
            for (int i = 0; i <= segs; i++)
            {
                double a = (startDeg + 90.0 * i / segs) * Math.PI / 180.0;
                pts.Add((cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
            }
        }
        Arc(l + radius, t + radius, 180);
        Arc(r - radius, t + radius, 270);
        Arc(r - radius, b - radius, 0);
        Arc(l + radius, b - radius, 90);
        return pts;
    }

    public static List<(double X, double Y)> EllipsePath(double x0, double y0, double x1, double y1)
    {
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        double rx = Math.Abs(x1 - x0) / 2, ry = Math.Abs(y1 - y0) / 2;
        const int segs = 128;
        var pts = new List<(double, double)>(segs);
        for (int i = 0; i < segs; i++)
        {
            double a = i * Math.PI * 2 / segs;
            pts.Add((cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }
        return pts;
    }

    /// <summary>Samples a Catmull-Rom spline through the given control points.</summary>
    public static List<(double X, double Y)> CurvePath(IReadOnlyList<(double X, double Y)> controls, int samplesPerSegment = 24)
    {
        if (controls.Count < 2) return controls.ToList();
        var pts = new List<(double, double)>();
        for (int i = 0; i < controls.Count - 1; i++)
        {
            var p0 = controls[Math.Max(0, i - 1)];
            var p1 = controls[i];
            var p2 = controls[i + 1];
            var p3 = controls[Math.Min(controls.Count - 1, i + 2)];
            for (int s = 0; s < samplesPerSegment; s++)
            {
                double t = (double)s / samplesPerSegment;
                double t2 = t * t, t3 = t2 * t;
                double x = 0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                double y = 0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                pts.Add((x, y));
            }
        }
        pts.Add(controls[^1]);
        return pts;
    }

    /// <summary>Strokes an open or closed path into the buffer with the given width.</summary>
    public static void StrokePath(StrokeBuffer buffer, IReadOnlyList<(double X, double Y)> path, double width, bool closed, bool antialias)
    {
        if (path.Count == 0) return;
        double radius = Math.Max(0.5, width / 2);
        if (path.Count == 1)
        {
            buffer.StampDab(path[0].X, path[0].Y, radius, 1.0, antialias);
            return;
        }
        for (int i = 0; i < path.Count - 1; i++)
            buffer.StampLine(path[i].X, path[i].Y, path[i + 1].X, path[i + 1].Y, radius, 1.0, antialias);
        if (closed)
            buffer.StampLine(path[^1].X, path[^1].Y, path[0].X, path[0].Y, radius, 1.0, antialias);
    }

    /// <summary>Fills a closed path into the buffer using the polygon rasterizer.</summary>
    public static void FillPath(StrokeBuffer buffer, IReadOnlyList<(double X, double Y)> path, bool antialias)
    {
        if (path.Count < 3) return;
        var mask = SelectionRasterizer.RasterizePolygon(buffer.Width, buffer.Height, path);
        if (!antialias)
        {
            for (int i = 0; i < mask.Length; i++)
                mask[i] = mask[i] >= 128 ? (byte)255 : (byte)0;
        }
        double minX = path.Min(p => p.X), minY = path.Min(p => p.Y);
        double maxX = path.Max(p => p.X), maxY = path.Max(p => p.Y);
        var dirty = RectInt.FromLTRB(
            Math.Max(0, (int)minX - 1), Math.Max(0, (int)minY - 1),
            Math.Min(buffer.Width, (int)maxX + 2), Math.Min(buffer.Height, (int)maxY + 2));
        buffer.AddMask(mask, dirty);
    }
}
