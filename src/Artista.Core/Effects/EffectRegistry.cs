namespace Artista.Core.Effects;

/// <summary>
/// Central list of available adjustments and effects. The application builds
/// its Adjustments and Effects menus from this registry, so registering a new
/// effect instance here is all that's needed to surface it in the UI.
/// </summary>
public static class EffectRegistry
{
    public static readonly IReadOnlyList<EffectBase> Adjustments = new EffectBase[]
    {
        new AutoLevelAdjustment(),
        new BlackAndWhiteAdjustment(),
        new BrightnessContrastAdjustment(),
        new CurvesAdjustment(),
        new HueSaturationAdjustment(),
        new InvertColorsAdjustment(),
        new LevelsAdjustment(),
        new PosterizeAdjustment(),
        new SepiaAdjustment(),
        new TransparencyAdjustment(),
    };

    public static readonly IReadOnlyList<EffectBase> Effects = new EffectBase[]
    {
        new GaussianBlurEffect(),
        new MotionBlurEffect(),
        new SharpenEffect(),
        new NoiseEffect(),
        new ReduceNoiseEffect(),
        new PixelateEffect(),
        new OutlineEffect(),
        new DropShadowEffect(),
        new GlowEffect(),
        new EmbossEffect(),
        new EdgeDetectEffect(),
        new VignetteEffect(),
        new RemoveColorEffect(),
    };
}
