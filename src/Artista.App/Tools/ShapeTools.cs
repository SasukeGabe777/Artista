using System.Windows;
using System.Windows.Media;
using Artista.App.Models;
using Artista.Core.Drawing;
using Artista.Core.History;
using Artista.Core.Imaging;

namespace Artista.App.Tools;

/// <summary>
/// Base for drag-to-draw shape tools. The shape previews live on the layer
/// (restored from a snapshot every update) and commits one history entry on
/// mouse release.
/// </summary>
public abstract class ShapeToolBase : ToolBase
{
    protected bool Dragging;
    protected Point Start, Current;
    private Surface? _snapshot;
    private RectInt _lastDirty = RectInt.Empty;
    private PointerButton _button;

    public override ToolSettingKind[] SettingsBar => new[]
    {
        ToolSettingKind.BrushWidth, ToolSettingKind.FillStyle, ToolSettingKind.Antialias,
    };
    public override string StatusHint =>
        "Drag to draw. Left mouse: primary outline / secondary fill. Right mouse: reversed. Shift constrains.";

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
        Dragging = true;
        _button = e.Button;
        Start = Current = new Point(e.X, e.Y);
        _snapshot = layer.Surface.Clone();
        _lastDirty = RectInt.Empty;
    }

    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        if (!Dragging) return;
        Current = ConstrainPoint(e);
        RenderPreview();
    }

    private Point ConstrainPoint(ToolPointerEventArgs e)
    {
        if ((e.Modifiers & System.Windows.Input.ModifierKeys.Shift) == 0)
            return new Point(e.X, e.Y);
        double dx = e.X - Start.X, dy = e.Y - Start.Y;
        double m = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new Point(Start.X + Math.Sign(dx == 0 ? 1 : dx) * m, Start.Y + Math.Sign(dy == 0 ? 1 : dy) * m);
    }

    protected void RenderPreview()
    {
        var ws = Context.Workspace;
        if (ws == null || _snapshot == null) return;
        var layer = ws.Document.ActiveLayer;

        // Restore what the previous preview touched.
        if (!_lastDirty.IsEmpty)
            layer.Surface.CopyRect(_snapshot, _lastDirty);

        var dirty = DrawShape(layer, _snapshot, ws);
        var invalid = dirty.Union(_lastDirty);
        _lastDirty = dirty;
        if (!invalid.IsEmpty)
            Context.InvalidateDocument(invalid);
    }

    /// <summary>Draws the current shape onto the layer; returns the dirty rect.</summary>
    private RectInt DrawShape(Core.Layers.Layer layer, Surface snapshot, DocumentWorkspace ws)
    {
        var env = Context.Environment;
        uint outlineColor = _button == PointerButton.Right ? env.SecondaryColor : env.PrimaryColor;
        uint fillColor = _button == PointerButton.Right ? env.PrimaryColor : env.SecondaryColor;

        var fill = new StrokeBuffer(ws.Document.Width, ws.Document.Height);
        var outline = new StrokeBuffer(ws.Document.Width, ws.Document.Height);
        BuildShape(fill, outline, env.Antialias);

        var dirty = fill.DirtyRect.Union(outline.DirtyRect).Inflate(2).Intersect(ws.Document.Bounds);
        if (dirty.IsEmpty) return RectInt.Empty;

        var style = env.FillStyle;
        if (style is FillStyle.Fill or FillStyle.FillAndOutline)
            fill.ApplyPaint(layer.Surface, snapshot, ws.Document.Selection, dirty,
                style == FillStyle.Fill ? outlineColor : fillColor, (float)env.Opacity, layer.AlphaLocked);
        if (style is FillStyle.Outline or FillStyle.FillAndOutline)
        {
            // When both, outline applies on top of fill: use current surface as base for outline
            var baseSurface = style == FillStyle.FillAndOutline ? layer.Surface.Clone() : snapshot;
            outline.ApplyPaint(layer.Surface, baseSurface, ws.Document.Selection, dirty,
                outlineColor, (float)env.Opacity, layer.AlphaLocked);
        }
        return dirty;
    }

    /// <summary>Stamp the shape's fill and outline coverage into the buffers.</summary>
    protected abstract void BuildShape(StrokeBuffer fill, StrokeBuffer outline, bool antialias);

    public override void OnPointerUp(ToolPointerEventArgs e)
    {
        if (!Dragging) return;
        Dragging = false;
        var ws = Context.Workspace;
        if (ws == null || _snapshot == null) return;
        Current = ConstrainPoint(e);
        RenderPreview();
        if (!_lastDirty.IsEmpty)
        {
            var before = _snapshot.ExtractRect(_lastDirty);
            Context.PushHistory(new SurfaceRegionMemento(Name, ws.Document.ActiveLayer, _lastDirty, before), IconKey);
            ws.MarkDirty();
        }
        _snapshot = null;
        _lastDirty = RectInt.Empty;
    }

    public override void OnCancel()
    {
        if (!Dragging) return;
        Dragging = false;
        var ws = Context.Workspace;
        if (ws != null && _snapshot != null && !_lastDirty.IsEmpty)
        {
            ws.Document.ActiveLayer.Surface.CopyRect(_snapshot, _lastDirty);
            Context.InvalidateDocument(_lastDirty);
        }
        _snapshot = null;
        _lastDirty = RectInt.Empty;
    }
}

