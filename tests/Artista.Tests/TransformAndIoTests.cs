using System.IO;
using Artista.Core.Documents;
using Artista.Core.Imaging;
using Artista.Core.IO;
using Artista.Core.Layers;

namespace Artista.Tests;

public class TransformTests
{
    private static Document MakeDoc(int w = 8, int h = 6)
    {
        var doc = new Document(w, h);
        doc.Layers.Add(new Layer(w, h, "Background"));
        return doc;
    }

    [Fact]
    public void ResizeImageChangesDimensionsAndKeepsContent()
    {
        var doc = MakeDoc(8, 8);
        doc.ActiveLayer.Surface.Clear(ColorBgra.Pack(0, 0, 255, 255));
        DocumentTransforms.ResizeImage(doc, 16, 16, ResampleMode.Bilinear);
        Assert.Equal(16, doc.Width);
        Assert.Equal(16, doc.Height);
        Assert.Equal(ColorBgra.Pack(0, 0, 255, 255), doc.ActiveLayer.Surface[8, 8]);
    }

    [Fact]
    public void ResizeCanvasAnchorTopLeftKeepsContentInPlace()
    {
        var doc = MakeDoc(4, 4);
        doc.ActiveLayer.Surface[0, 0] = 0xFF112233u;
        DocumentTransforms.ResizeCanvas(doc, 8, 8, AnchorPosition.TopLeft);
        Assert.Equal(8, doc.Width);
        Assert.Equal(0xFF112233u, doc.ActiveLayer.Surface[0, 0]);
        Assert.Equal(0u, doc.ActiveLayer.Surface[6, 6]);
    }

    [Fact]
    public void ResizeCanvasAnchorCenterCentersContent()
    {
        var doc = MakeDoc(4, 4);
        doc.ActiveLayer.Surface[0, 0] = 0xFF112233u;
        DocumentTransforms.ResizeCanvas(doc, 8, 8, AnchorPosition.MiddleCenter);
        Assert.Equal(0xFF112233u, doc.ActiveLayer.Surface[2, 2]);
    }

    [Fact]
    public void Rotate90SwapsDimensions()
    {
        var doc = MakeDoc(8, 6);
        doc.ActiveLayer.Surface[0, 0] = 0xFFABCDEFu;
        DocumentTransforms.Rotate90(doc, clockwise: true);
        Assert.Equal(6, doc.Width);
        Assert.Equal(8, doc.Height);
        // Top-left goes to top-right on clockwise rotation.
        Assert.Equal(0xFFABCDEFu, doc.ActiveLayer.Surface[5, 0]);
    }

    [Fact]
    public void FlipHorizontalMirrorsPixels()
    {
        var doc = MakeDoc(4, 2);
        doc.ActiveLayer.Surface[0, 0] = 0xFF000001u;
        DocumentTransforms.FlipHorizontal(doc);
        Assert.Equal(0xFF000001u, doc.ActiveLayer.Surface[3, 0]);
        Assert.Equal(0u, doc.ActiveLayer.Surface[0, 0]);
    }

    [Fact]
    public void FlipVerticalMirrorsPixels()
    {
        var doc = MakeDoc(2, 4);
        doc.ActiveLayer.Surface[0, 0] = 0xFF000001u;
        DocumentTransforms.FlipVertical(doc);
        Assert.Equal(0xFF000001u, doc.ActiveLayer.Surface[0, 3]);
    }

    [Fact]
    public void CropReducesCanvasToRect()
    {
        var doc = MakeDoc(10, 10);
        doc.ActiveLayer.Surface[5, 5] = 0xFF445566u;
        DocumentTransforms.CropTo(doc, new RectInt(4, 4, 4, 4), null);
        Assert.Equal(4, doc.Width);
        Assert.Equal(0xFF445566u, doc.ActiveLayer.Surface[1, 1]);
    }

    [Fact]
    public void MergeDownCombinesTwoLayers()
    {
        var doc = MakeDoc(4, 4);
        doc.ActiveLayer.Surface.Clear(ColorBgra.Pack(0, 0, 255, 255));
        var top = new Layer(4, 4, "top");
        top.Surface.FillRect(new RectInt(0, 0, 2, 4), ColorBgra.Pack(255, 0, 0, 255));
        doc.Layers.Add(top);

        DocumentTransforms.MergeDown(doc, 1);
        Assert.Single(doc.Layers);
        Assert.Equal(ColorBgra.Pack(255, 0, 0, 255), doc.ActiveLayer.Surface[0, 0]);
        Assert.Equal(ColorBgra.Pack(0, 0, 255, 255), doc.ActiveLayer.Surface[3, 0]);
    }

    [Fact]
    public void FlattenRespectsHiddenLayers()
    {
        var doc = MakeDoc(2, 2);
        doc.ActiveLayer.Surface.Clear(ColorBgra.Pack(0, 0, 255, 255));
        var hidden = new Layer(2, 2, "hidden") { Visible = false };
        hidden.Surface.Clear(ColorBgra.Pack(255, 0, 0, 255));
        doc.Layers.Add(hidden);

        DocumentTransforms.Flatten(doc);
        Assert.Single(doc.Layers);
        Assert.Equal(ColorBgra.Pack(0, 0, 255, 255), doc.ActiveLayer.Surface[0, 0]);
    }
}

