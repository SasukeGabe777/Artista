using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Artista.Core.Effects;
using Artista.Core.Imaging;

namespace Artista.App.Panels;

/// <summary>
/// Paint.NET-style Colors panel: primary/secondary swatches, HSV color wheel,
/// RGB / HSV / alpha sliders, hex entry, palette and recent colors.
/// </summary>
public sealed class ColorsPanel : StackPanel
{
    private readonly IShellHost _host;
    private readonly Border _primarySwatch = new();
    private readonly Border _secondarySwatch = new();
    private readonly ColorWheel _wheel = new();
    private readonly Slider _r = new(), _g = new(), _b = new(), _h = new(), _s = new(), _v = new(), _a = new();
    private readonly TextBox _rBox = new(), _gBox = new(), _bBox = new(), _hex = new(), _aBox = new();
    private readonly WrapPanel _palettePanel = new();
    private readonly WrapPanel _recentPanel = new();
    private readonly List<uint> _recent = new();
    private bool _editingSecondary;
    private bool _updating;

    public ColorsPanel(IShellHost host)
    {
        _host = host;
        Margin = new Thickness(8);

        // --- swatches row ---
        var swatchGrid = new Grid { Width = 44, Height = 44, HorizontalAlignment = HorizontalAlignment.Left };
        _secondarySwatch.Width = _primarySwatch.Width = 28;
        _secondarySwatch.Height = _primarySwatch.Height = 28;
        _secondarySwatch.BorderThickness = _primarySwatch.BorderThickness = new Thickness(1);
        _secondarySwatch.HorizontalAlignment = HorizontalAlignment.Right;
        _secondarySwatch.VerticalAlignment = VerticalAlignment.Bottom;
        _primarySwatch.HorizontalAlignment = HorizontalAlignment.Left;
        _primarySwatch.VerticalAlignment = VerticalAlignment.Top;
        _secondarySwatch.MouseLeftButtonDown += (_, _) => { _editingSecondary = true; RefreshFromEnv(); };
        _primarySwatch.MouseLeftButtonDown += (_, _) => { _editingSecondary = false; RefreshFromEnv(); };
        _primarySwatch.ToolTip = "Primary color (click to edit)";
        _secondarySwatch.ToolTip = "Secondary color (click to edit)";
        swatchGrid.Children.Add(_secondarySwatch);
        swatchGrid.Children.Add(_primarySwatch);

        var swapButton = MakeIconButton("Icon.Swap", "Swap colors (X)", (_, _) => host.Env.SwapColors());
        var resetButton = new Button { Content = "B/W", ToolTip = "Reset to black and white", Padding = new Thickness(6, 2, 6, 2) };
        resetButton.Click += (_, _) => host.Env.ResetColors();

        var row = new DockPanel();
        row.Children.Add(swatchGrid);
        var buttonsCol = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        buttonsCol.Children.Add(swapButton);
        buttonsCol.Children.Add(resetButton);
        row.Children.Add(buttonsCol);
        var editingLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            FontSize = 11,
        };
        editingLabel.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundDimBrush");
        row.Children.Add(editingLabel);
        _editingLabel = editingLabel;
        Children.Add(row);

        // --- wheel ---
        _wheel.Width = _wheel.Height = 140;
        _wheel.Margin = new Thickness(0, 10, 0, 6);
        _wheel.HorizontalAlignment = HorizontalAlignment.Center;
        _wheel.ColorSelected += (_, hs) =>
        {
            var (hh, ss, vv) = CurrentHsv();
            ApplyHsv(hs.H, hs.S, vv <= 0.02 ? 1.0 : vv);
        };
        Children.Add(_wheel);

