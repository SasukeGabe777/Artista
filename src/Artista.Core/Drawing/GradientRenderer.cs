using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.Core.Drawing;

public enum GradientShape
{
    Linear,
    Radial,
}

/// <summary>Renders linear and radial two-color gradients into a layer surface.</summary>
public static class GradientRenderer
{
    /// <summary>
    /// Renders the gradient from <paramref name="startColor"/> at (x0,y0) to
    /// <paramref name="endColor"/> at (x1,y1) into <paramref name="target"/>,
    /// compositing over <paramref name="original"/> and respecting selection.
    /// </summary>
    public static void Render(
        Surface target, Surface original, Selection selection, RectInt rect,
        GradientShape shape, double x0, double y0, double x1, double y1,
        uint startColor, uint endColor)
    {
        var r = rect.Intersect(target.Bounds);
        if (r.IsEmpty) return;
        double dx = x1 - x0, dy = y1 - y0;
        double lenSq = dx * dx + dy * dy;
        bool degenerate = lenSq < 0.0001;
        double len = Math.Sqrt(lenSq);
        bool hasSelection = !selection.IsEmpty;

        Parallel.For(r.Top, r.Bottom, y =>
        {
            var origRow = original.GetRowSpan(y, r.Left, r.Width);
            var dstRow = target.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < r.Width; x++)
            {
                double t;
                if (degenerate)
                    t = 0;
                else if (shape == GradientShape.Linear)
                    t = ((r.Left + x + 0.5 - x0) * dx + (y + 0.5 - y0) * dy) / lenSq;
                else
                {
                    double px = r.Left + x + 0.5 - x0, py = y + 0.5 - y0;
                    t = Math.Sqrt(px * px + py * py) / len;
                }
                t = Math.Clamp(t, 0, 1);
                uint grad = ColorBgra.Lerp(startColor, endColor, (float)t);
                uint blended = ColorBgra.Over(origRow[x], grad);
                if (hasSelection)
                {
                    byte sel = selection.MaskAt(r.Left + x, y);
                    blended = sel switch
                    {
                        0 => origRow[x],
                        255 => blended,
                        _ => ColorBgra.Lerp(origRow[x], blended, sel / 255f),
                    };
                }
                dstRow[x] = blended;
            }
        });
    }
}
