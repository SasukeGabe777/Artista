using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Artista.App.Models;
using Artista.App.Tools;
using Artista.Core.Imaging;

namespace Artista.App.Controls;

/// <summary>
/// Hosts the CanvasView with scrollbars and optional rulers; owns zooming,
/// panning and pointer routing to the active tool.
/// </summary>
public sealed class DocumentView : Grid
{
    public CanvasView Canvas { get; } = new();
    private readonly ScrollBar _hScroll = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollBar _vScroll = new() { Orientation = Orientation.Vertical };
    private readonly RulerView _hRuler = new() { Orientation = Orientation.Horizontal };
    private readonly RulerView _vRuler = new() { Orientation = Orientation.Vertical };
    private readonly Border _rulerCorner = new();

    public DocumentWorkspace? Workspace { get; private set; }

    public event EventHandler<Point>? CursorMoved;   // document coords
    public event EventHandler? ZoomChanged;

    private bool _middlePanning;
    private Point _panStartView;
    private double _panStartOffsetX, _panStartOffsetY;
    private bool _pointerDown;
    private PointerButton _pointerButton;
    private bool _showRulers;

    public double Zoom => Canvas.Zoom;

    public bool ShowRulers
    {
        get => _showRulers;
        set
        {
            _showRulers = value;
            _hRuler.Visibility = _vRuler.Visibility = _rulerCorner.Visibility =
                value ? Visibility.Visible : Visibility.Collapsed;
            RowDefinitions[0].Height = new GridLength(value ? 22 : 0);
            ColumnDefinitions[0].Width = new GridLength(value ? 22 : 0);
        }
    }

