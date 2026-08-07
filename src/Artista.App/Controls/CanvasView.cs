using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Artista.App.Models;
using Artista.App.Tools;
using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.App.Controls;

/// <summary>
/// The drawing surface: renders the checkerboard, the composite bitmap at the
/// current zoom/pan, the pixel grid, the animated selection outline, and the
/// active tool's preview overlay.
/// </summary>
public sealed class CanvasView : FrameworkElement
{
    public DocumentWorkspace? Workspace { get; private set; }
    public ToolBase? ActiveTool { get; set; }

    public double Zoom { get; set; } = 1.0;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public bool ShowPixelGrid { get; set; } = true;
    public bool ShowSpriteGrid { get; set; }
    public int SpriteGridCellWidth { get; set; } = 32;
    public int SpriteGridCellHeight { get; set; } = 32;

    private readonly DispatcherTimer _antsTimer;
    private double _antsOffset;

    // Cached selection outline geometry (rebuilt when the selection version changes).
    private StreamGeometry? _selectionGeometry;
    private int _selectionGeometryVersion = -1;

    public CanvasTransform Transform => new(Zoom, OffsetX, OffsetY);

    public CanvasView()
    {
        SnapsToDevicePixels = true;
        // The document is drawn at arbitrary zoom/offset and must never paint
        // outside the canvas area (over toolbars, tabs or rulers).
        ClipToBounds = true;
        _antsTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _antsTimer.Tick += (_, _) =>
        {
            _antsOffset = (_antsOffset + 1) % 8;
            if (Workspace != null && !Workspace.Document.Selection.IsEmpty)
                InvalidateVisual();
        };
        _antsTimer.Start();
        Focusable = true;
        FocusVisualStyle = null;
    }

    public void SetWorkspace(DocumentWorkspace? workspace)
    {
        Workspace = workspace;
        _selectionGeometryVersion = -1;
        _selectionGeometry = null;
        InvalidateVisual();
    }

    public Point ViewToDoc(Point view) => new((view.X - OffsetX) / Zoom, (view.Y - OffsetY) / Zoom);
    public Point DocToView(Point doc) => new(doc.X * Zoom + OffsetX, doc.Y * Zoom + OffsetY);

