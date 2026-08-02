using Artista.Core.ColorEngine;
using Artista.Core.Imaging;

namespace Artista.Core.Effects;

/// <summary>
/// Effects → Transparency → Remove Color.
///
/// Removes all pixels matching the target color (within tolerance, with a soft
/// alpha falloff band) from the layer, regardless of connectivity. Matching is
/// perceptual (OKLab) via the shared <see cref="ColorMatcher"/> engine, which
/// is also used by the Color Remover brush tool.
///
/// Scope (current layer / all visible layers / all layers including hidden) is
/// interpreted by the application layer, which runs this effect once per
/// target layer. Selection clipping is applied by the standard effect
/// pipeline (<see cref="EffectRunner.RunMasked"/>).
/// </summary>
public sealed class RemoveColorEffect : EffectBase
{
    public const string ScopeParamId = "scope";
    public const int ScopeCurrentLayer = 0;
    public const int ScopeAllVisible = 1;
    public const int ScopeAllLayers = 2;

    public override string Name => "Remove Color";
    public override string Category => "Transparency";

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new ColorParameter("target", "Target color", 0xFFFFFFFF, allowEyedropper: true),
        new IntParameter("tolerance", "Tolerance", 0, 100, 10),
        new IntParameter("softness", "Edge softness", 0, 100, 20),
        new EnumParameter(ScopeParamId, "Apply to",
            new[] { "Current layer", "All visible layers", "All layers (including hidden)" }, 0),
        new BoolParameter("limitToSelection", "Limit to selection", true),
        new BoolParameter("preserveAlpha", "Scale existing transparency proportionally", true),
    };

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var matcher = ColorMatcher.FromBgra(
            parameters.GetColor("target"),
            parameters.GetInt("tolerance"),
            parameters.GetInt("softness"));
        bool preserveAlpha = parameters.GetBool("preserveAlpha");

        var r = roi.Intersect(src.Bounds);
        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var srcRow = src.GetRowSpan(y, r.Left, r.Width);
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < srcRow.Length; x++)
            {
                uint c = srcRow[x];
                byte alpha = ColorBgra.A(c);
                if (alpha == 0)
                {
                    dstRow[x] = c;
                    continue;
                }
                float f = matcher.Match(c);
                if (f <= 0f)
                {
                    dstRow[x] = c;
                    continue;
                }
                byte newAlpha;
                if (preserveAlpha)
                {
                    // Scale existing alpha down: partially transparent pixels stay
                    // proportionally transparent.
                    newAlpha = (byte)Math.Clamp(alpha * (1f - f) + 0.5f, 0, 255);
                }
                else
                {
                    newAlpha = (byte)Math.Clamp(255f * (1f - f) + 0.5f, 0, Math.Max((byte)0, alpha));
                    newAlpha = Math.Min(newAlpha, alpha);
                }
                dstRow[x] = ColorBgra.WithAlpha(c, newAlpha);
            }
        });
    }
}
