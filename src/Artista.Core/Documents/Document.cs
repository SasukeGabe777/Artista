using Artista.Core.Imaging;
using Artista.Core.Layers;
using Artista.Core.Selections;

namespace Artista.Core.Documents;

/// <summary>
/// The core document model: canvas dimensions, an ordered list of layers
/// (index 0 = bottom), the active layer, and the current selection.
/// Pure model — no UI dependencies. Mutations that should be undoable are
/// performed through history mementos at the application layer.
/// </summary>
public sealed class Document
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public List<Layer> Layers { get; } = new();
    public int ActiveLayerIndex { get; set; }
    public Selection Selection { get; set; }
    public Dictionary<string, string> Metadata { get; } = new();
    public List<PasteboardItem> PasteboardItems { get; } = new();

    public RectInt Bounds => new(0, 0, Width, Height);

    public Document(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 32768 || height > 32768)
            throw new ArgumentOutOfRangeException(nameof(width), $"Invalid document size {width}x{height}.");
        Width = width;
        Height = height;
        Selection = new Selection(width, height);
    }

    public Layer ActiveLayer => Layers[Math.Clamp(ActiveLayerIndex, 0, Layers.Count - 1)];

    public Layer? FindLayer(int id) => Layers.FirstOrDefault(l => l.Id == id);

    public int IndexOfLayer(int id) => Layers.FindIndex(l => l.Id == id);

    /// <summary>Composites all visible layers over transparency into a new surface.</summary>
    public Surface Flatten()
    {
        var result = new Surface(Width, Height);
        Rendering.Compositor.Composite(this, result, Bounds);
        return result;
    }

    /// <summary>Changes the canvas size in place; each layer surface is replaced.</summary>
    public void SetCanvas(int newWidth, int newHeight, Func<Layer, Surface> surfaceFactory)
    {
        Width = newWidth;
        Height = newHeight;
        foreach (var layer in Layers)
            layer.Surface = surfaceFactory(layer);
        Selection = new Selection(newWidth, newHeight);
    }

    /// <summary>Replaces the whole document structure (used by history restore).</summary>
    public void SetCanvasRaw(int width, int height, List<Layer> layers, byte[]? selectionMask, int activeIndex)
    {
        Width = width;
        Height = height;
        Layers.Clear();
        Layers.AddRange(layers);
        Selection = new Selection(width, height);
        if (selectionMask != null && selectionMask.Length == Selection.Mask.Length)
            Selection.RestoreMask(selectionMask);
        ActiveLayerIndex = Math.Clamp(activeIndex, 0, Math.Max(0, Layers.Count - 1));
    }
}
