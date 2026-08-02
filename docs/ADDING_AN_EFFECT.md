# Adding an effect or adjustment

Effects live in `src/Artista.Core/Effects/`. An effect is a class deriving from `EffectBase` (or `PerPixelEffect` for pixel-independent operations). The configuration dialog — sliders, checkboxes, combos, color pickers with eyedropper, curve editors — plus live preview, cancellation and undo integration are all provided by the framework; you only write the parameter list and the pixel math.

## 1. Per-pixel example (an adjustment)

```csharp
public sealed class ExposureAdjustment : PerPixelEffect
{
    public override string Name => "Exposure";
    public override bool IsAdjustment => true;   // Adjustments menu instead of Effects

    public override IReadOnlyList<EffectParameter> CreateParameters() => new EffectParameter[]
    {
        new DoubleParameter("stops", "Exposure (stops)", -4, 4, 0, decimals: 2),
    };

    protected override Func<uint, uint> CreateTransform(ParameterSet p)
    {
        double factor = Math.Pow(2, p.GetDouble("stops"));
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
            lut[i] = (byte)Math.Clamp(i * factor, 0, 255);
        return c => ColorBgra.Pack(lut[ColorBgra.B(c)], lut[ColorBgra.G(c)], lut[ColorBgra.R(c)], ColorBgra.A(c));
    }
}
```

## 2. Area example (needs neighboring pixels)

Derive from `EffectBase` and implement `Render(src, dst, roi, parameters, token)`:

- Read from `src` (never modified), write to `dst`, only inside `roi`.
- Check `token` cooperatively (`ParallelOptions { CancellationToken = token }` does this for you).
- `BlurHelpers.GaussianBlur` is available as a building block (alpha-weighted, box-approximated).

## 3. Register it

Add an instance to `EffectRegistry.Adjustments` or `EffectRegistry.Effects` (`Effects/EffectRegistry.cs`). Set `Category` (e.g. `"Blurs"`, `"Photo"`, `"Stylize"`, `"Transparency"`) to control the submenu. The Adjustments/Effects menus are generated from the registry at startup.

## Parameter types

| Type | Dialog control |
|---|---|
| `IntParameter(id, label, min, max, default, unit?)` | slider + numeric box |
| `DoubleParameter(...)` | slider + numeric box |
| `BoolParameter` | checkbox |
| `EnumParameter(id, label, options[], defaultIndex)` | combo box |
| `ColorParameter(id, label, default, allowEyedropper)` | swatch + hex (+ *Pick from canvas*) |
| `CurvesParameter` | editable spline curve editor (per-channel) |

## Framework guarantees (don't re-implement these)

- **Live preview** renders on a background thread into a substitute surface; the layer itself is untouched until OK.
- **Cancel / close** restores the exact original image (the preview is simply discarded).
- **Selection masking**: `EffectRunner.RunMasked` blends effect output by selection coverage; pixels outside the selection are byte-identical to the source.
- **Undo**: application commits as one `SurfaceRegionMemento` (or one `CompositeMemento` for multi-layer scope) — one history entry per apply.
- **Locked layers** are excluded from application.

## Multi-layer scope

Plain effects apply to the active layer. If an effect needs Remove-Color-style scope ("all visible layers", "all layers"), follow the pattern in `MainWindow.ResolveEffectTargets` — the effect stays single-surface; the shell runs it once per target layer and groups the mementos.
