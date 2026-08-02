using Artista.Core.Imaging;
using Artista.Core.Layers;

namespace Artista.Core.Documents;

public enum AnchorPosition
{
    TopLeft, TopCenter, TopRight,
    MiddleLeft, MiddleCenter, MiddleRight,
    BottomLeft, BottomCenter, BottomRight,
}

/// <summary>Whole-document geometric operations. Callers wrap these in history mementos.</summary>
public static class DocumentTransforms
{
    /// <summary>Resamples every layer to a new size.</summary>
    public static void ResizeImage(Document doc, int newWidth, int newHeight, ResampleMode mode)
    {
        doc.SetCanvas(newWidth, newHeight, layer => layer.Surface.Resized(newWidth, newHeight, mode));
    }

    /// <summary>Changes the canvas size, anchoring existing content.</summary>
    public static void ResizeCanvas(Document doc, int newWidth, int newHeight, AnchorPosition anchor)
    {
        var (ox, oy) = AnchorOffset(anchor, doc.Width, doc.Height, newWidth, newHeight);
        doc.SetCanvas(newWidth, newHeight, layer =>
        {
            var s = new Surface(newWidth, newHeight);
            var srcRect = new RectInt(Math.Max(0, -ox), Math.Max(0, -oy),
                Math.Min(layer.Surface.Width, newWidth - Math.Max(0, ox)) - Math.Max(0, -ox),
                Math.Min(layer.Surface.Height, newHeight - Math.Max(0, oy)) - Math.Max(0, -oy));
            if (srcRect.Width > 0 && srcRect.Height > 0)
            {
                for (int y = 0; y < srcRect.Height; y++)
                {
                    var src = layer.Surface.GetRowSpan(srcRect.Top + y, srcRect.Left, srcRect.Width);
                    src.CopyTo(s.GetRowSpan(srcRect.Top + y + oy, srcRect.Left + ox, srcRect.Width));
                }
            }
            return s;
        });
    }

    private static (int X, int Y) AnchorOffset(AnchorPosition anchor, int oldW, int oldH, int newW, int newH)
    {
        int dx = newW - oldW, dy = newH - oldH;
        int x = anchor switch
        {
            AnchorPosition.TopLeft or AnchorPosition.MiddleLeft or AnchorPosition.BottomLeft => 0,
            AnchorPosition.TopCenter or AnchorPosition.MiddleCenter or AnchorPosition.BottomCenter => dx / 2,
            _ => dx,
        };
        int y = anchor switch
        {
            AnchorPosition.TopLeft or AnchorPosition.TopCenter or AnchorPosition.TopRight => 0,
            AnchorPosition.MiddleLeft or AnchorPosition.MiddleCenter or AnchorPosition.MiddleRight => dy / 2,
            _ => dy,
        };
        return (x, y);
    }

    public static void Rotate90(Document doc, bool clockwise)
    {
        int newW = doc.Height, newH = doc.Width;
        doc.SetCanvas(newW, newH, layer =>
        {
            var src = layer.Surface;
            var dst = new Surface(newW, newH);
            Parallel.For(0, newH, y =>
            {
                var row = dst.GetRow(y);
                for (int x = 0; x < newW; x++)
                {
                    row[x] = clockwise
                        ? src[y, src.Height - 1 - x]
                        : src[src.Width - 1 - y, x];
                }
            });
            return dst;
        });
    }

    public static void Rotate180(Document doc)
    {
        foreach (var layer in doc.Layers)
        {
            var src = layer.Surface;
            var dst = new Surface(src.Width, src.Height);
            Parallel.For(0, src.Height, y =>
            {
                var row = dst.GetRow(y);
                var srcRow = src.GetRow(src.Height - 1 - y);
                for (int x = 0; x < src.Width; x++)
                    row[x] = srcRow[src.Width - 1 - x];
            });
            layer.Surface = dst;
        }
    }

    public static void FlipHorizontal(Document doc)
    {
        foreach (var layer in doc.Layers)
        {
            var s = layer.Surface;
            Parallel.For(0, s.Height, y =>
            {
                var row = s.GetRow(y);
                row.Reverse();
            });
        }
    }

    public static void FlipVertical(Document doc)
    {
        foreach (var layer in doc.Layers)
        {
            var s = layer.Surface;
            Parallel.For(0, s.Height / 2, y =>
            {
                var top = s.GetRow(y);
                var bottom = s.GetRow(s.Height - 1 - y);
                for (int x = 0; x < s.Width; x++)
                    (top[x], bottom[x]) = (bottom[x], top[x]);
            });
        }
    }

    /// <summary>Crops the document to a rect, applying selection coverage as alpha where partial.</summary>
    public static void CropTo(Document doc, RectInt rect, byte[]? selectionMask)
    {
        var r = rect.Intersect(doc.Bounds);
        if (r.IsEmpty) return;
        int oldWidth = doc.Width;
        doc.SetCanvas(r.Width, r.Height, layer =>
        {
            var s = new Surface(r.Width, r.Height);
            for (int y = 0; y < r.Height; y++)
            {
                var src = layer.Surface.GetRowSpan(r.Top + y, r.Left, r.Width);
                var dst = s.GetRow(y);
                src.CopyTo(dst);
                if (selectionMask != null)
                {
                    int maskRow = (r.Top + y) * oldWidth;
                    for (int x = 0; x < r.Width; x++)
                    {
                        byte cov = selectionMask[maskRow + r.Left + x];
                        if (cov == 255) continue;
                        uint c = dst[x];
                        dst[x] = ColorBgra.WithAlpha(c, (byte)(ColorBgra.A(c) * cov / 255));
                    }
                }
            }
            return s;
        });
    }

    /// <summary>Flattens all visible layers into a single layer.</summary>
    public static void Flatten(Document doc)
    {
        var flattened = doc.Flatten();
        doc.Layers.Clear();
        doc.Layers.Add(new Layer(flattened, "Background"));
        doc.ActiveLayerIndex = 0;
    }

    /// <summary>Merges the layer at <paramref name="index"/> into the layer below it.</summary>
    public static void MergeDown(Document doc, int index)
    {
        if (index <= 0 || index >= doc.Layers.Count) return;
        var top = doc.Layers[index];
        var bottom = doc.Layers[index - 1];
        var merged = new Surface(doc.Width, doc.Height);
        Parallel.For(0, doc.Height, y =>
        {
            var dstRow = merged.GetRow(y);
            var botRow = bottom.Surface.GetRow(y);
            var topRow = top.Surface.GetRow(y);
            for (int x = 0; x < doc.Width; x++)
            {
                uint under = BlendModeOps.Composite(bottom.BlendMode, 0, botRow[x], bottom.Opacity);
                dstRow[x] = BlendModeOps.Composite(top.BlendMode, under, topRow[x], top.Opacity);
            }
        });
        bottom.Surface = merged;
        bottom.Opacity = 255;
        bottom.BlendMode = BlendMode.Normal;
        doc.Layers.RemoveAt(index);
        doc.ActiveLayerIndex = index - 1;
    }
}