        // --- sliders ---
        Children.Add(SliderRow("H:", _h, 0, 360, OnHsvSlider, out _));
        Children.Add(SliderRow("S:", _s, 0, 100, OnHsvSlider, out _));
        Children.Add(SliderRow("V:", _v, 0, 100, OnHsvSlider, out _));
        Children.Add(SliderRow("R:", _r, 0, 255, OnRgbSlider, out var rb)); _rBoxRef = rb;
        Children.Add(SliderRow("G:", _g, 0, 255, OnRgbSlider, out var gb)); _gBoxRef = gb;
        Children.Add(SliderRow("B:", _b, 0, 255, OnRgbSlider, out var bb)); _bBoxRef = bb;
        Children.Add(SliderRow("A:", _a, 0, 255, OnRgbSlider, out var ab)); _aBoxRef = ab;

        // --- hex ---
        var hexRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var hexLabel = new TextBlock { Text = "Hex:", Width = 30, VerticalAlignment = VerticalAlignment.Center };
        _hex.Width = 84;
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) ApplyHex();
        };
        _hex.LostFocus += (_, _) => ApplyHex();
        hexRow.Children.Add(hexLabel);
        hexRow.Children.Add(_hex);
        Children.Add(hexRow);

        // --- palette ---
        var paletteHeader = new DockPanel { Margin = new Thickness(0, 10, 0, 2) };
        var paletteLabel = new TextBlock { Text = "Palette", VerticalAlignment = VerticalAlignment.Center };
        paletteLabel.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundDimBrush");
        var addButton = MakeIconButton("Icon.Plus", "Add current color to palette", (_, _) => AddToPalette());
        DockPanel.SetDock(addButton, Dock.Right);
        paletteHeader.Children.Add(addButton);
        paletteHeader.Children.Add(paletteLabel);
        Children.Add(paletteHeader);
        Children.Add(_palettePanel);

        var recentLabel = new TextBlock { Text = "Recent", Margin = new Thickness(0, 8, 0, 2) };
        recentLabel.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundDimBrush");
        Children.Add(recentLabel);
        Children.Add(_recentPanel);

        host.Env.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Models.ToolEnvironment.PrimaryColor) or nameof(Models.ToolEnvironment.SecondaryColor))
            {
                RecordRecent(_editingSecondary ? host.Env.SecondaryColor : host.Env.PrimaryColor);
                RefreshFromEnv();
            }
        };

        BuildPalette();
        RefreshFromEnv();
    }

    private readonly TextBlock _editingLabel;
    private TextBox _rBoxRef = null!, _gBoxRef = null!, _bBoxRef = null!, _aBoxRef = null!;

    private uint CurrentColor
    {
        get => _editingSecondary ? _host.Env.SecondaryColor : _host.Env.PrimaryColor;
        set
        {
            if (_editingSecondary) _host.Env.SecondaryColor = value;
            else _host.Env.PrimaryColor = value;
        }
    }

    private Button MakeIconButton(string iconKey, string tooltip, RoutedEventHandler onClick)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = (Geometry)Application.Current.FindResource(iconKey),
            StrokeThickness = 1.4,
            Width = 16, Height = 16,
            Stretch = Stretch.Uniform,
        };
        path.SetResourceReference(System.Windows.Shapes.Path.StrokeProperty, "IconBrush");
        var button = new Button { Content = path, ToolTip = tooltip, Padding = new Thickness(3), Margin = new Thickness(0, 0, 0, 4) };
        button.Click += onClick;
        return button;
    }

    private DockPanel SliderRow(string label, Slider slider, double min, double max,
        RoutedPropertyChangedEventHandler<double> onChange, out TextBox box)
    {
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
        var text = new TextBlock { Text = label, Width = 20, VerticalAlignment = VerticalAlignment.Center };
        box = new TextBox { Width = 40, Margin = new Thickness(6, 0, 0, 0) };
        var localBox = box;
        var localSlider = slider;
        slider.Minimum = min;
        slider.Maximum = max;
        slider.ValueChanged += (s, e) =>
        {
            if (!_updating) onChange(s, e);
            localBox.Text = ((int)Math.Round(localSlider.Value)).ToString();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && double.TryParse(localBox.Text, out double v))
                localSlider.Value = Math.Clamp(v, min, max);
        };
        DockPanel.SetDock(text, Dock.Left);
        DockPanel.SetDock(box, Dock.Right);
        row.Children.Add(text);
        row.Children.Add(box);
        row.Children.Add(slider);
        return row;
    }

    private (double H, double S, double V) CurrentHsv()
    {
        uint c = CurrentColor;
        return HueSaturationAdjustment.RgbToHsv(ColorBgra.R(c), ColorBgra.G(c), ColorBgra.B(c));
    }

    private void OnRgbSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var c = ColorBgra.Pack((byte)_b.Value, (byte)_g.Value, (byte)_r.Value, (byte)_a.Value);
        CurrentColor = c;
    }

    private void OnHsvSlider(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        ApplyHsv(_h.Value, _s.Value / 100.0, _v.Value / 100.0);

    private void ApplyHsv(double h, double s, double v)
    {
        var (r, g, b) = HueSaturationAdjustment.HsvToRgb(h, s, v);
        CurrentColor = ColorBgra.Pack(b, g, r, (byte)_a.Value);
    }

    private void ApplyHex()
    {
        string text = _hex.Text.Trim().TrimStart('#');
        if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
        {
            if (text.Length <= 6)
                CurrentColor = ((uint)(byte)_a.Value << 24) | (rgb & 0xFFFFFF);
            else
                CurrentColor = rgb;
        }
        RefreshFromEnv();
    }

    public void RefreshFromEnv()
    {
        _updating = true;
        try
        {
            uint p = _host.Env.PrimaryColor, s = _host.Env.SecondaryColor;
            _primarySwatch.Background = MakeSwatchBrush(p);
            _secondarySwatch.Background = MakeSwatchBrush(s);
            _primarySwatch.SetResourceReference(Border.BorderBrushProperty,
                _editingSecondary ? "BorderBrush" : "AccentBrush");
            _secondarySwatch.SetResourceReference(Border.BorderBrushProperty,
                _editingSecondary ? "AccentBrush" : "BorderBrush");
            _editingLabel.Text = _editingSecondary ? "Secondary" : "Primary";

            uint c = CurrentColor;
            _r.Value = ColorBgra.R(c);
            _g.Value = ColorBgra.G(c);
            _b.Value = ColorBgra.B(c);
            _a.Value = ColorBgra.A(c);
            var (h, sat, v) = CurrentHsv();
            _h.Value = h;
            _s.Value = sat * 100;
            _v.Value = v * 100;
            _hex.Text = $"{c & 0xFFFFFF:X6}";
            _wheel.SetSelection(h, sat);
            _rBoxRef.Text = ColorBgra.R(c).ToString();
            _gBoxRef.Text = ColorBgra.G(c).ToString();
            _bBoxRef.Text = ColorBgra.B(c).ToString();
            _aBoxRef.Text = ColorBgra.A(c).ToString();
        }
        finally
        {
            _updating = false;
        }
    }

    private static Brush MakeSwatchBrush(uint c)
    {
        var color = Color.FromArgb(ColorBgra.A(c), ColorBgra.R(c), ColorBgra.G(c), ColorBgra.B(c));
        if (ColorBgra.A(c) == 255)
            return new SolidColorBrush(color);
        // Checkerboard underneath for transparency.
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(Brushes.LightGray, null, new RectangleGeometry(new Rect(0, 0, 4, 4))));
        group.Children.Add(new GeometryDrawing(Brushes.LightGray, null, new RectangleGeometry(new Rect(4, 4, 4, 4))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        return new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute };
    }

    // ---- palette ----

    private static readonly uint[] DefaultPalette =
    {
        0xFF000000, 0xFF404040, 0xFF808080, 0xFFC0C0C0, 0xFFFFFFFF,
        0xFF7F0000, 0xFFFF0000, 0xFFFF6A00, 0xFFFFD800, 0xFFB6FF00,
        0xFF4CFF00, 0xFF00FF21, 0xFF00FF90, 0xFF00FFFF, 0xFF0094FF,
        0xFF0026FF, 0xFF4800FF, 0xFFB200FF, 0xFFFF00DC, 0xFFFF006E,
    };

    private void BuildPalette()
    {
        _palettePanel.Children.Clear();
        var colors = _host.Settings.Palette.Count > 0 ? _host.Settings.Palette : DefaultPalette.ToList();
        foreach (uint c in colors)
            _palettePanel.Children.Add(MakePaletteSwatch(c));
    }

    private FrameworkElement MakePaletteSwatch(uint c)
    {
        var border = new Border
        {
            Width = 16, Height = 16, Margin = new Thickness(1),
            Background = MakeSwatchBrush(c),
            BorderThickness = new Thickness(1),
            ToolTip = $"#{c & 0xFFFFFF:X6}",
        };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");
        border.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left) _host.Env.PrimaryColor = c;
            else if (e.ChangedButton == MouseButton.Right) _host.Env.SecondaryColor = c;
        };
        return border;
    }

    private void AddToPalette()
    {
        if (_host.Settings.Palette.Count == 0)
            _host.Settings.Palette.AddRange(DefaultPalette);
        _host.Settings.Palette.Add(CurrentColor);
        _host.Settings.Save();
        BuildPalette();
    }

    private void RecordRecent(uint c)
    {
        _recent.Remove(c);
        _recent.Insert(0, c);
        if (_recent.Count > 10) _recent.RemoveAt(_recent.Count - 1);
        _recentPanel.Children.Clear();
        foreach (uint rc in _recent)
            _recentPanel.Children.Add(MakePaletteSwatch(rc));
    }
}

