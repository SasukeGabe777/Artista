namespace Artista.Core.Effects;

/// <summary>
/// Control points for the Curves adjustment. Channel 0 = luminosity (applied to
/// RGB together); channels 1-3 = R, G, B individually when PerChannel is true.
/// </summary>
public sealed class CurvesValue
{
    public bool PerChannel { get; set; }

    /// <summary>Four point lists: [lum, r, g, b]; points are (x,y) in 0-255.</summary>
    public List<(double X, double Y)>[] Channels { get; }

    public CurvesValue()
    {
        Channels = new List<(double, double)>[4];
        for (int i = 0; i < 4; i++)
            Channels[i] = new List<(double, double)> { (0, 0), (255, 255) };
    }

    public static CurvesValue Identity() => new();

    public CurvesValue Clone()
    {
        var c = new CurvesValue { PerChannel = PerChannel };
        for (int i = 0; i < 4; i++)
            c.Channels[i] = new List<(double, double)>(Channels[i]);
        return c;
    }

    /// <summary>Builds a 256-entry LUT for a channel using monotone cubic interpolation.</summary>
    public byte[] BuildLut(int channel)
    {
        var pts = Channels[channel].OrderBy(p => p.X).ToList();
        var lut = new byte[256];
        if (pts.Count == 0)
        {
            for (int i = 0; i < 256; i++) lut[i] = (byte)i;
            return lut;
        }
        if (pts.Count == 1)
        {
            byte v = (byte)Math.Clamp(pts[0].Y, 0, 255);
            for (int i = 0; i < 256; i++) lut[i] = v;
            return lut;
        }

        // Fritsch–Carlson monotone cubic spline.
        int n = pts.Count;
        var xs = pts.Select(p => p.X).ToArray();
        var ys = pts.Select(p => p.Y).ToArray();
        var dx = new double[n - 1];
        var slopes = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            dx[i] = Math.Max(1e-6, xs[i + 1] - xs[i]);
            slopes[i] = (ys[i + 1] - ys[i]) / dx[i];
        }
        var m = new double[n];
        m[0] = slopes[0];
        m[n - 1] = slopes[n - 2];
        for (int i = 1; i < n - 1; i++)
            m[i] = slopes[i - 1] * slopes[i] <= 0 ? 0 : (slopes[i - 1] + slopes[i]) / 2;
        for (int i = 0; i < n - 1; i++)
        {
            if (slopes[i] == 0) { m[i] = 0; m[i + 1] = 0; continue; }
            double a = m[i] / slopes[i], b = m[i + 1] / slopes[i];
            double s = a * a + b * b;
            if (s > 9)
            {
                double t = 3.0 / Math.Sqrt(s);
                m[i] = t * a * slopes[i];
                m[i + 1] = t * b * slopes[i];
            }
        }

        for (int x = 0; x < 256; x++)
        {
            double y;
            if (x <= xs[0]) y = ys[0];
            else if (x >= xs[n - 1]) y = ys[n - 1];
            else
            {
                int seg = 0;
                while (seg < n - 2 && x > xs[seg + 1]) seg++;
                double h = dx[seg];
                double t = (x - xs[seg]) / h;
                double t2 = t * t, t3 = t2 * t;
                y = (2 * t3 - 3 * t2 + 1) * ys[seg]
                    + (t3 - 2 * t2 + t) * h * m[seg]
                    + (-2 * t3 + 3 * t2) * ys[seg + 1]
                    + (t3 - t2) * h * m[seg + 1];
            }
            lut[x] = (byte)Math.Clamp(y, 0, 255);
        }
        return lut;
    }
}