    protected override void OnRender(DrawingContext dc)
    {
        // Canvas surround.
        var surround = TryFindResource("CanvasSurroundBrush") as Brush ?? Brushes.Gray;
        dc.DrawRectangle(surround, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var ws = Workspace;
        if (ws == null) return;

        int docW = ws.Document.Width, docH = ws.Document.Height;
        var docRectView = new Rect(OffsetX, OffsetY, docW * Zoom, docH * Zoom);

        // Reusable pieces live on the gray pasteboard behind the artboard.
        // Drawing them first keeps the document boundary visually unambiguous:
        // a parked item does not become part of the image until it is picked up
        // and placed back onto a layer.
        DrawPasteboardItems(dc, ws);

        // Drop shadow edge around the canvas.
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), 3),
            Rect.Inflate(docRectView, 1.5, 1.5));

        // Checkerboard under the image.
        dc.DrawRectangle(GetCheckerBrush(), null, docRectView);

        // The composite image.
        dc.PushClip(new RectangleGeometry(docRectView));
        var scaling = Zoom >= 1.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality;
        RenderOptions.SetBitmapScalingMode(this, scaling);
        dc.DrawImage(ws.CompositeBitmap, docRectView);
        dc.Pop();

        // Pixel grid at high zoom.
        if (ShowPixelGrid && Zoom >= 8)
            DrawPixelGrid(dc, docW, docH);

        // Selection marching ants.
        DrawSelection(dc, ws);

        // Tool overlay.
        ActiveTool?.OnRenderOverlay(dc, Transform);

        // Keep the alignment guide above floating pixels and every tool
        // preview. This is deliberately last so a moved sprite sheet can never
        // obscure the frame boundaries used to position it.
        if (ShowSpriteGrid)
            DrawSpriteGrid(dc, docW, docH);
    }

    private void DrawPasteboardItems(DrawingContext dc, DocumentWorkspace ws)
    {
        if (ws.Document.PasteboardItems.Count == 0) return;
        RenderOptions.SetBitmapScalingMode(this,
            Zoom >= 1.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        var outline = new Pen(new SolidColorBrush(Color.FromArgb(115, 220, 225, 235)), 1);
        outline.Freeze();
        var shadow = new SolidColorBrush(Color.FromArgb(55, 0, 0, 0));
        shadow.Freeze();
        foreach (var item in ws.Document.PasteboardItems)
        {
            var tl = DocToView(new Point(item.X, item.Y));
            var rect = new Rect(tl.X, tl.Y, item.Surface.Width * Zoom, item.Surface.Height * Zoom);
            dc.DrawRectangle(shadow, null, new Rect(rect.X + 3, rect.Y + 3, rect.Width, rect.Height));
            dc.DrawImage(ws.GetPasteboardBitmap(item), rect);
            dc.DrawRectangle(null, outline, rect);
        }
    }

    private Brush GetCheckerBrush()
    {
        Color light = (Color)(TryFindResource("CheckerLightColor") ?? Colors.White);
        Color dark = (Color)(TryFindResource("CheckerDarkColor") ?? Colors.LightGray);
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(light), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(dark), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(dark), null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    private void DrawPixelGrid(DrawingContext dc, int docW, int docH)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)), 1);
        pen.Freeze();
        // Only draw lines inside the viewport.
        int x0 = Math.Max(0, (int)((0 - OffsetX) / Zoom));
        int x1 = Math.Min(docW, (int)((ActualWidth - OffsetX) / Zoom) + 1);
        int y0 = Math.Max(0, (int)((0 - OffsetY) / Zoom));
        int y1 = Math.Min(docH, (int)((ActualHeight - OffsetY) / Zoom) + 1);
        double top = Math.Max(0, OffsetY), bottom = Math.Min(ActualHeight, OffsetY + docH * Zoom);
        double left = Math.Max(0, OffsetX), right = Math.Min(ActualWidth, OffsetX + docW * Zoom);
        for (int x = x0; x <= x1; x++)
        {
            double vx = Math.Round(x * Zoom + OffsetX) + 0.5;
            dc.DrawLine(pen, new Point(vx, top), new Point(vx, bottom));
        }
        for (int y = y0; y <= y1; y++)
        {
            double vy = Math.Round(y * Zoom + OffsetY) + 0.5;
            dc.DrawLine(pen, new Point(left, vy), new Point(right, vy));
        }
    }

    private void DrawSpriteGrid(DrawingContext dc, int docW, int docH)
    {
        var layout = new SpriteGridLayout(SpriteGridCellWidth, SpriteGridCellHeight);
        int columns = layout.Columns(docW), rows = layout.Rows(docH);
        if (columns == 0 || rows == 0) return;

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(225, 235, 45, 55)), 1.25);
        pen.Freeze();
        double left = OffsetX;
        double top = OffsetY;
        double right = OffsetX + columns * SpriteGridCellWidth * Zoom;
        double bottom = OffsetY + rows * SpriteGridCellHeight * Zoom;

        dc.PushClip(new RectangleGeometry(new Rect(OffsetX, OffsetY, docW * Zoom, docH * Zoom)));
        for (int column = 0; column <= columns; column++)
        {
            double x = Math.Round(OffsetX + column * SpriteGridCellWidth * Zoom) + 0.5;
            dc.DrawLine(pen, new Point(x, top), new Point(x, bottom));
        }
        for (int row = 0; row <= rows; row++)
        {
            double y = Math.Round(OffsetY + row * SpriteGridCellHeight * Zoom) + 0.5;
            dc.DrawLine(pen, new Point(left, y), new Point(right, y));
        }
        dc.Pop();
    }

    private void DrawSelection(DrawingContext dc, DocumentWorkspace ws)
    {
        var selection = ws.Document.Selection;
        if (selection.IsEmpty)
        {
            _selectionGeometry = null;
            _selectionGeometryVersion = -1;
            return;
        }
        if (_selectionGeometryVersion != selection.Version)
        {
            _selectionGeometry = BuildOutlineGeometry(selection.Mask, selection.Width, selection.Height, selection.Bounds);
            _selectionGeometryVersion = selection.Version;
        }
        if (_selectionGeometry == null) return;

        dc.PushTransform(new MatrixTransform(Zoom, 0, 0, Zoom, OffsetX, OffsetY));
        double thickness = 1.25 / Zoom;
        var whitePen = new Pen(Brushes.White, thickness);
        whitePen.Freeze();
        dc.DrawGeometry(null, whitePen, _selectionGeometry);
        var blackPen = new Pen(Brushes.Black, thickness)
        {
            DashStyle = new DashStyle(new double[] { 4, 4 }, _antsOffset),
        };
        blackPen.Freeze();
        dc.DrawGeometry(null, blackPen, _selectionGeometry);
        dc.Pop();
    }

    /// <summary>
    /// Extracts the boundary of the selection mask (threshold 128) as merged
    /// horizontal/vertical line segments in document space.
    /// </summary>
    private static StreamGeometry BuildOutlineGeometry(byte[] mask, int w, int h, RectInt bounds)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            bool Inside(int x, int y) =>
                x >= 0 && y >= 0 && x < w && y < h && mask[y * w + x] >= 128;

            int bx0 = Math.Max(0, bounds.Left - 1), bx1 = Math.Min(w, bounds.Right + 1);
            int by0 = Math.Max(0, bounds.Top - 1), by1 = Math.Min(h, bounds.Bottom + 1);

            // Horizontal edges: between row y-1 and y.
            for (int y = by0; y <= by1; y++)
            {
                int runStart = -1;
                for (int x = bx0; x <= bx1; x++)
                {
                    bool edge = x < bx1 && Inside(x, y) != Inside(x, y - 1);
                    if (edge && runStart < 0) runStart = x;
                    if (!edge && runStart >= 0)
                    {
                        ctx.BeginFigure(new Point(runStart, y), false, false);
                        ctx.LineTo(new Point(x, y), true, false);
                        runStart = -1;
                    }
                }
            }
            // Vertical edges: between column x-1 and x.
            for (int x = bx0; x <= bx1; x++)
            {
                int runStart = -1;
                for (int y = by0; y <= by1; y++)
                {
                    bool edge = y < by1 && Inside(x, y) != Inside(x - 1, y);
                    if (edge && runStart < 0) runStart = y;
                    if (!edge && runStart >= 0)
                    {
                        ctx.BeginFigure(new Point(x, runStart), false, false);
                        ctx.LineTo(new Point(x, y), true, false);
                        runStart = -1;
                    }
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }
}