/// <summary>HSV color wheel: hue by angle, saturation by radius.</summary>
public sealed class ColorWheel : FrameworkElement
{
    private WriteableBitmap? _bitmap;
    private double _selHue, _selSat;
    private bool _dragging;

    public event EventHandler<(double H, double S)>? ColorSelected;

    public void SetSelection(double hue, double sat)
    {
        _selHue = hue;
        _selSat = sat;
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        _bitmap = null;
    }

    private void EnsureBitmap()
    {
        int size = (int)Math.Min(ActualWidth, ActualHeight);
        if (size <= 4 || (_bitmap != null && _bitmap.PixelWidth == size)) return;
        _bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new uint[size * size];
        double radius = size / 2.0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - radius + 0.5, dy = y - radius + 0.5;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > radius) continue;
                double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                double sat = Math.Min(1.0, dist / radius);
                var (r, g, b) = HueSaturationAdjustment.HsvToRgb(hue, sat, 1.0);
                byte alpha = dist > radius - 1.5 ? (byte)Math.Clamp((radius - dist) / 1.5 * 255, 0, 255) : (byte)255;
                pixels[y * size + x] = ColorBgra.Pack(b, g, r, alpha);
            }
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
    }

    protected override void OnRender(DrawingContext dc)
    {
        EnsureBitmap();
        if (_bitmap == null) return;
        int size = _bitmap.PixelWidth;
        dc.DrawImage(_bitmap, new Rect(0, 0, size, size));
        // Selection thumb.
        double radius = size / 2.0;
        double angle = _selHue * Math.PI / 180;
        double dist = _selSat * radius;
        var p = new Point(radius + Math.Cos(angle) * dist, radius + Math.Sin(angle) * dist);
        dc.DrawEllipse(null, new Pen(Brushes.White, 2), p, 5, 5);
        dc.DrawEllipse(null, new Pen(Brushes.Black, 1), p, 6, 6);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        CaptureMouse();
        Pick(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) Pick(e.GetPosition(this));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }

    private void Pick(Point p)
    {
        double radius = Math.Min(ActualWidth, ActualHeight) / 2.0;
        double dx = p.X - radius, dy = p.Y - radius;
        double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        double sat = Math.Min(1.0, Math.Sqrt(dx * dx + dy * dy) / radius);
        _selHue = hue;
        _selSat = sat;
        InvalidateVisual();
        ColorSelected?.Invoke(this, (hue, sat));
    }
}
