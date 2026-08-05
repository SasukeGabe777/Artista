using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Artista.Core.Drawing;
using Artista.Core.History;
using Artista.Core.Imaging;

namespace Artista.App.Tools;

public sealed class PaintBucketTool : ToolBase
{
    public override string Name => "Paint Bucket";
    public override string IconKey => "Icon.Bucket";
    public override ToolSettingKind[] SettingsBar => new[]
    {
        ToolSettingKind.Tolerance, ToolSettingKind.WandGlobal, ToolSettingKind.Antialias,
    };
    public override string StatusHint =>
        "Click to fill a region with the primary color; right-click fills with the secondary color.";

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        var ws = Context.Workspace;
        if (ws == null) return;
        var layer = ws.Document.ActiveLayer;
        if (layer.Locked || !layer.Visible)
        {
            Context.SetStatus("The active layer is locked or hidden.");
            return;
        }
        uint color = e.Button == PointerButton.Right
            ? Context.Environment.SecondaryColor
            : Context.Environment.PrimaryColor;

        var snapshot = layer.Surface.Clone();
        var dirty = BucketFill.Fill(
            layer.Surface, ws.Document.Selection, e.PixelX, e.PixelY, color,
            Context.Environment.Tolerance, contiguous: !Context.Environment.WandGlobal,
            antialias: Context.Environment.Antialias);
        if (dirty.IsEmpty) return;

        var before = snapshot.ExtractRect(dirty);
        Context.PushHistory(new SurfaceRegionMemento(Name, layer, dirty, before), IconKey);
        ws.MarkDirty();
        Context.InvalidateDocument(dirty);
    }
}

public sealed class GradientTool : ToolBase
{
    public override string Name => "Gradient";
    public override bool IsBusy => _dragging;
    public override string IconKey => "Icon.Gradient";
    public override ToolSettingKind[] SettingsBar => new[]
    {
        ToolSettingKind.GradientShape, ToolSettingKind.GradientToTransparent,
    };
    public override string StatusHint =>
        "Drag to draw a gradient from the primary to the secondary color (right mouse reverses).";

    private bool _dragging;
    private Point _start, _current;
    private Surface? _snapshot;
    private PointerButton _button;

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        var ws = Context.Workspace;
        if (ws == null) return;
        var layer = ws.Document.ActiveLayer;
        if (layer.Locked || !layer.Visible)
        {
            Context.SetStatus("The active layer is locked or hidden.");
            return;
        }
        _dragging = true;
        _button = e.Button;
        _start = _current = new Point(e.X, e.Y);
        _snapshot = layer.Surface.Clone();
    }

    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        if (!_dragging || _snapshot == null) return;
        _current = new Point(e.X, e.Y);
        RenderGradient();
    }

    private void RenderGradient()
    {
        var ws = Context.Workspace;
        if (ws == null || _snapshot == null) return;
        var layer = ws.Document.ActiveLayer;
        var env = Context.Environment;
        uint c0 = _button == PointerButton.Right ? env.SecondaryColor : env.PrimaryColor;
        uint c1 = _button == PointerButton.Right ? env.PrimaryColor : env.SecondaryColor;
        if (env.GradientToTransparent)
            c1 = c0 & 0x00FFFFFFu;

        var roi = ws.Document.Selection.EffectiveBounds;
        layer.Surface.CopyRect(_snapshot, roi);
        GradientRenderer.Render(layer.Surface, _snapshot, ws.Document.Selection, roi,
            env.GradientShape, _start.X, _start.Y, _current.X, _current.Y, c0, c1);
        Context.InvalidateDocument(roi);
    }

    public override void OnPointerUp(ToolPointerEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        var ws = Context.Workspace;
        if (ws == null || _snapshot == null) return;
        _current = new Point(e.X, e.Y);
        RenderGradient();
        var roi = ws.Document.Selection.EffectiveBounds;
        var before = _snapshot.ExtractRect(roi);
        Context.PushHistory(new SurfaceRegionMemento(Name, ws.Document.ActiveLayer, roi, before), IconKey);
        ws.MarkDirty();
        _snapshot = null;
    }

    public override void OnCancel()
    {
        if (!_dragging) return;
        _dragging = false;
        var ws = Context.Workspace;
        if (ws != null && _snapshot != null)
        {
            ws.Document.ActiveLayer.Surface.CopyFrom(_snapshot);
            Context.InvalidateDocument(ws.Document.Bounds);
        }
        _snapshot = null;
    }
}

