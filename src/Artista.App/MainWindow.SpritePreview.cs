using System.Windows;
using Artista.App.Dialogs;
using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.App;

public sealed partial class MainWindow
{
    private readonly List<SpritePreviewWindow> _spritePreviewWindows = new();
    internal SpritePreviewWindow? LastSpritePreviewWindow { get; private set; }

    public void OpenSpritePreview()
    {
        if (_active == null) return;
        bool useSelectedGrid = IsSpriteGridActive &&
            _active.Document.PasteboardItems.Count == 0 &&
            !_active.Document.Selection.IsEmpty;
        SpriteGridLayout? grid = useSelectedGrid
            ? SpriteGridLayout
            : null;
        var frames = CollectSpriteFrames(_active, grid);
        if (frames.Count == 1 && !useSelectedGrid)
        {
            var sheet = frames[0];
            var layout = new SpriteSheetLayoutDialog(sheet.Surface.Width, sheet.Surface.Height, sheet.Name)
            {
                Owner = this,
            };
            if (layout.ShowDialog() != true)
            {
                SetStatus("Sprite Preview canceled. The selected sprite sheet was not changed.");
                return;
            }
            frames = SliceSpriteSheet(sheet.Surface, layout.Columns, layout.Rows, sheet.Name);
        }
        if (frames.Count < 2)
        {
            const string message = "Select two or more Sprite Grid cells, select a sprite strip, Ctrl-select frame regions, or park sprite sheets beside the canvas first.";
            SetStatus(message);
            MessageBox.Show(this, message, "Sprite Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new SpritePreviewWindow(frames, $"Sprite Canvas - {_active.DisplayName}")
        {
            Owner = this,
        };
        _spritePreviewWindows.Add(window);
        LastSpritePreviewWindow = window;
        window.Closed += (_, _) =>
        {
            _spritePreviewWindows.Remove(window);
            if (ReferenceEquals(LastSpritePreviewWindow, window))
                LastSpritePreviewWindow = _spritePreviewWindows.LastOrDefault();
        };
        window.Show();
        SetStatus($"Opened Sprite Canvas with {frames.Count} frames. Space plays/pauses; arrows step frames.");
    }

    private static List<SpriteFrameData> CollectSpriteFrames(
        Models.DocumentWorkspace workspace, SpriteGridLayout? spriteGrid = null)
    {
        var doc = workspace.Document;

        // The pasteboard is the most natural staging area for animation: park
        // each sprite frame beside the sheet, arrange them in reading order,
        // then invoke Sprite Preview.
        if (doc.PasteboardItems.Count >= 2)
        {
            return doc.PasteboardItems
                .OrderBy(item => item.Y)
                .ThenBy(item => item.X)
                .Select((item, i) => new SpriteFrameData(item.Surface.Clone(), 100,
                    string.IsNullOrWhiteSpace(item.Name) ? $"Frame {i + 1}" : item.Name))
                .ToList();
        }

        // Moving an entire sprite strip off-canvas produces one pasteboard
        // item. Treat that item as a sheet instead of requiring the user to
        // park every frame separately.
        if (doc.PasteboardItems.Count == 1)
        {
            var item = doc.PasteboardItems[0];
            var automatic = TrySplitSquareStrip(item.Surface,
                string.IsNullOrWhiteSpace(item.Name) ? "Pasteboard sprite sheet" : item.Name);
            return automatic ?? new List<SpriteFrameData>
            {
                new(item.Surface.Clone(), 100,
                    string.IsNullOrWhiteSpace(item.Name) ? "Pasteboard sprite sheet" : item.Name),
            };
        }

        if (doc.Selection.IsEmpty) return new List<SpriteFrameData>();

        // With the guide active, selection is cell-based rather than
        // component-based. Adjacent selected cells therefore remain distinct
        // animation frames even though their selection masks touch.
        if (spriteGrid is { IsValid: true } layout)
            return ExtractSelectedGridFrames(workspace.CompositeSurface, doc.Selection, layout);

        var regions = FindSelectionComponents(doc.Selection);

        // A single horizontal/vertical strip of square frames is common enough
        // to split automatically. Non-square or irregular sheets can be built
        // by Ctrl-adding separate rectangle selections instead.
        if (regions.Count == 1)
        {
            var bounds = regions[0];
            var sheet = ExtractSelectedFrame(workspace.CompositeSurface, doc.Selection, bounds);
            return TrySplitSquareStrip(sheet, "Selected sprite sheet") ?? new List<SpriteFrameData>
            {
                new(sheet, 100, "Selected sprite sheet"),
            };
        }

        return regions
            .OrderBy(r => r.Top)
            .ThenBy(r => r.Left)
            .Select((rect, i) => new SpriteFrameData(
                ExtractSelectedFrame(workspace.CompositeSurface, doc.Selection, rect),
                100, $"Frame {i + 1}"))
            .ToList();
    }

    private static List<SpriteFrameData> ExtractSelectedGridFrames(
        Surface composite, Selection selection, SpriteGridLayout layout)
    {
        var frames = new List<SpriteFrameData>();
        foreach (var cell in layout.EnumerateCompleteCells(composite.Width, composite.Height))
        {
            int selectedPixels = 0;
            for (int y = cell.Top; y < cell.Bottom; y++)
            {
                int row = y * selection.Width;
                for (int x = cell.Left; x < cell.Right; x++)
                    if (selection.Mask[row + x] >= 128)
                        selectedPixels++;
            }
            if (selectedPixels * 2L < cell.Area) continue;

            frames.Add(new SpriteFrameData(
                ExtractSurfaceRect(composite, cell),
                100,
                $"Grid frame {frames.Count + 1}"));
        }
        return frames;
    }

    private static Surface ExtractSurfaceRect(Surface source, RectInt rect)
    {
        var result = new Surface(rect.Width, rect.Height);
        for (int y = 0; y < rect.Height; y++)
            source.GetRowSpan(rect.Top + y, rect.Left, rect.Width).CopyTo(result.GetRow(y));
        return result;
    }

    private static List<SpriteFrameData>? TrySplitSquareStrip(Surface sheet, string name)
    {
        if (sheet.Width > sheet.Height && sheet.Width % sheet.Height == 0)
        {
            int count = sheet.Width / sheet.Height;
            if (count is >= 2 and <= 128)
                return SliceSpriteSheet(sheet, count, 1, name);
        }
        else if (sheet.Height > sheet.Width && sheet.Height % sheet.Width == 0)
        {
            int count = sheet.Height / sheet.Width;
            if (count is >= 2 and <= 128)
                return SliceSpriteSheet(sheet, 1, count, name);
        }
        return null;
    }

    internal static List<SpriteFrameData> SliceSpriteSheet(
        Surface sheet, int columns, int rows, string name = "Sprite sheet")
    {
        if (columns < 1 || rows < 1 || columns * rows > 128 ||
            sheet.Width % columns != 0 || sheet.Height % rows != 0)
            throw new ArgumentOutOfRangeException(nameof(columns), "The frame grid must divide the sprite sheet evenly.");

        int frameWidth = sheet.Width / columns;
        int frameHeight = sheet.Height / rows;
        var frames = new List<SpriteFrameData>(columns * rows);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int number = row * columns + column + 1;
                var frame = new Surface(frameWidth, frameHeight);
                for (int y = 0; y < frameHeight; y++)
                    sheet.GetRowSpan(row * frameHeight + y, column * frameWidth, frameWidth)
                        .CopyTo(frame.GetRow(y));
                frames.Add(new SpriteFrameData(
                    frame,
                    100, $"{name} - Frame {number}"));
            }
        }
        return frames;
    }