    public DocumentView()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });        // ruler
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });          // hscroll
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });   // ruler
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // vscroll

        SetRow(Canvas, 1); SetColumn(Canvas, 1);
        SetRow(_hScroll, 2); SetColumn(_hScroll, 1);
        SetRow(_vScroll, 1); SetColumn(_vScroll, 2);
        SetRow(_hRuler, 0); SetColumn(_hRuler, 1);
        SetRow(_vRuler, 1); SetColumn(_vRuler, 0);
        SetRow(_rulerCorner, 0); SetColumn(_rulerCorner, 0);
        Children.Add(Canvas);
        Children.Add(_hScroll);
        Children.Add(_vScroll);
        Children.Add(_hRuler);
        Children.Add(_vRuler);
        Children.Add(_rulerCorner);
        _hRuler.Visibility = _vRuler.Visibility = _rulerCorner.Visibility = Visibility.Collapsed;

        _hScroll.Scroll += (_, e) => { Canvas.OffsetX = -e.NewValue; AfterViewChanged(false); };
        _vScroll.Scroll += (_, e) => { Canvas.OffsetY = -e.NewValue; AfterViewChanged(false); };

        Canvas.SizeChanged += (_, _) => { ClampOffsets(); AfterViewChanged(false); };

        Canvas.MouseDown += OnCanvasMouseDown;
        Canvas.MouseMove += OnCanvasMouseMove;
        Canvas.MouseUp += OnCanvasMouseUp;
        Canvas.MouseWheel += OnCanvasMouseWheel;
        Canvas.MouseLeave += (_, _) => CursorMoved?.Invoke(this, new Point(double.NaN, double.NaN));
        Background = Brushes.Transparent;
        Focusable = true;
    }

    public void SetWorkspace(DocumentWorkspace? ws)
    {
        if (Workspace != null)
        {
            Workspace.ZoomFactor = Canvas.Zoom;
            Workspace.ScrollX = Canvas.OffsetX;
            Workspace.ScrollY = Canvas.OffsetY;
        }
        Workspace = ws;
        Canvas.SetWorkspace(ws);
        if (ws != null)
        {
            Canvas.Zoom = ws.ZoomFactor;
            Canvas.OffsetX = ws.ScrollX;
            Canvas.OffsetY = ws.ScrollY;
            if (ws.ScrollX == 0 && ws.ScrollY == 0 && Math.Abs(ws.ZoomFactor - 1.0) < 0.001)
                FitToWindowIfLarger();
        }
        AfterViewChanged(true);
    }

    // ---------------- zoom & pan ----------------

    public void SetZoom(double newZoom, Point? viewCenter = null)
    {
        newZoom = Math.Clamp(newZoom, 0.03, 64);
        var center = viewCenter ?? new Point(Canvas.ActualWidth / 2, Canvas.ActualHeight / 2);
        var docAtCenter = Canvas.ViewToDoc(center);
        Canvas.Zoom = newZoom;
        Canvas.OffsetX = center.X - docAtCenter.X * newZoom;
        Canvas.OffsetY = center.Y - docAtCenter.Y * newZoom;
        ClampOffsets();
        AfterViewChanged(true);
    }

    public void ZoomIn() => SetZoom(NextZoomStep(Zoom, up: true));
    public void ZoomOut() => SetZoom(NextZoomStep(Zoom, up: false));

    private static readonly double[] ZoomSteps =
        { 0.05, 0.08, 0.12, 0.16, 0.25, 0.33, 0.5, 0.66, 1, 1.5, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64 };

    private static double NextZoomStep(double current, bool up)
    {
        if (up)
        {
            foreach (var s in ZoomSteps)
                if (s > current * 1.001) return s;
            return ZoomSteps[^1];
        }
        for (int i = ZoomSteps.Length - 1; i >= 0; i--)
            if (ZoomSteps[i] < current * 0.999) return ZoomSteps[i];
        return ZoomSteps[0];
    }

    public void ActualSize() => SetZoom(1.0);

    public void FitToWindow()
    {
        if (Workspace == null || Canvas.ActualWidth < 10) return;
        double margin = 24;
        double zx = (Canvas.ActualWidth - margin) / Workspace.Document.Width;
        double zy = (Canvas.ActualHeight - margin) / Workspace.Document.Height;
        SetZoom(Math.Min(zx, zy));
        CenterImage();
    }

    private void FitToWindowIfLarger()
    {
        if (Workspace == null) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (Workspace == null || Canvas.ActualWidth < 10) return;
            if (Workspace.Document.Width * 1.0 > Canvas.ActualWidth - 24 ||
                Workspace.Document.Height * 1.0 > Canvas.ActualHeight - 24)
                FitToWindow();
            else
                CenterImage();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void CenterImage()
    {
        if (Workspace == null) return;
        Canvas.OffsetX = (Canvas.ActualWidth - Workspace.Document.Width * Zoom) / 2;
        Canvas.OffsetY = (Canvas.ActualHeight - Workspace.Document.Height * Zoom) / 2;
        AfterViewChanged(false);
    }

    public void PanBy(double viewDx, double viewDy) =>
        SetPanOffsets(Canvas.OffsetX + viewDx, Canvas.OffsetY + viewDy);

    private void SetPanOffsets(double offsetX, double offsetY)
    {
        Canvas.OffsetX = offsetX;
        Canvas.OffsetY = offsetY;
        ClampOffsets();
        AfterViewChanged(false);
    }

    private void ClampOffsets()
    {
        if (Workspace == null) return;
        double imgW = Workspace.Document.Width * Zoom;
        double imgH = Workspace.Document.Height * Zoom;
        double viewW = Canvas.ActualWidth, viewH = Canvas.ActualHeight;
        const double slack = 48; // let the user push the image a bit past the edge

        // Use the same padded bounds for images both larger and smaller than
        // the viewport. The old small-image branch forcibly re-centered the
        // canvas on every move, which made middle-drag and the Pan tool appear
        // nonfunctional at common zoom levels.
        double xEdgeA = slack;
        double xEdgeB = viewW - imgW - slack;
        double yEdgeA = slack;
        double yEdgeB = viewH - imgH - slack;
        Canvas.OffsetX = Math.Clamp(Canvas.OffsetX, Math.Min(xEdgeA, xEdgeB), Math.Max(xEdgeA, xEdgeB));
        Canvas.OffsetY = Math.Clamp(Canvas.OffsetY, Math.Min(yEdgeA, yEdgeB), Math.Max(yEdgeA, yEdgeB));
    }

    private void AfterViewChanged(bool zoomChanged)
    {
        UpdateScrollBars();
        _hRuler.Update(Canvas.Zoom, Canvas.OffsetX);
        _vRuler.Update(Canvas.Zoom, Canvas.OffsetY);
        Canvas.InvalidateVisual();
        if (zoomChanged) ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateScrollBars()
    {
        if (Workspace == null)
        {
            _hScroll.Visibility = _vScroll.Visibility = Visibility.Collapsed;
            return;
        }
        double imgW = Workspace.Document.Width * Zoom;
        double imgH = Workspace.Document.Height * Zoom;
        double viewW = Canvas.ActualWidth, viewH = Canvas.ActualHeight;

        _hScroll.Visibility = imgW > viewW ? Visibility.Visible : Visibility.Collapsed;
        _vScroll.Visibility = imgH > viewH ? Visibility.Visible : Visibility.Collapsed;
        if (imgW > viewW)
        {
            _hScroll.Minimum = -48;
            _hScroll.Maximum = imgW - viewW + 48;
            _hScroll.ViewportSize = viewW;
            _hScroll.Value = -Canvas.OffsetX;
        }
        if (imgH > viewH)
        {
            _vScroll.Minimum = -48;
            _vScroll.Maximum = imgH - viewH + 48;
            _vScroll.ViewportSize = viewH;
            _vScroll.Value = -Canvas.OffsetY;
        }
    }

    // ---------------- input ----------------

    public bool SpaceDown { get; set; }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        Canvas.Focus();
        var viewPos = e.GetPosition(Canvas);

        if (e.ChangedButton == MouseButton.Middle || (SpaceDown && e.ChangedButton == MouseButton.Left))
        {
            _middlePanning = true;
            _panStartView = viewPos;
            _panStartOffsetX = Canvas.OffsetX;
            _panStartOffsetY = Canvas.OffsetY;
            Canvas.CaptureMouse();
            Canvas.Cursor = Cursors.ScrollAll;
            e.Handled = true;
            return;
        }

        if (Workspace == null || Canvas.ActiveTool == null) return;
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;

        _pointerDown = true;
        _pointerButton = e.ChangedButton == MouseButton.Left ? PointerButton.Left : PointerButton.Right;
        Canvas.CaptureMouse();
        var doc = Canvas.ViewToDoc(viewPos);
        Canvas.ActiveTool.OnPointerDown(new ToolPointerEventArgs
        {
            X = doc.X, Y = doc.Y, Button = _pointerButton, Modifiers = Keyboard.Modifiers,
        });
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var viewPos = e.GetPosition(Canvas);
        if (_middlePanning)
        {
            SetPanOffsets(
                _panStartOffsetX + (viewPos.X - _panStartView.X),
                _panStartOffsetY + (viewPos.Y - _panStartView.Y));
            return;
        }
        var doc = Canvas.ViewToDoc(viewPos);
        CursorMoved?.Invoke(this, doc);
        if (Workspace == null || Canvas.ActiveTool == null) return;
        Canvas.ActiveTool.OnPointerMove(new ToolPointerEventArgs
        {
            X = doc.X, Y = doc.Y,
            Button = _pointerDown ? _pointerButton : PointerButton.None,
            Modifiers = Keyboard.Modifiers,
        });
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_middlePanning)
        {
            _middlePanning = false;
            Canvas.ReleaseMouseCapture();
            Canvas.Cursor = Canvas.ActiveTool?.Cursor ?? Cursors.Arrow;
            return;
        }
        if (!_pointerDown) return;
        _pointerDown = false;
        Canvas.ReleaseMouseCapture();
        if (Workspace == null || Canvas.ActiveTool == null) return;
        var doc = Canvas.ViewToDoc(e.GetPosition(Canvas));
        Canvas.ActiveTool.OnPointerUp(new ToolPointerEventArgs
        {
            X = doc.X, Y = doc.Y, Button = _pointerButton, Modifiers = Keyboard.Modifiers,
        });
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var viewPos = e.GetPosition(Canvas);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            // Ctrl+wheel zooms, centered on the cursor.
            SetZoom(e.Delta > 0 ? NextZoomStep(Zoom, true) : NextZoomStep(Zoom, false), viewPos);
        }
        else
        {
            // Plain wheel scrolls vertically, Shift+wheel horizontally
            // (Paint.NET behavior). Works while middle-button panning too:
            // shift the pan baseline by the same amount so there is no jump.
            double delta = e.Delta * 0.75;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                Canvas.OffsetX += delta;
                _panStartOffsetX += delta;
            }
            else
            {
                Canvas.OffsetY += delta;
                _panStartOffsetY += delta;
            }
            ClampOffsets();
            AfterViewChanged(false);
        }
        e.Handled = true;
    }
}

