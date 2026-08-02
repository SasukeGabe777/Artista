using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Artista.Core.Documents;
using Artista.Core.History;
using Artista.Core.Imaging;
using Artista.Core.Rendering;

namespace Artista.App.Models;

/// <summary>
/// Per-document UI state: the core document, its history stack, the cached
/// composite bitmap shown on the canvas, the file path / dirty flag, and the
/// remembered view (zoom + scroll) so switching tabs preserves it.
/// </summary>
public sealed class DocumentWorkspace
{
    public Document Document { get; }
    public HistoryStack History { get; }

    public string? FilePath { get; set; }
    public bool IsDirty { get; private set; }

    /// <summary>History count at last save, to clear dirty on undo back to saved state.</summary>
    private int _savedHistoryMark;

    public double ZoomFactor { get; set; } = 1.0;
    public double ScrollX { get; set; }
    public double ScrollY { get; set; }

    /// <summary>Composite of all visible layers, kept in sync with edits.</summary>
    public Surface CompositeSurface { get; private set; }

    public WriteableBitmap CompositeBitmap { get; private set; }

    /// <summary>Optional preview substitution: this surface replaces the layer's during compositing.</summary>
    public int PreviewLayerId { get; private set; } = -1;
    public Surface? PreviewSurface { get; private set; }

    public event EventHandler<RectInt>? CompositeUpdated;
    public event EventHandler? StructureChanged; // layers list, active layer, size
    public event EventHandler? DirtyChanged;

    public string DisplayName =>
        (FilePath is null ? "Untitled" : System.IO.Path.GetFileName(FilePath)) + (IsDirty ? " *" : "");

    public DocumentWorkspace(Document document)
    {
        Document = document;
        History = new HistoryStack(document);
        History.Changed += (_, _) => UpdateDirtyFromHistory();
        CompositeSurface = new Surface(document.Width, document.Height);
        CompositeBitmap = new WriteableBitmap(document.Width, document.Height, 96, 96, PixelFormats.Bgra32, null);
        InvalidateComposite(document.Bounds);
    }

    public void MarkDirty()
    {
        if (!IsDirty)
        {
            IsDirty = true;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MarkSaved()
    {
        _savedHistoryMark = History.UndoEntries.Count;
        if (IsDirty)
        {
            IsDirty = false;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateDirtyFromHistory()
    {
        bool dirty = History.UndoEntries.Count != _savedHistoryMark;
        if (dirty != IsDirty)
        {
            IsDirty = dirty;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetPreview(int layerId, Surface? surface)
    {
        PreviewLayerId = surface == null ? -1 : layerId;
        PreviewSurface = surface;
    }

    /// <summary>
    /// Recomposites the given region and pushes it into the WriteableBitmap.
    /// Must be called on the UI thread (WriteableBitmap requirement).
    /// </summary>
    public void InvalidateComposite(RectInt roi)
    {
        var r = roi.Intersect(Document.Bounds);
        if (r.IsEmpty) return;

        // Document size changed (resize/rotate/crop): rebuild buffers.
        if (CompositeSurface.Width != Document.Width || CompositeSurface.Height != Document.Height)
        {
            CompositeSurface = new Surface(Document.Width, Document.Height);
            CompositeBitmap = new WriteableBitmap(Document.Width, Document.Height, 96, 96, PixelFormats.Bgra32, null);
            r = Document.Bounds;
            StructureChanged?.Invoke(this, EventArgs.Empty);
        }

        Compositor.Composite(Document, CompositeSurface, r, PreviewLayerId, PreviewSurface);

        CompositeBitmap.Lock();
        try
        {
            unsafe
            {
                var backBuffer = (byte*)CompositeBitmap.BackBuffer;
                int stride = CompositeBitmap.BackBufferStride;
                for (int y = r.Top; y < r.Bottom; y++)
                {
                    fixed (uint* src = &CompositeSurface.Pixels[y * CompositeSurface.Width + r.Left])
                    {
                        Buffer.MemoryCopy(src, backBuffer + (long)y * stride + r.Left * 4,
                            r.Width * 4L, r.Width * 4L);
                    }
                }
            }
            CompositeBitmap.AddDirtyRect(new Int32Rect(r.Left, r.Top, r.Width, r.Height));
        }
        finally
        {
            CompositeBitmap.Unlock();
        }
        CompositeUpdated?.Invoke(this, r);
    }

    public void NotifyStructureChanged()
    {
        StructureChanged?.Invoke(this, EventArgs.Empty);
        InvalidateComposite(Document.Bounds);
    }
}