public sealed class LineCurveTool : ShapeToolBase
{
    public override string Name => "Line / Curve";
    public override string IconKey => "Icon.Line";
    public override ToolSettingKind[] SettingsBar => new[]
    {
        ToolSettingKind.BrushWidth, ToolSettingKind.Antialias,
    };
    public override string StatusHint => "Drag to draw a line. Drag again from an endpoint region to curve it. (Simple line; curves via Freeform.)";

    protected override void BuildShape(StrokeBuffer fill, StrokeBuffer outline, bool antialias)
    {
        outline.StampLine(Start.X, Start.Y, Current.X, Current.Y,
            Math.Max(0.5, Context.Environment.BrushWidth / 2), 1.0, antialias);
    }
}

public sealed class CurveTool : ToolBase
{
    public override string Name => "Curve";
    public override string IconKey => "Icon.Line";
    public override ToolSettingKind[] SettingsBar => new[]
    {
        ToolSettingKind.BrushWidth, ToolSettingKind.Antialias,
    };
    public override string StatusHint =>
        "Click to place curve points (up to 8). Enter commits, Escape cancels, right-click removes the last point.";

    private readonly List<(double X, double Y)> _points = new();
    private Surface? _snapshot;
    private RectInt _lastDirty = RectInt.Empty;

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        var ws = Context.Workspace;
        if (ws == null) return;
        var layer = ws.Document.ActiveLayer;
        if (layer.Locked || !layer.Visible) return;

        if (e.Button == PointerButton.Right)
        {
            if (_points.Count > 0) _points.RemoveAt(_points.Count - 1);
            if (_points.Count == 0) { OnCancel(); return; }
        }
        else
        {
            _snapshot ??= layer.Surface.Clone();
            _points.Add((e.X, e.Y));
            if (_points.Count >= 8)
            {
                RenderPreview();
                OnCommit();
                return;
            }
        }
        RenderPreview();
    }

    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        if (_points.Count == 0 || e.Button == PointerButton.None) return;
        _points[^1] = (e.X, e.Y);
        RenderPreview();
    }

    private void RenderPreview()
    {
        var ws = Context.Workspace;
        if (ws == null || _snapshot == null) return;
        var layer = ws.Document.ActiveLayer;
        if (!_lastDirty.IsEmpty)
            layer.Surface.CopyRect(_snapshot, _lastDirty);
        if (_points.Count == 0)
        {
            Context.InvalidateDocument(_lastDirty);
            _lastDirty = RectInt.Empty;
            return;
        }
        var buffer = new StrokeBuffer(ws.Document.Width, ws.Document.Height);
        var path = ShapeRenderer.CurvePath(_points);
        ShapeRenderer.StrokePath(buffer, path, Context.Environment.BrushWidth, false, Context.Environment.Antialias);
        var dirty = buffer.DirtyRect.Inflate(2).Intersect(ws.Document.Bounds);
        buffer.ApplyPaint(layer.Surface, _snapshot, ws.Document.Selection, dirty,
            Context.Environment.PrimaryColor, (float)Context.Environment.Opacity, layer.AlphaLocked);
        var invalid = dirty.Union(_lastDirty);
        _lastDirty = dirty;
        Context.InvalidateDocument(invalid);
    }

    public override void OnCommit()
    {
        var ws = Context.Workspace;
        if (ws == null || _snapshot == null)
        {
            Reset();
            return;
        }
        if (!_lastDirty.IsEmpty)
        {
            var before = _snapshot.ExtractRect(_lastDirty);
            Context.PushHistory(new SurfaceRegionMemento(Name, ws.Document.ActiveLayer, _lastDirty, before), IconKey);
            ws.MarkDirty();
        }
        Reset();
    }

    public override void OnCancel()
    {
        var ws = Context.Workspace;
        if (ws != null && _snapshot != null && !_lastDirty.IsEmpty)
        {
            ws.Document.ActiveLayer.Surface.CopyRect(_snapshot, _lastDirty);
            Context.InvalidateDocument(_lastDirty);
        }
        Reset();
    }

    public override void OnDeactivated() => OnCommit();

    private void Reset()
    {
        _points.Clear();
        _snapshot = null;
        _lastDirty = RectInt.Empty;
    }
}

public sealed class RectangleShapeTool : ShapeToolBase
{
    public override string Name => "Rectangle";
    public override string IconKey => "Icon.Rectangle";

    protected override void BuildShape(StrokeBuffer fill, StrokeBuffer outline, bool antialias)
    {
        var path = ShapeRenderer.RectanglePath(Start.X, Start.Y, Current.X, Current.Y);
        ShapeRenderer.FillPath(fill, path, antialias);
        ShapeRenderer.StrokePath(outline, path, Context.Environment.BrushWidth, true, antialias);
    }
}