/// <summary>Simple pixel-coordinate ruler strip.</summary>
public sealed class RulerView : FrameworkElement
{
    public Orientation Orientation { get; set; }
    private double _zoom = 1.0;
    private double _offset;

    public void Update(double zoom, double offset)
    {
        _zoom = zoom;
        _offset = offset;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bg = TryFindResource("PanelBackgroundBrush") as Brush ?? Brushes.LightGray;
        var fg = TryFindResource("ForegroundDimBrush") as Brush ?? Brushes.Gray;
        dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var pen = new Pen(fg, 1);
        pen.Freeze();

        double length = Orientation == Orientation.Horizontal ? ActualWidth : ActualHeight;
        // Pick a tick interval that yields ticks every >= 50 view px.
        double docPerTick = 50 / Math.Max(0.001, _zoom);
        double magnitude = Math.Pow(10, Math.Ceiling(Math.Log10(docPerTick)));
        foreach (var div in new[] { magnitude / 5, magnitude / 2, magnitude })
        {
            if (div * _zoom >= 40) { docPerTick = div; break; }
            docPerTick = magnitude;
        }

        double firstDoc = Math.Floor((0 - _offset) / _zoom / docPerTick) * docPerTick;
        var typeface = new Typeface("Segoe UI");
        for (double d = firstDoc; ; d += docPerTick)
        {
            double v = d * _zoom + _offset;
            if (v > length) break;
            if (v < 0) continue;
            if (Orientation == Orientation.Horizontal)
            {
                dc.DrawLine(pen, new Point(v + 0.5, ActualHeight - 7), new Point(v + 0.5, ActualHeight));
                var text = new FormattedText(((int)d).ToString(), System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 9, fg, 1.25);
                dc.DrawText(text, new Point(v + 3, 1));
            }
            else
            {
                dc.DrawLine(pen, new Point(ActualWidth - 7, v + 0.5), new Point(ActualWidth, v + 0.5));
                var text = new FormattedText(((int)d).ToString(), System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 9, fg, 1.25);
                dc.PushTransform(new RotateTransform(-90, 8, v));
                dc.DrawText(text, new Point(8 - text.Width, v - 11));
                dc.Pop();
            }
        }
    }
}