public sealed class ColorPickerTool : ToolBase
{
    public override string Name => "Color Picker";
    public override string IconKey => "Icon.Picker";
    public override string StatusHint =>
        "Click to set the primary color from the image; right-click sets the secondary color.";
    public override Cursor Cursor => Cursors.Cross;

    public override void OnPointerDown(ToolPointerEventArgs e) => Pick(e);
    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        if (e.Button != PointerButton.None) Pick(e);
    }

    private void Pick(ToolPointerEventArgs e)
    {
        var ws = Context.Workspace;
        if (ws == null || !ws.Document.Bounds.Contains(e.PixelX, e.PixelY)) return;
        uint color = ws.CompositeSurface[e.PixelX, e.PixelY];
        if (ColorBgra.A(color) == 0)
            color = 0xFF000000u | color; // picking fully transparent yields opaque color values
        if (e.Button == PointerButton.Right)
            Context.Environment.SecondaryColor = color;
        else
            Context.Environment.PrimaryColor = color;
    }
}

public sealed class ZoomTool : ToolBase
{
    public override string Name => "Zoom";
    public override string IconKey => "Icon.Zoom";
    public override string StatusHint => "Click to zoom in, right-click to zoom out, drag a rectangle to zoom into it.";
    public override Cursor Cursor => Cursors.Cross;

    private bool _dragging;
    private Point _start, _current;

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        _dragging = true;
        _start = _current = new Point(e.X, e.Y);
    }

    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        if (!_dragging) return;
        _current = new Point(e.X, e.Y);
        Context.InvalidateOverlay();
    }

    public override void OnPointerUp(ToolPointerEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        double w = Math.Abs(_current.X - _start.X);
        double h = Math.Abs(_current.Y - _start.Y);
        if (w > 4 && h > 4)
        {
            Context.ViewZoomToRect(RectInt.FromPoints(
                (int)Math.Min(_start.X, _current.X), (int)Math.Min(_start.Y, _current.Y),
                (int)Math.Max(_start.X, _current.X), (int)Math.Max(_start.Y, _current.Y)));
        }
        else if (e.Button == PointerButton.Right)
        {
            Context.ViewZoomOutAt(new Point(e.X, e.Y));
        }
        else
        {
            Context.ViewZoomInAt(new Point(e.X, e.Y));
        }
        Context.InvalidateOverlay();
    }

    public override void OnRenderOverlay(DrawingContext dc, CanvasTransform t)
    {
        if (!_dragging) return;
        var rect = new Rect(t.DocToView(_start.X, _start.Y), t.DocToView(_current.X, _current.Y));
        dc.DrawRectangle(null, new Pen(Brushes.White, 1), rect);
        dc.DrawRectangle(null, new Pen(Brushes.Black, 1) { DashStyle = DashStyles.Dash }, rect);
    }
}

public sealed class PanTool : ToolBase
{
    public override string Name => "Pan";
    public override string IconKey => "Icon.Pan";
    public override string StatusHint => "Drag to pan the view. Tip: hold Space with any tool to pan.";
    public override Cursor Cursor => Cursors.Hand;

    private bool _dragging;
    private Point _lastDoc;

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        _dragging = true;
        _lastDoc = new Point(e.X, e.Y);
    }

    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        if (!_dragging) return;
        double dx = (e.X - _lastDoc.X) * Context.ZoomFactor;
        double dy = (e.Y - _lastDoc.Y) * Context.ZoomFactor;
        Context.ViewPanBy(dx, dy);
        // Note: after panning, the same mouse position maps to a new doc coord;
        // don't update _lastDoc (the pan moves the coordinate system under it).
    }

    public override void OnPointerUp(ToolPointerEventArgs e) => _dragging = false;
}
