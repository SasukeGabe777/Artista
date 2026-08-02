using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Artista.Core.Imaging;

namespace Artista.Core.IO;

public enum ImageFormat
{
    Png,
    Jpeg,
    Bmp,
    Gif,
    Tiff,
    WebP,
}

/// <summary>
/// Loads and saves flat raster images through WIC (WPF's BitmapDecoder /
/// BitmapEncoder). WebP decodes when the OS codec is present (standard on
/// Windows 11); WebP encoding is not supported by WIC and is documented as a
/// limitation.
/// </summary>
public static class ImageCodec
{
    public static readonly string OpenFilter =
        "All images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.artz|" +
        "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|BMP (*.bmp)|*.bmp|" +
        "GIF (*.gif)|*.gif|TIFF (*.tif;*.tiff)|*.tif;*.tiff|WebP (*.webp)|*.webp|" +
        "Artista Project (*.artz)|*.artz|All files (*.*)|*.*";

    public static readonly string SaveFilter =
        "Artista Project (*.artz)|*.artz|PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|" +
        "BMP (*.bmp)|*.bmp|GIF (*.gif)|*.gif|TIFF (*.tiff)|*.tiff";

    public static ImageFormat? FormatFromExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => ImageFormat.Png,
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".bmp" => ImageFormat.Bmp,
            ".gif" => ImageFormat.Gif,
            ".tif" or ".tiff" => ImageFormat.Tiff,
            ".webp" => ImageFormat.WebP,
            _ => null,
        };

    /// <summary>Loads any WIC-decodable image into a Surface. Does not keep the file locked.</summary>
    public static Surface Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("The file contains no image frames.");
        return FromBitmapSource(decoder.Frames[0]);
    }

    public static Surface FromBitmapSource(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth, h = converted.PixelHeight;
        var surface = new Surface(w, h);
        converted.CopyPixels(new System.Windows.Int32Rect(0, 0, w, h), surface.Pixels, w * 4, 0);
        return surface;
    }

    public static BitmapSource ToBitmapSource(Surface surface)
    {
        var bmp = BitmapSource.Create(
            surface.Width, surface.Height, 96, 96, PixelFormats.Bgra32, null,
            surface.Pixels, surface.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Saves a surface to disk. Writes to a temporary file first and swaps it
    /// into place so a failed save never destroys the existing file.
    /// Formats without full alpha are composited over white.
    /// </summary>
    public static void Save(Surface surface, string path, ImageFormat format, int jpegQuality = 92)
    {
        BitmapEncoder encoder = format switch
        {
            ImageFormat.Png => new PngBitmapEncoder(),
            ImageFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) },
            ImageFormat.Bmp => new BmpBitmapEncoder(),
            ImageFormat.Gif => new GifBitmapEncoder(),
            ImageFormat.Tiff => new TiffBitmapEncoder(),
            ImageFormat.WebP => throw new NotSupportedException(
                "WebP encoding is not supported by the Windows imaging stack. Save as PNG instead."),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        Surface toSave = surface;
        if (format is ImageFormat.Jpeg or ImageFormat.Bmp)
        {
            // No alpha support: composite over white.
            toSave = new Surface(surface.Width, surface.Height);
            Parallel.For(0, surface.Height, y =>
            {
                var src = surface.GetRow(y);
                var dst = toSave.GetRow(y);
                for (int x = 0; x < src.Length; x++)
                    dst[x] = ColorBgra.OverOpaque(0xFFFFFFFFu, src[x]);
            });
        }

        encoder.Frames.Add(BitmapFrame.Create(ToBitmapSource(toSave)));
        SafeSave.Write(path, stream => encoder.Save(stream));
    }
}

/// <summary>Write-to-temp-then-replace helper shared by all savers.</summary>
public static class SafeSave
{
    public static void Write(string path, Action<Stream> writer)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                writer(stream);
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* best effort cleanup */ }
            throw;
        }
    }
}
