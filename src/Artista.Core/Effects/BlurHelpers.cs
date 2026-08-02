using Artista.Core.Imaging;

namespace Artista.Core.Effects;

/// <summary>
/// Shared Gaussian blur implementation (three box-blur passes, a standard and
/// fast approximation) with alpha-weighted color averaging so transparent
/// pixels don't bleed color halos.
/// </summary>
public static class BlurHelpers
{
    public static void GaussianBlur(Surface src, Surface dst, RectInt roi, int radius, CancellationToken token)
    {
        var r = roi.Intersect(src.Bounds);
        if (r.IsEmpty) return;
        if (radius <= 0)
        {
            dst.CopyRect(src, r);
            return;
        }

        // Work in premultiplied float space for correct alpha handling.
        int w = r.Width, h = r.Height;
        var buf = new float[w * h * 4];
        var tmp = new float[w * h * 4];

        Parallel.For(0, h, new ParallelOptions { CancellationToken = token }, y =>
        {
            var row = src.GetRowSpan(r.Top + y, r.Left, w);
            int o = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                uint c = row[x];
                float a = ColorBgra.A(c) / 255f;
                buf[o + x * 4 + 0] = ColorBgra.B(c) * a;
                buf[o + x * 4 + 1] = ColorBgra.G(c) * a;
                buf[o + x * 4 + 2] = ColorBgra.R(c) * a;
                buf[o + x * 4 + 3] = a;
            }
        });

        int[] boxes = BoxesForGauss(radius, 3);
        foreach (int box in boxes)
        {
            int br = (box - 1) / 2;
            BoxBlurH(buf, tmp, w, h, br, token);
            BoxBlurV(tmp, buf, w, h, br, token);
        }

        Parallel.For(0, h, new ParallelOptions { CancellationToken = token }, y =>
        {
            var row = dst.GetRowSpan(r.Top + y, r.Left, w);
            int o = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                float a = buf[o + x * 4 + 3];
                if (a <= 0.0001f)
                {
                    row[x] = 0;
                    continue;
                }
                byte b = (byte)Math.Clamp(buf[o + x * 4 + 0] / a + 0.5f, 0, 255);
                byte g = (byte)Math.Clamp(buf[o + x * 4 + 1] / a + 0.5f, 0, 255);
                byte rr = (byte)Math.Clamp(buf[o + x * 4 + 2] / a + 0.5f, 0, 255);
                row[x] = ColorBgra.Pack(b, g, rr, (byte)Math.Clamp(a * 255f + 0.5f, 0, 255));
            }
        });
    }

    private static int[] BoxesForGauss(double sigma, int n)
    {
        double wIdeal = Math.Sqrt(12 * sigma * sigma / n + 1);
        int wl = (int)Math.Floor(wIdeal);
        if (wl % 2 == 0) wl--;
        int wu = wl + 2;
        double mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4 * wl - 4);
        int m = (int)Math.Round(mIdeal);
        var sizes = new int[n];
        for (int i = 0; i < n; i++)
            sizes[i] = i < m ? wl : wu;
        return sizes;
    }

    private static void BoxBlurH(float[] src, float[] dst, int w, int h, int r, CancellationToken token)
    {
        if (r <= 0) { Array.Copy(src, dst, src.Length); return; }
        float norm = 1f / (2 * r + 1);
        Parallel.For(0, h, new ParallelOptions { CancellationToken = token }, y =>
        {
            int row = y * w * 4;
            Span<float> sum = stackalloc float[4];
            sum.Clear();
            for (int x = -r; x <= r; x++)
            {
                int cx = Math.Clamp(x, 0, w - 1);
                for (int ch = 0; ch < 4; ch++)
                    sum[ch] += src[row + cx * 4 + ch];
            }
            for (int x = 0; x < w; x++)
            {
                for (int ch = 0; ch < 4; ch++)
                    dst[row + x * 4 + ch] = sum[ch] * norm;
                int addX = Math.Min(x + r + 1, w - 1);
                int subX = Math.Max(x - r, 0);
                for (int ch = 0; ch < 4; ch++)
                    sum[ch] += src[row + addX * 4 + ch] - src[row + subX * 4 + ch];
            }
        });
    }

    private static void BoxBlurV(float[] src, float[] dst, int w, int h, int r, CancellationToken token)
    {
        if (r <= 0) { Array.Copy(src, dst, src.Length); return; }
        float norm = 1f / (2 * r + 1);
        Parallel.For(0, w, new ParallelOptions { CancellationToken = token }, x =>
        {
            Span<float> sum = stackalloc float[4];
            sum.Clear();
            for (int y = -r; y <= r; y++)
            {
                int cy = Math.Clamp(y, 0, h - 1);
                for (int ch = 0; ch < 4; ch++)
                    sum[ch] += src[(cy * w + x) * 4 + ch];
            }
            for (int y = 0; y < h; y++)
            {
                for (int ch = 0; ch < 4; ch++)
                    dst[(y * w + x) * 4 + ch] = sum[ch] * norm;
                int addY = Math.Min(y + r + 1, h - 1);
                int subY = Math.Max(y - r, 0);
                for (int ch = 0; ch < 4; ch++)
                    sum[ch] += src[(addY * w + x) * 4 + ch] - src[(subY * w + x) * 4 + ch];
            }
        });
    }
}
