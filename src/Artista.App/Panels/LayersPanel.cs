using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Artista.Core.History;
using Artista.Core.Layers;

namespace Artista.App.Panels;

/// <summary>
/// Layers panel: thumbnail list (top layer first), visibility checkboxes,
/// drag-to-reorder, and the add / delete / duplicate / merge / move / properties
/// buttons along the bottom.
/// </summary>
public sealed class LayersPanel : DockPanel
{
    private readonly IShellHost _host;
    private readonly ListBox _list = new();
    private bool _refreshing;
    private int _dragSourceIndex = -1;

    public LayersPanel(IShellHost host)
    {
        _host = host;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        buttons.Children.Add(IconButton("Icon.Plus", "Add layer", (_, _) => host.LayerAdd()));
        buttons.Children.Add(IconButton("Icon.Delete", "Delete layer", (_, _) => host.LayerDelete()));
        buttons.Children.Add(IconButton("Icon.Duplicate", "Duplicate layer", (_, _) => host.LayerDuplicate()));
        buttons.Children.Add(IconButton("Icon.MergeDown", "Merge layer down", (_, _) => host.LayerMergeDown()));
        buttons.Children.Add(IconButton("Icon.ArrowUp", "Move layer up", (_, _) => host.LayerMoveUp()));
        buttons.Children.Add(IconButton("Icon.ArrowDown", "Move layer down", (_, _) => host.LayerMoveDown()));
        buttons.Children.Add(IconButton("Icon.Properties", "Layer properties", (_, _) => host.LayerProperties()));
        SetDock(buttons, Dock.Bottom);
        Children.Add(buttons);

        _list.SelectionChanged += (_, _) =>
        {
            if (_refreshing || _host.ActiveWorkspace == null || _list.SelectedItem is not LayerRow row) return;
            var doc = _host.ActiveWorkspace.Document;
            int index = doc.IndexOfLayer(row.Layer.Id);
            if (index >= 0)
                doc.ActiveLayerIndex = index;
        };
        _list.PreviewMouseLeftButtonDown += OnListMouseDown;
        _list.PreviewMouseMove += OnListMouseMove;
        _list.MouseDoubleClick += (_, _) => host.LayerProperties();
        Children.Add(_list);
    }

