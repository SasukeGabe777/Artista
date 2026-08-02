using Artista.Core.Documents;
using Artista.Core.Imaging;
using Artista.Core.Layers;
using Artista.Core.Rendering;

namespace Artista.Tests;

public class CompositingTests
{
    [Fact]
    public void OpaqueTopLayerHidesBottomLayer()
    {
        var doc = new Document(4, 4);
        var bottom = new Layer(4, 4, "bottom");
        bottom.Surface.Clear(ColorBgra.Pack(0, 0, 255, 255)); // red
        var top = new Layer(4, 4, "top");
        top.Surface.Clear(ColorBgra.Pack(255, 0, 0, 255)); // blue
        doc.Layers.Add(bottom);
        doc.Layers.Add(top);

        var result = doc.Flatten();
        Assert.Equal(ColorBgra.Pack(255, 0, 0, 255), result[1, 1]);
    }

    [Fact]
    public void HiddenLayerIsSkipped()
    {
        var doc = new Document(4, 4);
        var bottom = new Layer(4, 4, "bottom");
        bottom.Surface.Clear(ColorBgra.Pack(0, 0, 255, 255));
        var top = new Layer(4, 4, "top") { Visible = false };
        top.Surface.Clear(ColorBgra.Pack(255, 0, 0, 255));
        doc.Layers.Add(bottom);
        doc.Layers.Add(top);

        var result = doc.Flatten();
        Assert.Equal(ColorBgra.Pack(0, 0, 255, 255), result[0, 0]);
    }

    [Fact]
    public void HalfOpacityLayerBlendsFiftyFifty()
    {
        var doc = new Document(2, 2);
        var bottom = new Layer(2, 2, "bottom");
        bottom.Surface.Clear(ColorBgra.Pack(0, 0, 0, 255)); // black
        var top = new Layer(2, 2, "top") { Opacity = 128 };
        top.Surface.Clear(ColorBgra.Pack(255, 255, 255, 255)); // white
        doc.Layers.Add(bottom);
        doc.Layers.Add(top);

        var result = doc.Flatten();
        uint c = result[0, 0];
        Assert.InRange(ColorBgra.R(c), 126, 130);
        Assert.Equal(255, ColorBgra.A(c));
    }

    [Fact]
    public void TransparentDocumentFlattensToTransparent()
    {
        var doc = new Document(2, 2);
        doc.Layers.Add(new Layer(2, 2, "empty"));
        var result = doc.Flatten();
        Assert.Equal(0u, result[0, 0]);
    }

    [Fact]
    public void SemiTransparentOverTransparentKeepsColorAndAlpha()
    {
        var doc = new Document(2, 2);
        var layer = new Layer(2, 2, "l");
        layer.Surface.Clear(ColorBgra.Pack(10, 20, 30, 128));
        doc.Layers.Add(layer);
        var result = doc.Flatten();
        Assert.Equal(ColorBgra.Pack(10, 20, 30, 128), result[0, 0]);
    }

    [Fact]
    public void SubstituteSurfaceIsUsedInsteadOfLayerSurface()
    {
        var doc = new Document(2, 2);
        var layer = new Layer(2, 2, "l");
        layer.Surface.Clear(ColorBgra.Pack(0, 0, 255, 255));
        doc.Layers.Add(layer);

        var substitute = new Surface(2, 2);
        substitute.Clear(ColorBgra.Pack(0, 255, 0, 255));
        var dst = new Surface(2, 2);
        Compositor.Composite(doc, dst, doc.Bounds, layer.Id, substitute);
        Assert.Equal(ColorBgra.Pack(0, 255, 0, 255), dst[0, 0]);
    }
}

public class BlendModeTests
{
    private static uint Blend(BlendMode mode, uint dst, uint src, int opacity = 255) =>
        BlendModeOps.Composite(mode, dst, src, opacity);

    [Fact]
    public void MultiplyDarkens()
    {
        uint gray = ColorBgra.Pack(128, 128, 128, 255);
        uint result = Blend(BlendMode.Multiply, gray, gray);
        Assert.InRange(ColorBgra.R(result), 63, 65); // 128*128/255 ≈ 64
    }

    [Fact]
    public void MultiplyWithWhiteIsIdentity()
    {
        uint color = ColorBgra.Pack(10, 200, 90, 255);
        uint result = Blend(BlendMode.Multiply, color, ColorBgra.White);
        Assert.Equal(color, result);
    }

    [Fact]
    public void ScreenLightens()
    {
        uint gray = ColorBgra.Pack(128, 128, 128, 255);
        uint result = Blend(BlendMode.Screen, gray, gray);
        Assert.InRange(ColorBgra.R(result), 190, 193); // 255-(127*127/255) ≈ 191
    }

    [Fact]
    public void ScreenWithBlackIsIdentity()
    {
        uint color = ColorBgra.Pack(10, 200, 90, 255);
        uint result = Blend(BlendMode.Screen, color, ColorBgra.Black);
        Assert.Equal(color, result);
    }

    [Fact]
    public void DarkenPicksMinimumPerChannel()
    {
        uint a = ColorBgra.Pack(10, 200, 90, 255);
        uint b = ColorBgra.Pack(50, 100, 150, 255);
        uint result = Blend(BlendMode.Darken, a, b);
        Assert.Equal(10, ColorBgra.B(result));
        Assert.Equal(100, ColorBgra.G(result));
        Assert.Equal(90, ColorBgra.R(result));
    }

    [Fact]
    public void LightenPicksMaximumPerChannel()
    {
        uint a = ColorBgra.Pack(10, 200, 90, 255);
        uint b = ColorBgra.Pack(50, 100, 150, 255);
        uint result = Blend(BlendMode.Lighten, a, b);
        Assert.Equal(50, ColorBgra.B(result));
        Assert.Equal(200, ColorBgra.G(result));
        Assert.Equal(150, ColorBgra.R(result));
    }

    [Fact]
    public void DifferenceOfSameColorIsBlack()
    {
        uint color = ColorBgra.Pack(80, 90, 100, 255);
        uint result = Blend(BlendMode.Difference, color, color);
        Assert.Equal(0, ColorBgra.R(result));
        Assert.Equal(0, ColorBgra.G(result));
        Assert.Equal(0, ColorBgra.B(result));
    }

    [Fact]
    public void AdditiveSaturatesAt255()
    {
        uint a = ColorBgra.Pack(200, 200, 200, 255);
        uint result = Blend(BlendMode.Additive, a, a);
        Assert.Equal(255, ColorBgra.R(result));
    }

    [Fact]
    public void TransparentSourceLeavesDestinationUnchanged()
    {
        uint dst = ColorBgra.Pack(1, 2, 3, 200);
        foreach (BlendMode mode in Enum.GetValues<BlendMode>())
            Assert.Equal(dst, Blend(mode, dst, 0x00FFFFFF));
    }
}
