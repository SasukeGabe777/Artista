using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Artista.App.Models;
using Artista.Core.Effects;
using Artista.Core.Imaging;

namespace Artista.App.Dialogs;

/// <summary>
/// Auto-generated configuration dialog for any effect: builds sliders,
/// checkboxes, combos, color pickers and curve editors from the effect's
/// parameter descriptors, renders a live preview on a background thread with
/// cancellation, and restores the exact original image on cancel.
/// </summary>
public sealed class EffectDialog : DialogBase
{
    private readonly EffectBase _effect;
    private readonly DocumentWorkspace _workspace;
    private readonly ParameterSet _parameters;
    private readonly Surface _source;         // untouched copy of the active layer
    private readonly RectInt _roi;
    private CancellationTokenSource? _previewCts;
    private Task? _previewTask;
    private int _previewGeneration;

    /// <summary>Set when the user asks to sample a color from the canvas.</summary>
    public event Action<Action<uint>>? EyedropperRequested;

    public ParameterSet Parameters => _parameters;

    public EffectDialog(EffectBase effect, DocumentWorkspace workspace) : base(effect.Name)
    {
        _effect = effect;
        _workspace = workspace;
        _source = workspace.Document.ActiveLayer.Surface.Clone();
        _roi = EffectiveRoi(workspace);
        var paramDefs = effect.CreateParameters();
        _parameters = ParameterSet.FromDefaults(paramDefs);

        foreach (var p in paramDefs)
            Body.Children.Add(BuildControl(p));

        Loaded += (_, _) => SchedulePreview();
        Closed += (_, _) => StopPreviewAndRestore();
        CancelButton.Click += (_, _) => Close();
    }

    private static RectInt EffectiveRoi(DocumentWorkspace ws)
    {
        var selection = ws.Document.Selection;
        // Render over the whole canvas; masking clips to selection precisely.
        // (Whole-canvas render keeps blur-type effects consistent at the edges.)
        return ws.Document.Bounds;
    }

    // ---------------- parameter controls ----------------

    private FrameworkElement BuildControl(EffectParameter p) => p switch
    {
        IntParameter ip => BuildSlider(ip.Label, ip.Min, ip.Max, ip.Default, 0,
            v => { _parameters.Set(ip.Id, (int)Math.Round(v)); SchedulePreview(); }),
        DoubleParameter dp => BuildSlider(dp.Label, dp.Min, dp.Max, dp.Default, dp.Decimals,
            v => { _parameters.Set(dp.Id, v); SchedulePreview(); }),
        BoolParameter bp => BuildCheckBox(bp),
        EnumParameter ep => BuildCombo(ep),
        ColorParameter cp => BuildColorRow(cp),
        CurvesParameter cup => BuildCurves(cup),
        _ => new TextBlock { Text = p.Label },
    };