    private static List<RectInt> FindSelectionComponents(Selection selection)
    {
        int width = selection.Width, height = selection.Height;
        var visited = new bool[selection.Mask.Length];
        var result = new List<RectInt>();
        var queue = new Queue<(int X, int Y)>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int startIndex = y * width + x;
                if (visited[startIndex] || selection.Mask[startIndex] == 0) continue;
                visited[startIndex] = true;
                queue.Enqueue((x, y));
                int left = x, right = x, top = y, bottom = y;
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    left = Math.Min(left, cx); right = Math.Max(right, cx);
                    top = Math.Min(top, cy); bottom = Math.Max(bottom, cy);
                    Visit(cx - 1, cy);
                    Visit(cx + 1, cy);
                    Visit(cx, cy - 1);
                    Visit(cx, cy + 1);
                }
                result.Add(RectInt.FromLTRB(left, top, right + 1, bottom + 1));

                void Visit(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) return;
                    int index = ny * width + nx;
                    if (visited[index] || selection.Mask[index] == 0) return;
                    visited[index] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }
        return result;
    }

    private static Surface ExtractSelectedFrame(Surface composite, Selection selection, RectInt rect)
    {
        var clipped = rect.Intersect(composite.Bounds);
        var frame = new Surface(Math.Max(1, clipped.Width), Math.Max(1, clipped.Height));
        for (int y = 0; y < clipped.Height; y++)
        {
            var source = composite.GetRowSpan(clipped.Top + y, clipped.Left, clipped.Width);
            var destination = frame.GetRow(y);
            for (int x = 0; x < clipped.Width; x++)
            {
                uint color = source[x];
                byte coverage = selection.MaskAt(clipped.Left + x, clipped.Top + y);
                destination[x] = coverage == 255
                    ? color
                    : ColorBgra.WithAlpha(color, (byte)(ColorBgra.A(color) * coverage / 255));
            }
        }
        return frame;
    }
}
