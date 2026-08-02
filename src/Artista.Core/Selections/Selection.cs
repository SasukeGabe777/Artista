using Artista.Core.Imaging;

namespace Artista.Core.Selections;

public enum SelectionCombineMode
{
    Replace,
    Add,
    Subtract,
    Intersect,
}

/// <summary>
/// A per-pixel coverage mask (0 = unselected, 255 = fully selected) over the
/// document canvas. An empty selection means "everything is editable"
/// (i.e. no selection active), matching Paint.NET semantics.
/// </summary>
public sealed class Selection
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Mask { get; }

    private RectInt _bounds = RectInt.Empty;
    private bool _boundsValid = true;
    private bool _isEmpty = true;

    /// <summary>Monotonically increases every time the mask changes (used for cached outline geometry).</summary>
    public int Version { get; private set; }

    public Selection(int width, int height)
    {
        Width = width;
        Height = height;
        Mask = new byte[(long)width * height];
    }

    /// <summary>True when no selection is active (all pixels editable).</summary>
    public bool IsEmpty
    {
        get
        {
            EnsureBounds();
            return _isEmpty;
        }
    }

    /// <summary>Bounding box of selected pixels; Empty when no selection.</summary>
    public RectInt Bounds
    {
        get
        {
            EnsureBounds();
            return _bounds;
        }
    }

    /// <summary>The rect that tools/effects should operate on: selection bounds, or the whole canvas when empty.</summary>
    public RectInt EffectiveBounds => IsEmpty ? new RectInt(0, 0, Width, Height) : Bounds;

    public byte CoverageAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return 0;
        if (IsEmpty) return 255;
        return Mask[y * Width + x];
    }

    /// <summary>Raw mask value (0 when empty selection — no implicit select-all).</summary>
    public byte MaskAt(int x, int y) => Mask[y * Width + x];

    public void MarkChanged()
    {
        _boundsValid = false;
        Version++;
    }

    private void EnsureBounds()
    {
        if (_boundsValid) return;
        _boundsValid = true;
        int minX = Width, minY = Height, maxX = -1, maxY = -1;
        for (int y = 0; y < Height; y++)
        {
            var row = Mask.AsSpan(y * Width, Width);
            int first = row.IndexOfAnyExcept((byte)0);
            if (first < 0) continue;
            int last = row.LastIndexOfAnyExcept((byte)0);
            if (first < minX) minX = first;
            if (last > maxX) maxX = last;
            if (y < minY) minY = y;
            maxY = y;
        }
        _isEmpty = maxX < 0;
        _bounds = _isEmpty ? RectInt.Empty : RectInt.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    public void Clear()
    {
        Array.Clear(Mask);
        MarkChanged();
    }

    public void SelectAll()
    {
        Array.Fill(Mask, (byte)255);
        MarkChanged();
    }

    public void Invert()
    {
        // Inverting "no selection" (= everything) yields no selection again,
        // so only meaningful when a selection exists; caller decides.
        for (int i = 0; i < Mask.Length; i++)
            Mask[i] = (byte)(255 - Mask[i]);
        MarkChanged();
    }

    public Selection Clone()
    {
        var s = new Selection(Width, Height);
        Array.Copy(Mask, s.Mask, Mask.Length);
        s.MarkChanged();
        return s;
    }

    public byte[] SnapshotMask() => (byte[])Mask.Clone();

    public void RestoreMask(byte[] snapshot)
    {
        Array.Copy(snapshot, Mask, Mask.Length);
        MarkChanged();
    }

    /// <summary>Combines a new coverage mask into this selection.</summary>
    public void Combine(byte[] newMask, SelectionCombineMode mode)
    {
        switch (mode)
        {
            case SelectionCombineMode.Replace:
                Array.Copy(newMask, Mask, Mask.Length);
                break;
            case SelectionCombineMode.Add:
                for (int i = 0; i < Mask.Length; i++)
                    Mask[i] = Math.Max(Mask[i], newMask[i]);
                break;
            case SelectionCombineMode.Subtract:
                for (int i = 0; i < Mask.Length; i++)
                    Mask[i] = (byte)Math.Max(0, Mask[i] - newMask[i]);
                break;
            case SelectionCombineMode.Intersect:
                for (int i = 0; i < Mask.Length; i++)
                    Mask[i] = Math.Min(Mask[i], newMask[i]);
                break;
        }
        MarkChanged();
    }

    /// <summary>Translates the selection outline by (dx, dy) pixels.</summary>
    public void Translate(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        var old = SnapshotMask();
        Array.Clear(Mask);
        for (int y = 0; y < Height; y++)
        {
            int sy = y - dy;
            if (sy < 0 || sy >= Height) continue;
            int dstStart = Math.Max(0, dx);
            int srcStart = Math.Max(0, -dx);
            int len = Width - Math.Abs(dx);
            if (len <= 0) continue;
            Array.Copy(old, sy * Width + srcStart, Mask, y * Width + dstStart, len);
        }
        MarkChanged();
    }

    /// <summary>Gaussian-ish feather using three box blur passes on the mask.</summary>
    public void Feather(int radius)
    {
        if (radius <= 0 || IsEmpty) return;
        int boxR = Math.Max(1, (int)(radius / 1.8));
        var tmp = new byte[Mask.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlurH(Mask, tmp, Width, Height, boxR);
            BoxBlurV(tmp, Mask, Width, Height, boxR);
        }
        MarkChanged();
    }

    private static void BoxBlurH(byte[] src, byte[] dst, int w, int h, int r)
    {
        Parallel.For(0, h, y =>
        {
            int row = y * w;
            int windowSize = 2 * r + 1;
            int sum = 0;
            for (int x = -r; x <= r; x++)
                sum += src[row + Math.Clamp(x, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                dst[row + x] = (byte)(sum / windowSize);
                int addX = Math.Min(x + r + 1, w - 1);
                int subX = Math.Max(x - r, 0);
                sum += src[row + addX] - src[row + subX];
            }
        });
    }

    private static void BoxBlurV(byte[] src, byte[] dst, int w, int h, int r)
    {
        Parallel.For(0, w, x =>
        {
            int windowSize = 2 * r + 1;
            int sum = 0;
            for (int y = -r; y <= r; y++)
                sum += src[Math.Clamp(y, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = (byte)(sum / windowSize);
                int addY = Math.Min(y + r + 1, h - 1);
                int subY = Math.Max(y - r, 0);
                sum += src[addY * w + x] - src[subY * w + x];
            }
        });
    }
}
