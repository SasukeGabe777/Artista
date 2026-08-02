using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Artista.Core.Documents;
using Artista.Core.Layers;

namespace Artista.Core.IO;

/// <summary>
/// Artista's native layered project format (.artz).
///
/// A .artz file is a ZIP archive containing:
///   document.json   — canvas size, layer metadata, active layer, app metadata
///   layers/N.png    — each layer's pixels as PNG (bottom layer first)
///   selection.png   — optional: the selection mask as an 8-bit grayscale PNG
///
/// See docs/PROJECT_FORMAT.md for the full specification.
/// </summary>
public static class ArtzFormat
{
    public const string Extension = ".artz";
    private const int CurrentVersion = 1;

    private sealed record LayerInfo(
        string Name, bool Visible, bool Locked, bool AlphaLocked, byte Opacity, string BlendMode);

    private sealed record DocumentInfo(
        int Version, int Width, int Height, int ActiveLayerIndex,
        List<LayerInfo> Layers, Dictionary<string, string>? Metadata, bool HasSelection);

    public static void Save(Document doc, string path)
    {
        SafeSave.Write(path, stream =>
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            var info = new DocumentInfo(
                CurrentVersion, doc.Width, doc.Height, doc.ActiveLayerIndex,
                doc.Layers.Select(l => new LayerInfo(
                    l.Name, l.Visible, l.Locked, l.AlphaLocked, l.Opacity, l.BlendMode.ToString())).ToList(),
                doc.Metadata.Count > 0 ? doc.Metadata : null,
                !doc.Selection.IsEmpty);

            var jsonEntry = zip.CreateEntry("document.json");
            using (var js = jsonEntry.Open())
                JsonSerializer.Serialize(js, info, new JsonSerializerOptions { WriteIndented = true });

            // WIC encoders need a seekable stream, which zip entry streams are
            // not — encode to memory first.
            for (int i = 0; i < doc.Layers.Count; i++)
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
                    ImageCodec.ToBitmapSource(doc.Layers[i].Surface)));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;
                var entry = zip.CreateEntry($"layers/{i}.png");
                using var es = entry.Open();
                ms.CopyTo(es);
            }

            if (!doc.Selection.IsEmpty)
            {
                var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                    doc.Width, doc.Height, 96, 96, System.Windows.Media.PixelFormats.Gray8, null,
                    doc.Selection.Mask, doc.Width);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;
                var entry = zip.CreateEntry("selection.png");
                using var es = entry.Open();
                ms.CopyTo(es);
            }
        });
    }

    public static Document Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var jsonEntry = zip.GetEntry("document.json")
            ?? throw new InvalidDataException("Not a valid Artista project: document.json missing.");
        DocumentInfo info;
        using (var js = jsonEntry.Open())
            info = JsonSerializer.Deserialize<DocumentInfo>(js)
                ?? throw new InvalidDataException("Not a valid Artista project: document.json unreadable.");

        if (info.Width <= 0 || info.Height <= 0)
            throw new InvalidDataException("Invalid canvas dimensions in project file.");

        var doc = new Document(info.Width, info.Height);
        for (int i = 0; i < info.Layers.Count; i++)
        {
            var entry = zip.GetEntry($"layers/{i}.png")
                ?? throw new InvalidDataException($"Project is missing pixel data for layer {i}.");
            using var es = entry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            ms.Position = 0;
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                ms, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var surface = ImageCodec.FromBitmapSource(decoder.Frames[0]);
            if (surface.Width != info.Width || surface.Height != info.Height)
                throw new InvalidDataException($"Layer {i} size does not match the canvas.");

            var li = info.Layers[i];
            var layer = new Layer(surface, li.Name)
            {
                Visible = li.Visible,
                Locked = li.Locked,
                AlphaLocked = li.AlphaLocked,
                Opacity = li.Opacity,
                BlendMode = Enum.TryParse<BlendMode>(li.BlendMode, out var bm) ? bm : BlendMode.Normal,
            };
            doc.Layers.Add(layer);
        }
        if (doc.Layers.Count == 0)
            doc.Layers.Add(new Layer(info.Width, info.Height, "Background"));
        doc.ActiveLayerIndex = Math.Clamp(info.ActiveLayerIndex, 0, doc.Layers.Count - 1);

        if (info.Metadata != null)
            foreach (var (k, v) in info.Metadata)
                doc.Metadata[k] = v;

        var selEntry = zip.GetEntry("selection.png");
        if (selEntry != null && info.HasSelection)
        {
            using var es = selEntry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            ms.Position = 0;
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                ms, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth == info.Width && frame.PixelHeight == info.Height)
            {
                var gray = frame.Format == System.Windows.Media.PixelFormats.Gray8
                    ? (System.Windows.Media.Imaging.BitmapSource)frame
                    : new System.Windows.Media.Imaging.FormatConvertedBitmap(
                        frame, System.Windows.Media.PixelFormats.Gray8, null, 0);
                gray.CopyPixels(new System.Windows.Int32Rect(0, 0, info.Width, info.Height),
                    doc.Selection.Mask, info.Width, 0);
                doc.Selection.MarkChanged();
            }
        }
        return doc;
    }
}
