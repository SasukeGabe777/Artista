namespace Artista.Core.Imaging;

/// <summary>
/// A 32-bit BGRA (straight alpha) pixel buffer. The backing store is a flat
/// uint array in row-major order, directly compatible with WPF's Bgra32 format.
/// </summary>
public sealed class Surface
{
    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }

    public RectInt Bounds => new(0, 0, Width, Height);
    public long ByteCount => (long)Width * Height * 4;

    public Surface(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Surface dimensions must be positive ({width}x{height}).");
        Width = width;
        Height = height;
        Pixels = new uint[(long)width * height];
    }

    public uint this[int x, int y]
    {
        get => Pixels[(long)y * Width + x];
        set => Pixels[(long)y * Width + x] = value;
    }

    public uint GetPixelClamped(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        return Pixels[(long)y * Width + x];
    }

    public Span<uint> GetRow(int y) => Pixels.AsSpan(y * Width, Width);

    public Span<uint> GetRowSpan(int y, int x, int length) => Pixels.AsSpan(y * Width + x, length);

    public void Clear(uint color = 0)
    {
        Array.Fill(Pixels, color);
    }

    public void FillRect(RectInt rect, uint color)
    {
        var r = rect.Intersect(Bounds);
        for (int y = r.Top; y < r.Bottom; y++)
            GetRowSpan(y, r.Left, r.Width).Fill(color);
    }

    public Surface Clone()
    {
        var s = new Surface(Width, Height);
        Array.Copy(Pixels, s.Pixels, Pixels.Length);
        return s;
    }

    /// <summary>Copies the given rect from <paramref name="source"/> at the same coordinates.</summary>
    public void CopyRect(Surface source, RectInt rect)
    {
        var r = rect.Intersect(Bounds).Intersect(source.Bounds);
        for (int y = r.Top; y < r.Bottom; y++)
            source.GetRowSpan(y, r.Left, r.Width).CopyTo(GetRowSpan(y, r.Left, r.Width));
    }

    /// <summary>Copies all pixels from <paramref name="source"/> (must be the same size).</summary>
    public void CopyFrom(Surface source)
    {
        if (source.Width != Width || source.Height != Height)
            throw new ArgumentException("Surface size mismatch.");
        Array.Copy(source.Pixels, Pixels, Pixels.Length);
    }

    /// <summary>Extracts a rectangular region into a tightly packed pixel array.</summary>
    public uint[] ExtractRect(RectInt rect)
    {
        var r = rect.Intersect(Bounds);
        var data = new uint[r.Area];
        int i = 0;
        for (int y = r.Top; y < r.Bottom; y++, i += r.Width)
            GetRowSpan(y, r.Left, r.Width).CopyTo(data.AsSpan(i, r.Width));
        return data;
    }

    /// <summary>Writes a tightly packed pixel array (from <see cref="ExtractRect"/>) back into place.</summary>
    public void WriteRect(RectInt rect, uint[] data)
    {
        var r = rect.Intersect(Bounds);
        int i = 0;
        for (int y = r.Top; y < r.Bottom; y++, i += r.Width)
            data.AsSpan(i, r.Width).CopyTo(GetRowSpan(y, r.Left, r.Width));
    }

    /// <summary>Draws (blits, source-over) a source surface at an offset.</summary>
    public void DrawSurfaceOver(Surface source, int offsetX, int offsetY)
    {
        var target = new RectInt(offsetX, offsetY, source.Width, source.Height).Intersect(Bounds);
        for (int y = target.Top; y < target.Bottom; y++)
        {
            var dstRow = GetRowSpan(y, target.Left, target.Width);
            var srcRow = source.GetRowSpan(y - offsetY, target.Left - offsetX, target.Width);
            for (int x = 0; x < dstRow.Length; x++)
                dstRow[x] = ColorBgra.Over(dstRow[x], srcRow[x]);
        }
    }

    /// <summary>Nearest-neighbor or bilinear resize into a new surface.</summary>
    public Surface Resized(int newWidth, int newHeight, ResampleMode mode = ResampleMode.Bilinear)
    {
        var dst = new Surface(newWidth, newHeight);
        if (mode == ResampleMode.NearestNeighbor)
        {
            Parallel.For(0, newHeight, y =>
            {
                int sy = Math.Min((int)((y + 0.5) * Height / newHeight), Height - 1);
                var dstRow = dst.GetRow(y);
                var srcRow = GetRow(sy);
                for (int x = 0; x < newWidth; x++)
                {
                    int sx = Math.Min((int)((x + 0.5) * Width / newWidth), Width - 1);
                    dstRow[x] = srcRow[sx];
                }
            });
        }
        else
        {
            // Bilinear with alpha-weighted color averaging to avoid halos from
            // transparent pixels bleeding their (invisible) color values.
            double scaleX = (double)Width / newWidth;
            double scaleY = (double)Height / newHeight;
            Parallel.For(0, newHeight, y =>
            {
                double srcY = (y + 0.5) * scaleY - 0.5;
                int y0 = (int)Math.Floor(srcY);
                double fy = srcY - y0;
                int y1 = Math.Clamp(y0 + 1, 0, Height - 1);
                y0 = Math.Clamp(y0, 0, Height - 1);
                for (int x = 0; x < newWidth; x++)
                {
                    double srcX = (x + 0.5) * scaleX - 0.5;
                    int x0 = (int)Math.Floor(srcX);
                    double fx = srcX - x0;
                    int x1 = Math.Clamp(x0 + 1, 0, Width - 1);
                    x0 = Math.Clamp(x0, 0, Width - 1);

                    uint c00 = this[x0, y0], c10 = this[x1, y0], c01 = this[x0, y1], c11 = this[x1, y1];
                    double w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;

                    double a00 = ColorBgra.A(c00) * w00, a10 = ColorBgra.A(c10) * w10;
                    double a01 = ColorBgra.A(c01) * w01, a11 = ColorBgra.A(c11) * w11;
                    double aSum = a00 + a10 + a01 + a11;
                    double aOut = aSum;
                    if (aSum <= 0.0001)
                    {
                        dst[x, y] = 0;
                        continue;
                    }
                    double b = (ColorBgra.B(c00) * a00 + ColorBgra.B(c10) * a10 + ColorBgra.B(c01) * a01 + ColorBgra.B(c11) * a11) / aSum;
                    double g = (ColorBgra.G(c00) * a00 + ColorBgra.G(c10) * a10 + ColorBgra.G(c01) * a01 + ColorBgra.G(c11) * a11) / aSum;
                    double r = (ColorBgra.R(c00) * a00 + ColorBgra.R(c10) * a10 + ColorBgra.R(c01) * a01 + ColorBgra.R(c11) * a11) / aSum;
                    dst[x, y] = ColorBgra.Pack(
                        (byte)Math.Clamp(b + 0.5, 0, 255),
                        (byte)Math.Clamp(g + 0.5, 0, 255),
                        (byte)Math.Clamp(r + 0.5, 0, 255),
                        (byte)Math.Clamp(aOut + 0.5, 0, 255));
                }
            });
        }
        return dst;
    }
}

public enum ResampleMode
{
    NearestNeighbor,
    Bilinear,
}
