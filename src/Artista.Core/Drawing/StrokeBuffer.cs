using Artista.Core.ColorEngine;
using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.Core.Drawing;

/// <summary>
/// Accumulates per-pixel coverage (0-255) for one continuous stroke.
///
/// Dabs are combined with max() so overlapping stamps within a stroke never
/// exceed the stroke opacity (Paint.NET behavior: one stroke = uniform paint).
/// The stroke is applied by blending between a snapshot of the layer taken at
/// stroke start and the stroke result, which also makes the whole stroke a
/// single history delta.
/// </summary>
public sealed class StrokeBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Coverage { get; }
    public RectInt DirtyRect { get; private set; } = RectInt.Empty;

    public StrokeBuffer(int width, int height)
    {
        Width = width;
        Height = height;
        Coverage = new byte[(long)width * height];
    }

    public void Clear()
    {
        if (DirtyRect.IsEmpty) return;
        var r = DirtyRect.Intersect(new RectInt(0, 0, Width, Height));
        for (int y = r.Top; y < r.Bottom; y++)
            Array.Clear(Coverage, y * Width + r.Left, r.Width);
        DirtyRect = RectInt.Empty;
    }

    /// <summary>
    /// Stamps an antialiased round dab. Hardness 0-1: fraction of the radius
    /// that is fully opaque before falloff begins.
    /// </summary>
    public void StampDab(double cx, double cy, double radius, double hardness, bool antialias = true)
    {
        radius = Math.Max(0.5, radius);
        hardness = Math.Clamp(hardness, 0, 1);
        int x0 = Math.Max(0, (int)Math.Floor(cx - radius - 1));
        int y0 = Math.Max(0, (int)Math.Floor(cy - radius - 1));
        int x1 = Math.Min(Width - 1, (int)Math.Ceiling(cx + radius + 1));
        int y1 = Math.Min(Height - 1, (int)Math.Ceiling(cy + radius + 1));
        if (x1 < x0 || y1 < y0) return;

        double hardRadius = radius * hardness;
        double falloff = Math.Max(0.75, radius - hardRadius); // AA band at minimum

        for (int y = y0; y <= y1; y++)
        {
            int row = y * Width;
            for (int x = x0; x <= x1; x++)
            {
                double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double cov;
                if (dist <= hardRadius) cov = 1.0;
                else if (dist >= radius) cov = 0.0;
                else cov = 1.0 - (dist - hardRadius) / falloff;
                if (cov <= 0) continue;
                if (!antialias)
                    cov = cov >= 0.5 ? 1.0 : 0.0;
                byte v = (byte)Math.Clamp(cov * 255.0 + 0.5, 0, 255);
                if (v > Coverage[row + x])
                    Coverage[row + x] = v;
            }
        }
        DirtyRect = DirtyRect.Union(RectInt.FromLTRB(x0, y0, x1 + 1, y1 + 1));
    }

    /// <summary>Stamps dabs along a line segment with spacing proportional to the radius.</summary>
    public void StampLine(double x0, double y0, double x1, double y1, double radius, double hardness, bool antialias = true)
    {
        double dist = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        double spacing = Math.Max(0.5, radius * 0.15);
        int steps = Math.Max(1, (int)Math.Ceiling(dist / spacing));
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            StampDab(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, radius, hardness, antialias);
        }
    }

    /// <summary>Adds an arbitrary coverage mask (e.g. a rasterized shape) into the buffer.</summary>
    public void AddMask(byte[] mask, RectInt dirty)
    {
        var r = dirty.Intersect(new RectInt(0, 0, Width, Height));
        for (int y = r.Top; y < r.Bottom; y++)
        {
            int row = y * Width;
            for (int x = r.Left; x < r.Right; x++)
            {
                if (mask[row + x] > Coverage[row + x])
                    Coverage[row + x] = mask[row + x];
            }
        }
        DirtyRect = DirtyRect.Union(r);
    }

    // ---- Appliers: blend from the stroke-start snapshot into the live surface ----

    public delegate uint PixelBlend(uint original, float strength);

    /// <summary>
    /// Core applier: for each covered pixel, target = blend(originalSnapshot
    /// pixel, coverage * opacity * selectionCoverage).
    /// </summary>
    public void Apply(Surface target, Surface original, Selection selection, RectInt rect, float opacity, PixelBlend blend)
    {
        var r = rect.Intersect(DirtyRect).Intersect(target.Bounds);
        if (r.IsEmpty) return;
        bool hasSelection = !selection.IsEmpty;
        Parallel.For(r.Top, r.Bottom, y =>
        {
            int row = y * Width;
            var origRow = original.GetRowSpan(y, r.Left, r.Width);
            var dstRow = target.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < r.Width; x++)
            {
                byte cov = Coverage[row + r.Left + x];
                if (cov == 0)
                {
                    dstRow[x] = origRow[x];
                    continue;
                }
                float strength = cov / 255f * opacity;
                if (hasSelection)
                {
                    byte sel = selection.MaskAt(r.Left + x, y);
                    if (sel == 0)
                    {
                        dstRow[x] = origRow[x];
                        continue;
                    }
                    strength *= sel / 255f;
                }
                dstRow[x] = blend(origRow[x], strength);
            }
        });
    }

    public void ApplyPaint(Surface target, Surface original, Selection selection, RectInt rect, uint color, float opacity, bool alphaLock)
    {
        Apply(target, original, selection, rect, opacity, (orig, strength) =>
        {
            if (alphaLock)
            {
                // Paint color but keep the original alpha exactly.
                uint painted = ColorBgra.Lerp(orig, ColorBgra.WithAlpha(color, ColorBgra.A(orig)), strength);
                return ColorBgra.WithAlpha(painted, ColorBgra.A(orig));
            }
            return ColorBgra.Over(orig, color, (int)(strength * 255 + 0.5f));
        });
    }

    public void ApplyErase(Surface target, Surface original, Selection selection, RectInt rect, float opacity)
    {
        Apply(target, original, selection, rect, opacity, (orig, strength) =>
        {
            byte a = ColorBgra.A(orig);
            if (a == 0) return orig;
            return ColorBgra.WithAlpha(orig, (byte)Math.Clamp(a * (1f - strength) + 0.5f, 0, 255));
        });
    }

    /// <summary>The Color Remover brush: erase only pixels matching the target color.</summary>
    public void ApplyColorRemove(Surface target, Surface original, Selection selection, RectInt rect, ColorMatcher matcher, float strength)
    {
        Apply(target, original, selection, rect, strength, (orig, s) =>
        {
            byte a = ColorBgra.A(orig);
            if (a == 0) return orig;
            float f = matcher.Match(orig) * s;
            if (f <= 0f) return orig;
            return ColorBgra.WithAlpha(orig, (byte)Math.Clamp(a * (1f - f) + 0.5f, 0, 255));
        });
    }

    /// <summary>Recolor brush: repaint pixels matching <paramref name="matcher"/> with <paramref name="paintColor"/>.</summary>
    public void ApplyRecolor(Surface target, Surface original, Selection selection, RectInt rect, ColorMatcher matcher, uint paintColor, float opacity)
    {
        Apply(target, original, selection, rect, opacity, (orig, s) =>
        {
            float f = matcher.Match(orig) * s;
            if (f <= 0f) return orig;
            uint painted = ColorBgra.WithAlpha(paintColor, ColorBgra.A(orig));
            return ColorBgra.Lerp(orig, painted, f);
        });
    }

    /// <summary>Clone stamp: copies pixels from the snapshot at a fixed offset.</summary>
    public void ApplyClone(Surface target, Surface original, Selection selection, RectInt rect, int offsetX, int offsetY, float opacity)
    {
        var r = rect.Intersect(DirtyRect).Intersect(target.Bounds);
        if (r.IsEmpty) return;
        bool hasSelection = !selection.IsEmpty;
        Parallel.For(r.Top, r.Bottom, y =>
        {
            int row = y * Width;
            var origRow = original.GetRowSpan(y, r.Left, r.Width);
            var dstRow = target.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < r.Width; x++)
            {
                byte cov = Coverage[row + r.Left + x];
                if (cov == 0)
                {
                    dstRow[x] = origRow[x];
                    continue;
                }
                int sx = r.Left + x + offsetX, sy = y + offsetY;
                if (sx < 0 || sy < 0 || sx >= original.Width || sy >= original.Height)
                {
                    dstRow[x] = origRow[x];
                    continue;
                }
                float strength = cov / 255f * opacity;
                if (hasSelection)
                    strength *= selection.MaskAt(r.Left + x, y) / 255f;
                dstRow[x] = ColorBgra.Lerp(origRow[x], original[sx, sy], strength);
            }
        });
    }
}
