using Artista.Core.Documents;
using Artista.Core.Imaging;
using Artista.Core.Layers;

namespace Artista.Core.Rendering;

/// <summary>
/// Composites document layers into a destination surface. Supports substituting
/// one layer's surface (used for live effect previews and tool previews without
/// mutating the real layer).
/// </summary>
public static class Compositor
{
    /// <summary>
    /// Composites the visible layers of <paramref name="doc"/> into
    /// <paramref name="dst"/> over transparency, restricted to <paramref name="roi"/>.
    /// </summary>
    /// <param name="substituteLayerId">If a layer with this id exists, its surface is replaced by <paramref name="substituteSurface"/> during compositing.</param>
    public static void Composite(
        Document doc, Surface dst, RectInt roi,
        int substituteLayerId = -1, Surface? substituteSurface = null)
    {
        var r = roi.Intersect(dst.Bounds);
        if (r.IsEmpty) return;

        // Snapshot layer list to be robust against concurrent UI changes.
        var layers = doc.Layers.ToArray();

        Parallel.For(r.Top, r.Bottom, y =>
        {
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            dstRow.Clear();
            foreach (var layer in layers)
            {
                if (!layer.Visible || layer.Opacity == 0)
                    continue;
                var surface = layer.Id == substituteLayerId && substituteSurface != null
                    ? substituteSurface
                    : layer.Surface;
                if (y >= surface.Height) continue;
                int w = Math.Min(r.Width, surface.Width - r.Left);
                if (w <= 0) continue;
                var srcRow = surface.GetRowSpan(y, r.Left, w);
                var mode = layer.BlendMode;
                int opacity = layer.Opacity;

                if (mode == BlendMode.Normal && opacity == 255)
                {
                    for (int x = 0; x < w; x++)
                    {
                        uint s = srcRow[x];
                        uint a = s >> 24;
                        if (a == 0) continue;
                        dstRow[x] = a == 255 ? s : ColorBgra.Over(dstRow[x], s);
                    }
                }
                else
                {
                    for (int x = 0; x < w; x++)
                        dstRow[x] = BlendModeOps.Composite(mode, dstRow[x], srcRow[x], opacity);
                }
            }
        });
    }
}
