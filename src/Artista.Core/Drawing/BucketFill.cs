using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.Core.Drawing;

/// <summary>Paint Bucket: flood fill with tolerance, respecting the selection mask.</summary>
public static class BucketFill
{
    /// <summary>
    /// Fills the region around (x, y) with <paramref name="color"/>. Returns the
    /// dirty rect, or Empty if nothing changed.
    /// </summary>
    public static RectInt Fill(
        Surface surface, Selection selection, int x, int y, uint color,
        double tolerance, bool contiguous, bool antialias = true)
    {
        if (x < 0 || y < 0 || x >= surface.Width || y >= surface.Height)
            return RectInt.Empty;
        if (!selection.IsEmpty && selection.MaskAt(x, y) == 0)
            return RectInt.Empty;

        var roi = selection.EffectiveBounds;
        var mask = FloodFill.ComputeMask(surface, x, y, tolerance, contiguous, roi);

        int minX = surface.Width, minY = surface.Height, maxX = -1, maxY = -1;
        bool hasSelection = !selection.IsEmpty;
        for (int py = roi.Top; py < roi.Bottom; py++)
        {
            int row = py * surface.Width;
            var surfRow = surface.GetRow(py);
            for (int px = roi.Left; px < roi.Right; px++)
            {
                byte cov = mask[row + px];
                if (cov == 0) continue;
                if (hasSelection)
                {
                    byte sel = selection.MaskAt(px, py);
                    if (sel == 0) continue;
                    cov = (byte)(cov * sel / 255);
                    if (cov == 0) continue;
                }
                surfRow[px] = cov == 255
                    ? ColorBgra.Over(surfRow[px], color)
                    : ColorBgra.Lerp(surfRow[px], ColorBgra.Over(surfRow[px], color), cov / 255f);
                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
                if (py < minY) minY = py;
                if (py > maxY) maxY = py;
            }
        }
        return maxX < 0 ? RectInt.Empty : RectInt.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }
}
