using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Artista.Core.Imaging;
using Artista.Core.IO;
using Artista.Core.Selections;

namespace Artista.App.Dialogs;

/// <summary>Visual confirmation shared by all automatic sprite-grid operations.</summary>
internal sealed class SpriteAnalysisPreviewDialog : DialogBase
{
    public SpriteAnalysisPreviewDialog(
        string title,
        Surface surface,
        RectInt selectionBounds,
        IReadOnlyList<DetectedSprite> sprites,
        SpriteGridLayout? currentGrid,
        SpriteGridLayout proposedGrid,
        IReadOnlyList<SpriteMovePlan>? moves,
        string summary)
        : base(title)
    {
        OkButton.Content = "Apply";
        Body.Children.Add(new TextBlock
        {
            Text = summary,
            Width = 720,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        Body.Children.Add(new SpriteAnalysisPreviewView(
            surface, selectionBounds, sprites, currentGrid, proposedGrid, moves)
        {
            Width = 720,
            Height = 480,
        });
        Body.Children.Add(new TextBlock
        {
            Text = moves != null
                ? "Yellow = detected sprite; green = proposed destination; red = active grid."
                : "Yellow = detected sprite; gray dashed = current grid; red = proposed grid.",
            Margin = new Thickness(0, 7, 0, 0),
            Opacity = 0.8,
        });
    }
}

internal sealed class SpriteAnalysisPreviewView : FrameworkElement
{
    private readonly Surface _surface;
    private readonly BitmapSource _bitmap;
    private readonly RectInt _selectionBounds;
    private readonly IReadOnlyList<DetectedSprite> _sprites;
    private readonly SpriteGridLayout? _currentGrid;
    private readonly SpriteGridLayout _proposedGrid;
    private readonly IReadOnlyList<SpriteMovePlan>? _moves;

    public SpriteAnalysisPreviewView(
        Surface surface,
        RectInt selectionBounds,
        IReadOnlyList<DetectedSprite> sprites,
        SpriteGridLayout? currentGrid,
        SpriteGridLayout proposedGrid,
        IReadOnlyList<SpriteMovePlan>? moves)
    {
        _surface = surface;
        _selectionBounds = selectionBounds;
        _sprites = sprites;
        _currentGrid = currentGrid;
        _proposedGrid = proposedGrid;
        _moves = moves;
        _bitmap = ImageCodec.ToBitmapSource(surface);
        _bitmap.Freeze();
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.DimGray, null, new Rect(0, 0, ActualWidth, ActualHeight));
        double scale = Math.Min(
            Math.Max(0.01, (ActualWidth - 20) / _surface.Width),
            Math.Max(0.01, (ActualHeight - 20) / _surface.Height));
        double width = _surface.Width * scale, height = _surface.Height * scale;
        double offsetX = (ActualWidth - width) / 2;
        double offsetY = (ActualHeight - height) / 2;
        var artboard = new Rect(offsetX, offsetY, width, height);
        dc.DrawRectangle(CheckerBrush(), null, artboard);
        RenderOptions.SetBitmapScalingMode(this,
            scale >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        dc.DrawImage(_bitmap, artboard);
        dc.PushClip(new RectangleGeometry(artboard));

        Point Map(double x, double y) => new(offsetX + x * scale, offsetY + y * scale);
        Rect MapRect(RectInt rect) => new(Map(rect.Left, rect.Top), Map(rect.Right, rect.Bottom));

        var selectionPen = new Pen(new SolidColorBrush(Color.FromArgb(210, 55, 150, 255)), 1.5)
            { DashStyle = DashStyles.Dash };
        selectionPen.Freeze();
        dc.DrawRectangle(null, selectionPen, MapRect(_selectionBounds));

        if (_currentGrid is { } oldGrid && oldGrid != _proposedGrid)
        {
            var oldPen = new Pen(new SolidColorBrush(Color.FromArgb(150, 220, 225, 235)), 1)
                { DashStyle = DashStyles.Dash };
            oldPen.Freeze();
            DrawGrid(dc, oldGrid, oldPen, Map);
        }
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(235, 245, 45, 55)), 1.5);
        gridPen.Freeze();
        DrawGrid(dc, _proposedGrid, gridPen, Map);

        var detectedPen = new Pen(Brushes.Gold, 1.5);
        detectedPen.Freeze();
        foreach (var sprite in _sprites)
            dc.DrawRectangle(null, detectedPen, MapRect(sprite.Bounds));

        if (_moves != null)
        {
            var destinationPen = new Pen(Brushes.LimeGreen, 1.75);
            destinationPen.Freeze();
            foreach (var move in _moves)
            {
                dc.DrawRectangle(null, destinationPen, MapRect(move.DestinationBounds));
                var from = Map(
                    (move.Sprite.Bounds.Left + move.Sprite.Bounds.Right) / 2.0,
                    (move.Sprite.Bounds.Top + move.Sprite.Bounds.Bottom) / 2.0);
                var to = Map(
                    (move.DestinationBounds.Left + move.DestinationBounds.Right) / 2.0,
                    (move.DestinationBounds.Top + move.DestinationBounds.Bottom) / 2.0);
                dc.DrawLine(destinationPen, from, to);
                if ((to - from).Length > 5)
                    dc.DrawEllipse(Brushes.LimeGreen, null, to, 3, 3);
            }
        }
        dc.Pop();

        dc.DrawRectangle(null, new Pen(Brushes.Black, 1), artboard);
    }

    private void DrawGrid(
        DrawingContext dc,
        SpriteGridLayout grid,
        Pen pen,
        Func<double, double, Point> map)
    {
        var columns = grid.IntersectingColumnRange(_surface.Width);
        var rows = grid.IntersectingRowRange(_surface.Height);
        if (columns.Count == 0 || rows.Count == 0) return;
        for (int column = columns.Min; column <= columns.Max; column++)
        {
            var cell = grid.CellBounds(column, rows.Min);
            foreach (int x in new[] { cell.Left, cell.Right })
                if (x >= 0 && x <= _surface.Width)
                    dc.DrawLine(pen, map(x, 0), map(x, _surface.Height));
        }
        for (int row = rows.Min; row <= rows.Max; row++)
        {
            var cell = grid.CellBounds(columns.Min, row);
            foreach (int y in new[] { cell.Top, cell.Bottom })
                if (y >= 0 && y <= _surface.Height)
                    dc.DrawLine(pen, map(0, y), map(_surface.Width, y));
        }
    }

    private Brush CheckerBrush()
    {
        Color light = (Color)(TryFindResource("CheckerLightColor") ?? Colors.White);
        Color dark = (Color)(TryFindResource("CheckerDarkColor") ?? Colors.LightGray);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(light), null,
            new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(dark), null,
            new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(dark), null,
            new RectangleGeometry(new Rect(8, 8, 8, 8))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
        };
    }
}
