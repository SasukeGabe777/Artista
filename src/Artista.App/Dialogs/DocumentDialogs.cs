using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Artista.Core.Documents;
using Artista.Core.Layers;
using Artista.Core.Imaging;

namespace Artista.App.Dialogs;

public sealed class NewDocumentDialog : DialogBase
{
    private readonly TextBox _width;
    private readonly TextBox _height;
    private readonly RadioButton _bgTransparent = new() { Content = "Transparent", Margin = new Thickness(0, 2, 0, 2) };
    private readonly RadioButton _bgWhite = new() { Content = "White", Margin = new Thickness(0, 2, 0, 2) };
    private readonly RadioButton _bgColor = new() { Content = "Primary color", Margin = new Thickness(0, 2, 0, 2) };

    public int DocWidth { get; private set; }
    public int DocHeight { get; private set; }
    public string BackgroundKind =>
        _bgTransparent.IsChecked == true ? "Transparent" : _bgWhite.IsChecked == true ? "White" : "Color";

    public NewDocumentDialog(AppSettings settings) : base("New Image")
    {
        _width = NumberBox(settings.DefaultDocumentWidth);
        _height = NumberBox(settings.DefaultDocumentHeight);
        Body.Children.Add(LabeledRow("Width (px):", _width));
        Body.Children.Add(LabeledRow("Height (px):", _height));
        var group = new GroupBox { Header = "Background", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(8) };
        var stack = new StackPanel();
        stack.Children.Add(_bgTransparent);
        stack.Children.Add(_bgWhite);
        stack.Children.Add(_bgColor);
        group.Content = stack;
        Body.Children.Add(group);

        switch (settings.DefaultDocumentBackground)
        {
            case "Transparent": _bgTransparent.IsChecked = true; break;
            case "Color": _bgColor.IsChecked = true; break;
            default: _bgWhite.IsChecked = true; break;
        }
        _width.Focus();
        _width.SelectAll();
    }

    protected override void TryAccept()
    {
        if (!TryParsePositive(_width, out int w) || !TryParsePositive(_height, out int h)) return;
        DocWidth = w;
        DocHeight = h;
        DialogResult = true;
    }
}

public sealed class ResizeImageDialog : DialogBase
{
    private readonly TextBox _width;
    private readonly TextBox _height;
    private readonly CheckBox _aspect = new() { Content = "Maintain aspect ratio", IsChecked = true, Margin = new Thickness(0, 6, 0, 2) };
    private readonly ComboBox _mode = new() { Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly int _origW, _origH;
    private bool _updating;

    public int NewWidth { get; private set; }
    public int NewHeight { get; private set; }
    public ResampleMode Mode => _mode.SelectedIndex == 1 ? ResampleMode.NearestNeighbor : ResampleMode.Bilinear;

    public ResizeImageDialog(int width, int height) : base("Resize Image")
    {
        _origW = width;
        _origH = height;
        _width = NumberBox(width);
        _height = NumberBox(height);
        _mode.Items.Add("Smooth (bilinear)");
        _mode.Items.Add("Nearest neighbor");
        _mode.SelectedIndex = 0;

        Body.Children.Add(LabeledRow("Width (px):", _width));
        Body.Children.Add(LabeledRow("Height (px):", _height));
        Body.Children.Add(_aspect);
        Body.Children.Add(LabeledRow("Resampling:", _mode));

        _width.TextChanged += (_, _) => SyncAspect(true);
        _height.TextChanged += (_, _) => SyncAspect(false);
        _width.Focus();
        _width.SelectAll();
    }

    private void SyncAspect(bool fromWidth)
    {
        if (_updating || _aspect.IsChecked != true) return;
        _updating = true;
        try
        {
            if (fromWidth && int.TryParse(_width.Text, out int w) && w > 0)
                _height.Text = Math.Max(1, (int)Math.Round((double)w * _origH / _origW)).ToString();
            else if (!fromWidth && int.TryParse(_height.Text, out int h) && h > 0)
                _width.Text = Math.Max(1, (int)Math.Round((double)h * _origW / _origH)).ToString();
        }
        finally
        {
            _updating = false;
        }
    }

    protected override void TryAccept()
    {
        if (!TryParsePositive(_width, out int w) || !TryParsePositive(_height, out int h)) return;
        NewWidth = w;
        NewHeight = h;
        DialogResult = true;
    }
}

public sealed class CanvasSizeDialog : DialogBase
{
    private readonly TextBox _width;
    private readonly TextBox _height;
    private readonly UniformGrid _anchorGrid = new() { Rows = 3, Columns = 3, Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left };
    private AnchorPosition _anchor = AnchorPosition.MiddleCenter;

    public int NewWidth { get; private set; }
    public int NewHeight { get; private set; }
    public AnchorPosition Anchor => _anchor;

