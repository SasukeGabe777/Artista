using Artista.Core.Documents;
using Artista.Core.Imaging;
using Artista.Core.Layers;
using Artista.Core.Selections;

namespace Artista.Core.History;

/// <summary>
/// A reversible unit of document change. Applying a memento restores the state
/// it captured and returns a new memento capturing the state it replaced
/// (Paint.NET's model): undo(m) -> redo-memento, redo(m) -> undo-memento.
/// </summary>
public abstract class HistoryMemento
{
    public string Name { get; }

    protected HistoryMemento(string name) => Name = name;

    /// <summary>Approximate memory retained by this memento, for the history memory budget.</summary>
    public abstract long SizeEstimate { get; }

    /// <summary>Restores captured state to the document and returns the inverse memento.</summary>
    public abstract HistoryMemento Apply(Document doc);
}

/// <summary>Restores a rectangular pixel region of one layer.</summary>
public sealed class SurfaceRegionMemento : HistoryMemento
{
    private readonly int _layerId;
    private readonly RectInt _rect;
    private uint[] _pixels;

    public SurfaceRegionMemento(string name, Layer layer, RectInt rect, uint[]? capturedPixels = null)
        : base(name)
    {
        _layerId = layer.Id;
        _rect = rect.Intersect(layer.Surface.Bounds);
        _pixels = capturedPixels ?? layer.Surface.ExtractRect(_rect);
    }

    public override long SizeEstimate => (long)_pixels.Length * 4 + 64;

    public RectInt Rect => _rect;
    public int LayerId => _layerId;

    public override HistoryMemento Apply(Document doc)
    {
        var layer = doc.FindLayer(_layerId)
            ?? throw new InvalidOperationException("History references a layer that no longer exists.");
        var current = layer.Surface.ExtractRect(_rect);
        layer.Surface.WriteRect(_rect, _pixels);
        var inverse = new SurfaceRegionMemento(Name, layer, _rect, current);
        _pixels = Array.Empty<uint>();
        return inverse;
    }
}

/// <summary>Restores an entire layer surface reference (used by resize/rotate/flip).</summary>
public sealed class SurfaceSwapMemento : HistoryMemento
{
    private readonly int _layerId;
    private Surface _surface;

    public SurfaceSwapMemento(string name, Layer layer, Surface captured)
        : base(name)
    {
        _layerId = layer.Id;
        _surface = captured;
    }

    public override long SizeEstimate => _surface.ByteCount + 64;

    public override HistoryMemento Apply(Document doc)
    {
        var layer = doc.FindLayer(_layerId)
            ?? throw new InvalidOperationException("History references a layer that no longer exists.");
        var current = layer.Surface;
        layer.Surface = _surface;
        var inverse = new SurfaceSwapMemento(Name, layer, current);
        _surface = null!;
        return inverse;
    }
}

/// <summary>Undoes adding a layer (removes it); inverse re-inserts it.</summary>
public sealed class LayerAddedMemento : HistoryMemento
{
    private readonly int _layerId;

    public LayerAddedMemento(string name, Layer layer) : base(name) => _layerId = layer.Id;

    public override long SizeEstimate => 64;

    public override HistoryMemento Apply(Document doc)
    {
        int index = doc.IndexOfLayer(_layerId);
        if (index < 0) throw new InvalidOperationException("Layer not found.");
        var layer = doc.Layers[index];
        doc.Layers.RemoveAt(index);
        doc.ActiveLayerIndex = Math.Clamp(doc.ActiveLayerIndex >= index ? doc.ActiveLayerIndex - 1 : doc.ActiveLayerIndex, 0, Math.Max(0, doc.Layers.Count - 1));
        return new LayerRemovedMemento(Name, layer, index);
    }
}

/// <summary>Undoes removing a layer (re-inserts it); inverse removes it again.</summary>
public sealed class LayerRemovedMemento : HistoryMemento
{
    private Layer _layer;
    private readonly int _index;

    public LayerRemovedMemento(string name, Layer layer, int index) : base(name)
    {
        _layer = layer;
        _index = index;
    }

    public override long SizeEstimate => _layer?.Surface.ByteCount + 128 ?? 128;

    public override HistoryMemento Apply(Document doc)
    {
        doc.Layers.Insert(Math.Clamp(_index, 0, doc.Layers.Count), _layer);
        doc.ActiveLayerIndex = doc.IndexOfLayer(_layer.Id);
        var inverse = new LayerAddedMemento(Name, _layer);
        _layer = null!;
        return inverse;
    }
}

/// <summary>Restores a layer's position in the z-order.</summary>
public sealed class LayerOrderMemento : HistoryMemento
{
    private readonly int _layerId;
    private readonly int _oldIndex;

    public LayerOrderMemento(string name, int layerId, int oldIndex) : base(name)
    {
        _layerId = layerId;
        _oldIndex = oldIndex;
    }

    public override long SizeEstimate => 64;

    public override HistoryMemento Apply(Document doc)
    {
        int current = doc.IndexOfLayer(_layerId);
        if (current < 0) throw new InvalidOperationException("Layer not found.");
        var layer = doc.Layers[current];
        doc.Layers.RemoveAt(current);
        doc.Layers.Insert(Math.Clamp(_oldIndex, 0, doc.Layers.Count), layer);
        doc.ActiveLayerIndex = doc.IndexOfLayer(_layerId);
        return new LayerOrderMemento(Name, _layerId, current);
    }
}