    private FrameworkElement BuildSlider(string label, double min, double max, double initial, int decimals, Action<double> onChange)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4), MinWidth = 330 };
        var text = new TextBlock { Text = label + ":", Width = 130, VerticalAlignment = VerticalAlignment.Center };
        var valueBox = new TextBox { Width = 52, Margin = new Thickness(8, 0, 0, 0) };
        var slider = new Slider { Minimum = min, Maximum = max, Value = initial, Width = 170 };
        bool updating = false;
        void Sync(double v)
        {
            valueBox.Text = decimals == 0 ? ((int)Math.Round(v)).ToString() : Math.Round(v, decimals).ToString();
        }
        Sync(initial);
        slider.ValueChanged += (_, e) =>
        {
            if (updating) return;
            updating = true;
            Sync(e.NewValue);
            updating = false;
            onChange(e.NewValue);
        };
        valueBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && double.TryParse(valueBox.Text, out double v))
                slider.Value = Math.Clamp(v, min, max);
        };
        DockPanel.SetDock(text, Dock.Left);
        DockPanel.SetDock(valueBox, Dock.Right);
        row.Children.Add(text);
        row.Children.Add(valueBox);
        row.Children.Add(slider);
        return row;
    }

    private FrameworkElement BuildCheckBox(BoolParameter bp)
    {
        var check = new CheckBox { Content = bp.Label, IsChecked = bp.Default, Margin = new Thickness(0, 6, 0, 2) };
        check.Click += (_, _) =>
        {
            _parameters.Set(bp.Id, check.IsChecked == true);
            SchedulePreview();
        };
        return check;
    }

    private FrameworkElement BuildCombo(EnumParameter ep)
    {
        var combo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var option in ep.Options)
            combo.Items.Add(option);
        combo.SelectedIndex = ep.DefaultIndex;
        combo.SelectionChanged += (_, _) =>
        {
            _parameters.Set(ep.Id, combo.SelectedIndex);
            SchedulePreview();
        };
        return LabeledRow(ep.Label + ":", combo, 130);
    }

    private FrameworkElement BuildColorRow(ColorParameter cp)
    {
        var swatch = new Border
        {
            Width = 40, Height = 20, BorderThickness = new Thickness(1),
            Background = BrushFromBgra(cp.Default),
            VerticalAlignment = VerticalAlignment.Center,
        };
        swatch.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        uint current = cp.Default;

        var hexBox = new TextBox { Width = 70, Margin = new Thickness(8, 0, 0, 0), Text = $"{cp.Default & 0xFFFFFF:X6}" };
        void SetColor(uint c)
        {
            current = 0xFF000000u | (c & 0xFFFFFF);
            swatch.Background = BrushFromBgra(current);
            hexBox.Text = $"{current & 0xFFFFFF:X6}";
            _parameters.Set(cp.Id, current);
            SchedulePreview();
        }
        hexBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter &&
                uint.TryParse(hexBox.Text.Trim().TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out uint v))
                SetColor(v);
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(swatch);
        panel.Children.Add(hexBox);
        if (cp.AllowEyedropper)
        {
            var pick = new Button { Content = "Pick from canvas", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            pick.Click += (_, _) => EyedropperRequested?.Invoke(c => SetColor(c));
            panel.Children.Add(pick);
        }
        return LabeledRow(cp.Label + ":", panel, 130);
    }

    private FrameworkElement BuildCurves(CurvesParameter cup)
    {
        var editor = new CurveEditor { Width = 280, Height = 280, Margin = new Thickness(0, 6, 0, 4) };
        editor.CurvesChanged += (_, _) =>
        {
            _parameters.Set(cup.Id, editor.Value);
            SchedulePreview();
        };
        return editor;
    }

    private static Brush BrushFromBgra(uint c) => new SolidColorBrush(
        Color.FromArgb(255, ColorBgra.R(c), ColorBgra.G(c), ColorBgra.B(c)));

    // ---------------- live preview ----------------

    public void SchedulePreview()
    {
        if (_workspace == null) return;
        _previewCts?.Cancel();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        int generation = ++_previewGeneration;
        var parameters = _parameters.Clone();
        var selection = _workspace.Document.Selection;
        var layerId = _workspace.Document.ActiveLayer.Id;

        _previewTask = Task.Run(() =>
        {
            try
            {
                var preview = new Surface(_source.Width, _source.Height);
                preview.CopyFrom(_source);
                EffectRunner.RunMasked(_effect, _source, preview, parameters, selection, _roi, cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                Dispatcher.BeginInvoke(() =>
                {
                    if (generation != _previewGeneration) return;
                    _workspace.SetPreview(layerId, preview);
                    _workspace.InvalidateComposite(_roi);
                });
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer preview or dialog closing
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() =>
                    MessageBox.Show(this, $"The effect failed to render:\n{ex.Message}", "Effect error",
                        MessageBoxButton.OK, MessageBoxImage.Warning));
            }
        });
    }

    private void StopPreviewAndRestore()
    {
        _previewCts?.Cancel();
        _workspace.SetPreview(-1, null);
        _workspace.InvalidateComposite(_roi);
    }

    protected override void TryAccept()
    {
        // The actual application is done by the caller (MainWindow) so scope
        // handling and history integration live in one place.
        _previewCts?.Cancel();
        DialogResult = true;
    }
}

/// <summary>
/// Modal progress window with cancel for long-running operations.
/// </summary>
public sealed class ProgressDialog : Window
{
    public CancellationTokenSource Cts { get; } = new();

    public ProgressDialog(string title, Window owner)
    {
        Title = title;
        Owner = owner;
        Width = 340;
        Height = 120;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "ForegroundBrush");
        ThemeManager.ApplyTitleBar(this);

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = title + "…", Margin = new Thickness(0, 0, 0, 10) });
        stack.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6 });
        var cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80, Margin = new Thickness(0, 12, 0, 0) };
        cancel.Click += (_, _) => Cts.Cancel();
        stack.Children.Add(cancel);
        Content = stack;
        Closing += (_, _) => Cts.Cancel();
    }
}