public class FileIoTests
{
    private static string TempPath(string ext) =>
        Path.Combine(Path.GetTempPath(), $"artista-test-{Guid.NewGuid():N}{ext}");

    [Fact]
    public void PngRoundTripPreservesTransparency()
    {
        var surface = new Surface(8, 8);
        surface.FillRect(new RectInt(0, 0, 4, 8), ColorBgra.Pack(10, 20, 30, 255));
        surface.FillRect(new RectInt(4, 0, 4, 8), ColorBgra.Pack(40, 50, 60, 77));
        string path = TempPath(".png");
        try
        {
            ImageCodec.Save(surface, path, ImageFormat.Png);
            var loaded = ImageCodec.Load(path);
            Assert.Equal(surface.Width, loaded.Width);
            Assert.Equal(ColorBgra.Pack(10, 20, 30, 255), loaded[1, 1]);
            Assert.Equal(77, ColorBgra.A(loaded[6, 1]));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BmpRoundTripFlattensOverWhite()
    {
        var surface = new Surface(4, 4); // fully transparent
        string path = TempPath(".bmp");
        try
        {
            ImageCodec.Save(surface, path, ImageFormat.Bmp);
            var loaded = ImageCodec.Load(path);
            Assert.Equal(255, ColorBgra.R(loaded[0, 0]));
            Assert.Equal(255, ColorBgra.A(loaded[0, 0]));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void JpegRoundTripKeepsApproximateColor()
    {
        var surface = new Surface(16, 16);
        surface.Clear(ColorBgra.Pack(30, 60, 200, 255));
        string path = TempPath(".jpg");
        try
        {
            ImageCodec.Save(surface, path, ImageFormat.Jpeg);
            var loaded = ImageCodec.Load(path);
            Assert.InRange<int>(ColorBgra.R(loaded[8, 8]), 180, 220);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TiffRoundTripIsLossless()
    {
        var surface = new Surface(8, 8);
        surface.Clear(ColorBgra.Pack(1, 2, 3, 200));
        string path = TempPath(".tiff");
        try
        {
            ImageCodec.Save(surface, path, ImageFormat.Tiff);
            var loaded = ImageCodec.Load(path);
            Assert.Equal(surface[4, 4], loaded[4, 4]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SafeSaveDoesNotDestroyExistingFileOnFailure()
    {
        string path = TempPath(".png");
        try
        {
            File.WriteAllText(path, "precious");
            Assert.ThrowsAny<Exception>(() =>
                SafeSave.Write(path, _ => throw new InvalidOperationException("boom")));
            Assert.Equal("precious", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ArtzRoundTripPreservesFullDocumentStructure()
    {
        var doc = new Document(12, 10);
        var l1 = new Layer(12, 10, "Background");
        l1.Surface.Clear(ColorBgra.Pack(9, 8, 7, 255));
        var l2 = new Layer(12, 10, "Overlay ✓ (unicode)")
        {
            Opacity = 128,
            Visible = false,
            Locked = true,
            AlphaLocked = true,
            BlendMode = Core.Layers.BlendMode.Multiply,
        };
        l2.Surface[3, 3] = ColorBgra.Pack(1, 2, 3, 4);
        doc.Layers.Add(l1);
        doc.Layers.Add(l2);
        doc.ActiveLayerIndex = 1;
        doc.Metadata["author"] = "test";
        doc.Selection.Combine(
            Core.Selections.SelectionRasterizer.RasterizeRectangle(12, 10, 2, 2, 8, 8),
            Core.Selections.SelectionCombineMode.Replace);

        string path = TempPath(".artz");
        try
        {
            ArtzFormat.Save(doc, path);
            var loaded = ArtzFormat.Load(path);

            Assert.Equal(12, loaded.Width);
            Assert.Equal(10, loaded.Height);
            Assert.Equal(2, loaded.Layers.Count);
            Assert.Equal(1, loaded.ActiveLayerIndex);
            Assert.Equal("Overlay ✓ (unicode)", loaded.Layers[1].Name);
            Assert.Equal(128, loaded.Layers[1].Opacity);
            Assert.False(loaded.Layers[1].Visible);
            Assert.True(loaded.Layers[1].Locked);
            Assert.True(loaded.Layers[1].AlphaLocked);
            Assert.Equal(Core.Layers.BlendMode.Multiply, loaded.Layers[1].BlendMode);
            Assert.Equal(ColorBgra.Pack(9, 8, 7, 255), loaded.Layers[0].Surface[0, 0]);
            Assert.Equal(ColorBgra.Pack(1, 2, 3, 4), loaded.Layers[1].Surface[3, 3]);
            Assert.Equal("test", loaded.Metadata["author"]);
            Assert.False(loaded.Selection.IsEmpty);
            Assert.Equal(255, loaded.Selection.MaskAt(5, 5));
            Assert.Equal(0, loaded.Selection.MaskAt(0, 0));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadingMalformedArtzThrowsInvalidData()
    {
        string path = TempPath(".artz");
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
            Assert.ThrowsAny<Exception>(() => ArtzFormat.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadDoesNotLockTheFile()
    {
        var surface = new Surface(4, 4);
        string path = TempPath(".png");
        try
        {
            ImageCodec.Save(surface, path, ImageFormat.Png);
            _ = ImageCodec.Load(path);
            // Should be deletable immediately — no retained handle.
            File.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
