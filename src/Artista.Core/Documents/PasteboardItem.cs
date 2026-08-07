using Artista.Core.Imaging;

namespace Artista.Core.Documents;

/// <summary>
/// A reusable pixel piece parked outside the document canvas. Pasteboard items
/// are saved with .artz projects but are deliberately excluded from flattening
/// and image export until they are placed back onto a layer.
/// </summary>
public sealed class PasteboardItem
{
    private static int _nextId;

    public int Id { get; }
    public string Name { get; }
    public Surface Surface { get; }
    public int X { get; }
    public int Y { get; }

    public RectInt Bounds => new(X, Y, Surface.Width, Surface.Height);

    public PasteboardItem(Surface surface, int x, int y, string name = "Pasteboard item")
        : this(Interlocked.Increment(ref _nextId), surface, x, y, name)
    {
    }

    internal PasteboardItem(int id, Surface surface, int x, int y, string name)
    {
        Id = id;
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        X = x;
        Y = y;
        Name = string.IsNullOrWhiteSpace(name) ? "Pasteboard item" : name;
        AdvanceNextId(id);
    }

    public PasteboardItem MovedTo(int x, int y) => new(Id, Surface, x, y, Name);

    private static void AdvanceNextId(int id)
    {
        int current;
        do
        {
            current = _nextId;
            if (current >= id) return;
        }
        while (Interlocked.CompareExchange(ref _nextId, id, current) != current);
    }
}