/// <summary>Restores layer properties (name, visibility, opacity, blend mode, locks).</summary>
public sealed class LayerPropertiesMemento : HistoryMemento
{
    private readonly int _layerId;
    private readonly LayerProperties _properties;

    public LayerPropertiesMemento(string name, Layer layer) : base(name)
    {
        _layerId = layer.Id;
        _properties = layer.GetProperties();
    }

    public override long SizeEstimate => 128;

    public override HistoryMemento Apply(Document doc)
    {
        var layer = doc.FindLayer(_layerId)
            ?? throw new InvalidOperationException("Layer not found.");
        var inverse = new LayerPropertiesMemento(Name, layer);
        layer.SetProperties(_properties);
        return inverse;
    }
}

/// <summary>Restores the selection mask.</summary>
public sealed class SelectionMemento : HistoryMemento
{
    private byte[] _mask;

    public SelectionMemento(string name, Selection selection) : base(name) =>
        _mask = selection.SnapshotMask();

    public SelectionMemento(string name, byte[] capturedMask) : base(name) => _mask = capturedMask;

    public override long SizeEstimate => _mask.LongLength + 64;

    public override HistoryMemento Apply(Document doc)
    {
        var current = doc.Selection.SnapshotMask();
        doc.Selection.RestoreMask(_mask);
        var inverse = new SelectionMemento(Name, current);
        _mask = Array.Empty<byte>();
        return inverse;
    }
}

/// <summary>Restores the persistent off-canvas pasteboard pieces.</summary>
public sealed class PasteboardStateMemento : HistoryMemento
{
    private List<PasteboardItem> _items;

    public PasteboardStateMemento(string name, Document doc) : this(name, doc.PasteboardItems)
    {
    }

    public PasteboardStateMemento(string name, IEnumerable<PasteboardItem> items) : base(name) =>
        _items = items.ToList();

    public override long SizeEstimate => _items.Sum(i => i.Surface.ByteCount + 96);

    public override HistoryMemento Apply(Document doc)
    {
        var inverse = new PasteboardStateMemento(Name, doc);
        doc.PasteboardItems.Clear();
        doc.PasteboardItems.AddRange(_items);
        _items = new List<PasteboardItem>();
        return inverse;
    }
}

/// <summary>Restores the active layer index.</summary>
public sealed class ActiveLayerMemento : HistoryMemento
{
    private readonly int _index;

    public ActiveLayerMemento(string name, int index) : base(name) => _index = index;

    public override long SizeEstimate => 32;

    public override HistoryMemento Apply(Document doc)
    {
        var inverse = new ActiveLayerMemento(Name, doc.ActiveLayerIndex);
        doc.ActiveLayerIndex = Math.Clamp(_index, 0, Math.Max(0, doc.Layers.Count - 1));
        return inverse;
    }
}

/// <summary>
/// Restores whole-document structure: dimensions, the layer list, each layer's
/// surface reference and properties, and the selection. Used for canvas
/// resize, rotate, flip, flatten, crop and merge.
///
/// It captures the same Layer objects (so their ids stay valid for other
/// history entries) together with their current Surface references — document
/// transforms must therefore REPLACE layer surfaces, never mutate them in
/// place.
/// </summary>
public sealed class DocumentStructureMemento : HistoryMemento
{
    private sealed record Entry(Layer Layer, Surface Surface, LayerProperties Properties);

    private readonly int _width;
    private readonly int _height;
    private List<Entry>? _entries;
    private byte[]? _selectionMask;
    private readonly int _activeIndex;

    public DocumentStructureMemento(string name, Document doc) : base(name)
    {
        _width = doc.Width;
        _height = doc.Height;
        _entries = doc.Layers.Select(l => new Entry(l, l.Surface, l.GetProperties())).ToList();
        _selectionMask = doc.Selection.SnapshotMask();
        _activeIndex = doc.ActiveLayerIndex;
    }

    public override long SizeEstimate =>
        (_entries?.Sum(e => e.Surface.ByteCount) ?? 0) + (_selectionMask?.LongLength ?? 0) + 256;

    public override HistoryMemento Apply(Document doc)
    {
        var inverse = new DocumentStructureMemento(Name, doc);
        foreach (var entry in _entries!)
        {
            entry.Layer.Surface = entry.Surface;
            entry.Layer.SetProperties(entry.Properties);
        }
        doc.SetCanvasRaw(_width, _height, _entries.Select(e => e.Layer).ToList(), _selectionMask!, _activeIndex);
        _entries = null;
        _selectionMask = null;
        return inverse;
    }
}

/// <summary>Groups several mementos into one named history step.</summary>
public sealed class CompositeMemento : HistoryMemento
{
    private readonly List<HistoryMemento> _children;

    public CompositeMemento(string name, IEnumerable<HistoryMemento> children) : base(name) =>
        _children = children.ToList();

    public override long SizeEstimate => _children.Sum(c => c.SizeEstimate) + 64;

    public override HistoryMemento Apply(Document doc)
    {
        // Apply in reverse order; inverse plays back forward.
        var inverses = new List<HistoryMemento>(_children.Count);
        for (int i = _children.Count - 1; i >= 0; i--)
            inverses.Add(_children[i].Apply(doc));
        inverses.Reverse();
        return new CompositeMemento(Name, inverses);
    }
}