public sealed class RoundedRectangleTool : ShapeToolBase
{
    public override string Name => "Rounded Rectangle";
    public override string IconKey => "Icon.RoundedRect";
    public override ToolSettingKind[] SettingsBar => new[]
    {
        ToolSettingKind.BrushWidth, ToolSettingKind.FillStyle, ToolSettingKind.CornerRadius, ToolSettingKind.Antialias,
    };

    protected override void BuildShape(StrokeBuffer fill, StrokeBuffer outline, bool antialias)
    {
        var path = ShapeRenderer.RoundedRectanglePath(Start.X, Start.Y, Current.X, Current.Y,
            Context.Environment.CornerRadius);
        ShapeRenderer.FillPath(fill, path, antialias);
        ShapeRenderer.StrokePath(outline, path, Context.Environment.BrushWidth, true, antialias);
    }
}

public sealed class EllipseShapeTool : ShapeToolBase
{
    public override string Name => "Ellipse";
    public override string IconKey => "Icon.EllipseShape";

    protected override void BuildShape(StrokeBuffer fill, StrokeBuffer outline, bool antialias)
    {
        var path = ShapeRenderer.EllipsePath(Start.X, Start.Y, Current.X, Current.Y);
        ShapeRenderer.FillPath(fill, path, antialias);
        ShapeRenderer.StrokePath(outline, path, Context.Environment.BrushWidth, true, antialias);
    }
}

public sealed class FreeformShapeTool : ToolBase
{
    public override string Name => "Freeform Shape";
    public override string IconKey => "Icon.Freeform";
    public override ToolSettingKind[] SettingsBar => new[]
    {
        ToolSettingKind.BrushWidth, ToolSettingKind.FillStyle, ToolSettingKind.Antialias,
    };
    public override string StatusHint => "Drag to draw a freeform shape; it closes and fills on release.";

    private readonly List<(double X, double Y)> _points = new();
    private Surface? _snapshot;
    private bool _dragging;
    private PointerButton _button;

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        var ws = Context.Workspace;
        if (ws == null) return;
        var layer = ws.Document.ActiveLayer;
        if (layer.Locked || !layer.Visible) return;
        _dragging = true;
        _button = e.Button;
        _points.Clear();
        _points.Add((e.X, e.Y));
        _snapshot = layer.Surface.Clone();
    }

    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        if (!_dragging) return;
        var last = _points[^1];
        if (Math.Abs(e.X - last.X) + Math.Abs(e.Y - last.Y) > 0.75)
        {
            _points.Add((e.X, e.Y));
            Context.InvalidateOverlay();
        }
    }

    public override void OnPointerUp(ToolPointerEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        var ws = Context.Workspace;
        if (ws == null || _snapshot == null || _points.Count < 3)
        {
            _snapshot = null;
            _points.Clear();
            Context.InvalidateOverlay();
            return;
        }
        var layer = ws.Document.ActiveLayer;
        var env = Context.Environment;
        uint outlineColor = _button == PointerButton.Right ? env.SecondaryColor : env.PrimaryColor;
        uint fillColor = _button == PointerButton.Right ? env.PrimaryColor : env.SecondaryColor;

        var fill = new StrokeBuffer(ws.Document.Width, ws.Document.Height);
        var outline = new StrokeBuffer(ws.Document.Width, ws.Document.Height);
        ShapeRenderer.FillPath(fill, _points, env.Antialias);
        ShapeRenderer.StrokePath(outline, _points, env.BrushWidth, true, env.Antialias);
        var dirty = fill.DirtyRect.Union(outline.DirtyRect).Inflate(2).Intersect(ws.Document.Bounds);
        if (!dirty.IsEmpty)
        {
            var style = env.FillStyle;
            if (style is FillStyle.Fill or FillStyle.FillAndOutline)
                fill.ApplyPaint(layer.Surface, _snapshot, ws.Document.Selection, dirty,
                    style == FillStyle.Fill ? outlineColor : fillColor, (float)env.Opacity, layer.AlphaLocked);
            if (style is FillStyle.Outline or FillStyle.FillAndOutline)
            {
                var baseSurface = style == FillStyle.FillAndOutline ? layer.Surface.Clone() : _snapshot;
                outline.ApplyPaint(layer.Surface, baseSurface, ws.Document.Selection, dirty,
                    outlineColor, (float)env.Opacity, layer.AlphaLocked);
            }
            var before = _snapshot.ExtractRect(dirty);
            Context.PushHistory(new SurfaceRegionMemento(Name, layer, dirty, before), IconKey);
            ws.MarkDirty();
            Context.InvalidateDocument(dirty);
        }
        _snapshot = null;
        _points.Clear();
    }

    public override void OnRenderOverlay(DrawingContext dc, CanvasTransform t)
    {
        if (!_dragging || _points.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(t.DocToView(_points[0].X, _points[0].Y), false, false);
            foreach (var p in _points.Skip(1))
                ctx.LineTo(t.DocToView(p.X, p.Y), true, false);
        }
        dc.DrawGeometry(null, new Pen(Brushes.White, 1), geometry);
        dc.DrawGeometry(null, new Pen(Brushes.Black, 1) { DashStyle = DashStyles.Dash }, geometry);
    }
}