    public CanvasSizeDialog(int width, int height) : base("Canvas Size")
    {
        _width = NumberBox(width);
        _height = NumberBox(height);
        Body.Children.Add(LabeledRow("Width (px):", _width));
        Body.Children.Add(LabeledRow("Height (px):", _height));

        var anchors = (AnchorPosition[])Enum.GetValues(typeof(AnchorPosition));
        foreach (var anchor in anchors)
        {
            var toggle = new System.Windows.Controls.Primitives.ToggleButton
            {
                Tag = anchor,
                Margin = new Thickness(1),
                IsChecked = anchor == AnchorPosition.MiddleCenter,
            };
            toggle.Checked += (s, _) =>
            {
                _anchor = (AnchorPosition)((FrameworkElement)s!).Tag;
                foreach (System.Windows.Controls.Primitives.ToggleButton other in _anchorGrid.Children)
                    if (!ReferenceEquals(other, s))
                        other.IsChecked = false;
            };
            _anchorGrid.Children.Add(toggle);
        }
        Body.Children.Add(LabeledRow("Anchor:", _anchorGrid));
        _width.Focus();
        _width.SelectAll();
    }

    protected override void TryAccept()
    {
        if (!TryParsePositive(_width, out int w) || !TryParsePositive(_height, out int h)) return;
        NewWidth = w;
        NewHeight = h;
        DialogResult = true;
    }
}

public sealed class LayerPropertiesDialog : DialogBase
{
    private readonly TextBox _name;
    private readonly Slider _opacity;
    private readonly ComboBox _blend = new() { Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox _visible = new() { Content = "Visible", Margin = new Thickness(0, 4, 0, 2) };
    private readonly CheckBox _locked = new() { Content = "Locked (protect pixels)", Margin = new Thickness(0, 2, 0, 2) };
    private readonly CheckBox _alphaLocked = new() { Content = "Lock alpha (paint keeps transparency)", Margin = new Thickness(0, 2, 0, 2) };
    private readonly TextBlock _opacityValue = new() { Width = 36, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };

    public string LayerName => _name.Text.Trim().Length == 0 ? "Layer" : _name.Text.Trim();
    public byte LayerOpacity => (byte)Math.Clamp(Math.Round(_opacity.Value * 255 / 100), 0, 255);
    public Core.Layers.BlendMode Blend => (Core.Layers.BlendMode)_blend.SelectedIndex;
    public bool LayerVisible => _visible.IsChecked == true;
    public bool LayerLocked => _locked.IsChecked == true;
    public bool LayerAlphaLocked => _alphaLocked.IsChecked == true;

    public LayerPropertiesDialog(Core.Layers.Layer layer) : base("Layer Properties")
    {
        _name = new TextBox { Text = layer.Name, Width = 200 };
        Body.Children.Add(LabeledRow("Name:", _name, 80));

        foreach (var mode in Core.Layers.BlendModeOps.All)
            _blend.Items.Add(mode.DisplayName());
        _blend.SelectedIndex = (int)layer.BlendMode;
        Body.Children.Add(LabeledRow("Blend mode:", _blend, 80));

        _opacity = new Slider { Minimum = 0, Maximum = 100, Value = layer.Opacity * 100.0 / 255.0, Width = 160 };
        var opacityRow = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var label = new TextBlock { Text = "Opacity:", Width = 80, VerticalAlignment = VerticalAlignment.Center };
        _opacity.ValueChanged += (_, _) => _opacityValue.Text = $"{(int)_opacity.Value}%";
        _opacityValue.Text = $"{(int)_opacity.Value}%";
        opacityRow.Children.Add(label);
        DockPanel.SetDock(_opacityValue, Dock.Right);
        opacityRow.Children.Add(_opacityValue);
        opacityRow.Children.Add(_opacity);
        Body.Children.Add(opacityRow);

        _visible.IsChecked = layer.Visible;
        _locked.IsChecked = layer.Locked;
        _alphaLocked.IsChecked = layer.AlphaLocked;
        Body.Children.Add(_visible);
        Body.Children.Add(_locked);
        Body.Children.Add(_alphaLocked);
        _name.Focus();
        _name.SelectAll();
    }
}

public sealed class ImagePropertiesDialog : DialogBase
{
    public ImagePropertiesDialog(Models.DocumentWorkspace ws) : base("Image Properties")
    {
        var doc = ws.Document;
        long memory = doc.Layers.Sum(l => l.Surface.ByteCount);
        AddRow("File:", ws.FilePath ?? "(not saved)");
        AddRow("Dimensions:", $"{doc.Width} × {doc.Height} pixels");
        AddRow("Layers:", doc.Layers.Count.ToString());
        AddRow("Pixel format:", "32-bit BGRA (straight alpha)");
        AddRow("Memory (layers):", $"{memory / (1024.0 * 1024.0):F1} MB");
        AddRow("Undo steps:", ws.History.UndoEntries.Count.ToString());
        AddRow("History memory:", $"{ws.History.TotalSize / (1024.0 * 1024.0):F1} MB");
        CancelButton.Visibility = Visibility.Collapsed;
    }

    private void AddRow(string label, string value)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var l = new TextBlock { Text = label, Width = 120 };
        l.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundDimBrush");
        row.Children.Add(l);
        row.Children.Add(new TextBlock { Text = value });
        Body.Children.Add(row);
    }
}