/// <summary>
/// Editable tone curve: drag points, click to add, right-click to remove.
/// Channel selector switches between luminosity and per-channel R/G/B curves.
/// </summary>
public sealed class CurveEditor : Grid
{
    private readonly ComboBox _channel = new() { Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
    private readonly CurveCanvas _canvas = new();

    public CurvesValue Value => _canvas.Value;
    public event EventHandler? CurvesChanged;

    public CurveEditor()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        _channel.Items.Add("Luminosity");
        _channel.Items.Add("Red");
        _channel.Items.Add("Green");
        _channel.Items.Add("Blue");
        _channel.SelectedIndex = 0;
        _channel.SelectionChanged += (_, _) =>
        {
            _canvas.ActiveChannel = _channel.SelectedIndex;
            _canvas.Value.PerChannel = _channel.SelectedIndex > 0 || _canvas.Value.PerChannel;
            _canvas.InvalidateVisual();
        };
        SetRow(_channel, 0);
        SetRow(_canvas, 1);
        Children.Add(_channel);
        Children.Add(_canvas);
        _canvas.Changed += (_, _) => CurvesChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class CurveCanvas : FrameworkElement
    {
        public CurvesValue Value { get; } = CurvesValue.Identity();
        public int ActiveChannel;
        public event EventHandler? Changed;
        private int _dragIndex = -1;

        private List<(double X, double Y)> Points => Value.Channels[ActiveChannel];

        protected override void OnRender(DrawingContext dc)
        {
            var bg = TryFindResource("InputBackgroundBrush") as Brush ?? Brushes.White;
            var grid = TryFindResource("BorderLightBrush") as Brush ?? Brushes.LightGray;
            var fg = ActiveChannel switch
            {
                1 => Brushes.IndianRed,
                2 => Brushes.MediumSeaGreen,
                3 => Brushes.CornflowerBlue,
                _ => (TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue),
            };
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(bg, new Pen(grid, 1), new Rect(0, 0, w, h));
            for (int i = 1; i < 4; i++)
            {
                dc.DrawLine(new Pen(grid, 0.5), new Point(w * i / 4, 0), new Point(w * i / 4, h));
                dc.DrawLine(new Pen(grid, 0.5), new Point(0, h * i / 4), new Point(w, h * i / 4));
            }

            var lut = Value.BuildLut(ActiveChannel);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(MapToView(0, lut[0], w, h), false, false);
                for (int x = 1; x < 256; x += 2)
                    ctx.LineTo(MapToView(x, lut[x], w, h), true, false);
            }
            dc.DrawGeometry(null, new Pen(fg, 1.6), geometry);

            foreach (var p in Points)
            {
                var vp = MapToView(p.X, p.Y, w, h);
                dc.DrawEllipse(fg, null, vp, 4, 4);
            }
        }

        private static Point MapToView(double x, double y, double w, double h) =>
            new(x / 255.0 * w, h - y / 255.0 * h);

        private (double X, double Y) ViewToCurve(Point p) =>
            (Math.Clamp(p.X / ActualWidth * 255.0, 0, 255), Math.Clamp((ActualHeight - p.Y) / ActualHeight * 255.0, 0, 255));

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);
            int hit = HitTestPoint(pos);
            if (e.ChangedButton == MouseButton.Right)
            {
                if (hit > 0 && hit < Points.Count - 1)
                {
                    Points.RemoveAt(hit);
                    InvalidateVisual();
                    Changed?.Invoke(this, EventArgs.Empty);
                }
                return;
            }
            if (hit >= 0)
            {
                _dragIndex = hit;
            }
            else
            {
                var cp = ViewToCurve(pos);
                Points.Add(cp);
                Points.Sort((a, b) => a.X.CompareTo(b.X));
                _dragIndex = Points.FindIndex(p => p == cp);
                InvalidateVisual();
                Changed?.Invoke(this, EventArgs.Empty);
            }
            CaptureMouse();
        }

        private int HitTestPoint(Point pos)
        {
            for (int i = 0; i < Points.Count; i++)
            {
                var vp = MapToView(Points[i].X, Points[i].Y, ActualWidth, ActualHeight);
                if ((vp - pos).Length < 8) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragIndex < 0) return;
            var cp = ViewToCurve(e.GetPosition(this));
            // Endpoints stay at x=0 / x=255; middle points keep order.
            if (_dragIndex == 0) cp = (0, cp.Y);
            else if (_dragIndex == Points.Count - 1) cp = (255, cp.Y);
            else cp = (Math.Clamp(cp.X, Points[_dragIndex - 1].X + 1, Points[_dragIndex + 1].X - 1), cp.Y);
            Points[_dragIndex] = cp;
            InvalidateVisual();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            _dragIndex = -1;
            ReleaseMouseCapture();
        }
    }
}
