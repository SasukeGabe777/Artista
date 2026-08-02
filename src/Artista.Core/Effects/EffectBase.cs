using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.Core.Effects;

/// <summary>
/// Base class for all effects and adjustments.
///
/// To add a new effect: derive from this class (or <see cref="PerPixelEffect"/>
/// for simple pixel-wise operations), implement <see cref="Render"/>, and
/// register an instance in <see cref="EffectRegistry"/>. The application builds
/// the menu entry and the configuration dialog (with live preview, apply and
/// cancel) automatically from <see cref="CreateParameters"/>.
/// </summary>
public abstract class EffectBase
{
    public abstract string Name { get; }

    /// <summary>Menu category, e.g. "Blurs", "Photo", "Stylize", "Transparency". Empty = top level.</summary>
    public virtual string Category => "";

    /// <summary>True for entries under Adjustments, false for Effects.</summary>
    public virtual bool IsAdjustment => false;

    /// <summary>True when the effect has no parameters and applies immediately without a dialog.</summary>
    public bool IsConfigurable => CreateParameters().Count > 0;

    public abstract IReadOnlyList<EffectParameter> CreateParameters();

    /// <summary>
    /// Renders the effect. Read from <paramref name="src"/>, write to
    /// <paramref name="dst"/> (different surfaces, same size), only within
    /// <paramref name="roi"/>. Must check the token cooperatively.
    /// </summary>
    public abstract void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token);
}

/// <summary>Convenience base for effects computed independently per pixel.</summary>
public abstract class PerPixelEffect : EffectBase
{
    /// <summary>Called once per invocation to build a per-pixel transform closure.</summary>
    protected abstract Func<uint, uint> CreateTransform(ParameterSet parameters);

    public override void Render(Surface src, Surface dst, RectInt roi, ParameterSet parameters, CancellationToken token)
    {
        var transform = CreateTransform(parameters);
        var r = roi.Intersect(src.Bounds);
        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var srcRow = src.GetRowSpan(y, r.Left, r.Width);
            var dstRow = dst.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < srcRow.Length; x++)
                dstRow[x] = transform(srcRow[x]);
        });
    }
}

/// <summary>
/// Helpers for running an effect against a layer with selection-mask blending
/// and full-state restoration on cancellation.
/// </summary>
public static class EffectRunner
{
    /// <summary>
    /// Runs <paramref name="effect"/> over <paramref name="source"/> and writes
    /// the result into <paramref name="target"/> (may be the same surface as
    /// source is cloned internally per call site — pass distinct surfaces).
    /// Pixels are blended by selection coverage: outside the selection the
    /// source is preserved exactly.
    /// </summary>
    public static void RunMasked(
        EffectBase effect, Surface source, Surface target, ParameterSet parameters,
        Selection selection, RectInt roi, CancellationToken token)
    {
        var r = roi.Intersect(source.Bounds);
        if (r.IsEmpty) return;

        var rendered = new Surface(source.Width, source.Height);
        rendered.CopyRect(source, r);
        effect.Render(source, rendered, r, parameters, token);
        token.ThrowIfCancellationRequested();

        bool hasSelection = !selection.IsEmpty;
        Parallel.For(r.Top, r.Bottom, new ParallelOptions { CancellationToken = token }, y =>
        {
            var srcRow = source.GetRowSpan(y, r.Left, r.Width);
            var fxRow = rendered.GetRowSpan(y, r.Left, r.Width);
            var dstRow = target.GetRowSpan(y, r.Left, r.Width);
            if (!hasSelection)
            {
                fxRow.CopyTo(dstRow);
                return;
            }
            for (int x = 0; x < dstRow.Length; x++)
            {
                byte cov = selection.MaskAt(r.Left + x, y);
                dstRow[x] = cov switch
                {
                    0 => srcRow[x],
                    255 => fxRow[x],
                    _ => ColorBgra.Lerp(srcRow[x], fxRow[x], cov / 255f),
                };
            }
        });
    }
}
