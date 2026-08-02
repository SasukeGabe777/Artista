using Artista.Core.Imaging;

namespace Artista.Core.Layers;

/// <summary>A single raster layer in a document.</summary>
public sealed class Layer
{
    private static int _nextId;

    /// <summary>Stable identity used by history mementos to survive reordering.</summary>
    public int Id { get; } = Interlocked.Increment(ref _nextId);

    public Surface Surface { get; set; }
    public string Name { get; set; }
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public bool AlphaLocked { get; set; }

    /// <summary>0-255.</summary>
    public byte Opacity { get; set; } = 255;

    public BlendMode BlendMode { get; set; } = BlendMode.Normal;

    public Layer(Surface surface, string name)
    {
        Surface = surface;
        Name = name;
    }

    public Layer(int width, int height, string name)
        : this(new Surface(width, height), name)
    {
    }

    public bool IsEditable => Visible && !Locked;

    public Layer Clone(string? newName = null) => new(Surface.Clone(), newName ?? Name)
    {
        Visible = Visible,
        Locked = Locked,
        AlphaLocked = AlphaLocked,
        Opacity = Opacity,
        BlendMode = BlendMode,
    };

    /// <summary>Snapshot of layer properties (not pixels) for history.</summary>
    public LayerProperties GetProperties() => new(Name, Visible, Locked, AlphaLocked, Opacity, BlendMode);

    public void SetProperties(LayerProperties p)
    {
        Name = p.Name;
        Visible = p.Visible;
        Locked = p.Locked;
        AlphaLocked = p.AlphaLocked;
        Opacity = p.Opacity;
        BlendMode = p.BlendMode;
    }
}

public readonly record struct LayerProperties(
    string Name, bool Visible, bool Locked, bool AlphaLocked, byte Opacity, BlendMode BlendMode);