    private Button IconButton(string iconKey, string tooltip, RoutedEventHandler onClick)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = (Geometry)Application.Current.FindResource(iconKey),
            StrokeThickness = 1.3,
            Width = 14, Height = 14,
            Stretch = Stretch.Uniform,
        };
        path.SetResourceReference(System.Windows.Shapes.Path.StrokeProperty, "IconBrush");
        var button = new Button
        {
            Content = path, ToolTip = tooltip,
            Padding = new Thickness(4), Margin = new Thickness(1, 0, 1, 0),
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        };
        button.Click += onClick;
        return button;
    }

    private sealed record LayerRow(Layer Layer, FrameworkElement Visual);

    public void Refresh()
    {
        _refreshing = true;
        try
        {
            _list.Items.Clear();
            var ws = _host.ActiveWorkspace;
            if (ws == null) return;
            var doc = ws.Document;
            // Top layer first (index Layers.Count-1 shows at top of the panel).
            for (int i = doc.Layers.Count - 1; i >= 0; i--)
            {
                var layer = doc.Layers[i];
                var row = new LayerRow(layer, BuildRow(layer));
                var item = new ListBoxItem { Content = row.Visual, Tag = row };
                _list.Items.Add(item);
                if (i == doc.ActiveLayerIndex)
                    _list.SelectedItem = item;
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private FrameworkElement BuildRow(Layer layer)
    {
        var grid = new DockPanel { Height = 40, LastChildFill = true };

        var visible = new CheckBox
        {
            IsChecked = layer.Visible,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 6, 0),
            ToolTip = "Layer visibility",
        };
        visible.Click += (_, _) =>
        {
            var ws = _host.ActiveWorkspace;
            if (ws == null) return;
            _host.PushHistory(new LayerPropertiesMemento("Layer Visibility", layer));
            layer.Visible = visible.IsChecked == true;
            ws.MarkDirty();
            _host.InvalidateDocument(ws.Document.Bounds);
        };
        grid.Children.Add(visible);

        var thumb = new Border
        {
            Width = 48, Height = 34,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 0),
            Background = MakeCheckerBrush(),
            Child = new Image
            {
                Source = MakeThumbnail(layer),
                Stretch = Stretch.Uniform,
            },
        };
        thumb.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        grid.Children.Add(thumb);

        var textCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = layer.Name, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = new TextBlock
        {
            FontSize = 10,
            Text = BuildDetailText(layer),
        };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundDimBrush");
        textCol.Children.Add(name);
        textCol.Children.Add(detail);
        grid.Children.Add(textCol);
        return grid;
    }

    private static string BuildDetailText(Layer layer)
    {
        var parts = new List<string>();
        if (layer.Opacity != 255) parts.Add($"{layer.Opacity * 100 / 255}%");
        if (layer.BlendMode != BlendMode.Normal) parts.Add(layer.BlendMode.DisplayName());
        if (layer.Locked) parts.Add("Locked");
        if (layer.AlphaLocked) parts.Add("Alpha lock");
        return parts.Count == 0 ? "Normal" : string.Join(" · ", parts);
    }

    private static Brush MakeCheckerBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(Brushes.LightGray, null, new RectangleGeometry(new Rect(0, 0, 4, 4))));
        group.Children.Add(new GeometryDrawing(Brushes.LightGray, null, new RectangleGeometry(new Rect(4, 4, 4, 4))));
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    private static BitmapSource MakeThumbnail(Layer layer)
    {
        // Downsample to <= 48x34 with nearest sampling (fast, good enough for thumbs).
        var s = layer.Surface;
        int tw = 48, th = 34;
        double scale = Math.Min((double)tw / s.Width, (double)th / s.Height);
        int w = Math.Max(1, (int)(s.Width * scale));
        int h = Math.Max(1, (int)(s.Height * scale));
        var pixels = new uint[w * h];
        for (int y = 0; y < h; y++)
        {
            int sy = Math.Min(s.Height - 1, y * s.Height / h);
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(s.Width - 1, x * s.Width / w);
                pixels[y * w + x] = s[sx, sy];
            }
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        bmp.Freeze();
        return bmp;
    }

    // ---- drag to reorder ----

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragSourceIndex = IndexUnderMouse(e);
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSourceIndex < 0) return;
        int overIndex = IndexUnderMouse(e);
        if (overIndex < 0 || overIndex == _dragSourceIndex) return;

        var ws = _host.ActiveWorkspace;
        if (ws == null) return;
        var doc = ws.Document;
        // List index 0 = top layer = doc index Count-1.
        int fromDoc = doc.Layers.Count - 1 - _dragSourceIndex;
        int toDoc = doc.Layers.Count - 1 - overIndex;
        if (fromDoc < 0 || toDoc < 0 || fromDoc >= doc.Layers.Count || toDoc >= doc.Layers.Count) return;

        var layer = doc.Layers[fromDoc];
        _host.PushHistory(new LayerOrderMemento("Reorder Layers", layer.Id, fromDoc), "Icon.ArrowUp");
        doc.Layers.RemoveAt(fromDoc);
        doc.Layers.Insert(toDoc, layer);
        doc.ActiveLayerIndex = toDoc;
        ws.MarkDirty();
        _dragSourceIndex = overIndex;
        _host.InvalidateDocument(doc.Bounds);
        _host.RefreshAllPanels();
    }

    private int IndexUnderMouse(MouseEventArgs e)
    {
        for (int i = 0; i < _list.Items.Count; i++)
        {
            if (_list.Items[i] is ListBoxItem item && item.IsMouseOver)
                return i;
        }
        return -1;
    }
}
