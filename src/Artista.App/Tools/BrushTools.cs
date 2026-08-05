using System.Windows.Input;
using System.Windows.Media;
using Artista.Core.ColorEngine;
using Artista.Core.Drawing;
using Artista.Core.History;
using Artista.Core.Imaging;

namespace Artista.App.Tools;

/// <summary>
/// Base for stroke-based tools: snapshots the layer at stroke start, stamps
/// into a StrokeBuffer while dragging, applies against the snapshot, and
/// pushes exactly one history entry per stroke.
/// </summary>
public abstract class StrokeToolBase : ToolBase
{
    protected Surface? Snapshot;
    protected StrokeBuffer? Stroke;
    protected uint StrokeColor;
    protected PointerButton StrokeButton;
    private double _lastX, _lastY;
    private bool _active;

    public override bool IsBusy => _active;

    protected virtual double DabRadius => Context.Environment.BrushWidth / 2;
    protected virtual double DabHardness => Context.Environment.Hardness;
    protected virtual bool DabAntialias => Context.Environment.Antialias;

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
        _active = true;
        StrokeButton = e.Button;
        StrokeColor = e.Button == PointerButton.Right
            ? Context.Environment.SecondaryColor
            : Context.Environment.PrimaryColor;
        Snapshot = layer.Surface.Clone();
        Stroke = new StrokeBuffer(ws.Document.Width, ws.Document.Height);
        _lastX = e.X;
        _lastY = e.Y;
        OnStrokeStart(e);
        StampAndApply(e.X, e.Y, e.X, e.Y);
    }

    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        if (!_active || e.Button == PointerButton.None) return;
        StampAndApply(_lastX, _lastY, e.X, e.Y);
        _lastX = e.X;
        _lastY = e.Y;
    }

    private void StampAndApply(double x0, double y0, double x1, double y1)
    {
        var ws = Context.Workspace;
        if (ws == null || Stroke == null || Snapshot == null) return;
        var layer = ws.Document.ActiveLayer;

        var beforeDirty = Stroke.DirtyRect;
        Stroke.StampLine(x0, y0, x1, y1, DabRadius, DabHardness, DabAntialias);
        var newDirty = Stroke.DirtyRect;
        // Re-apply over the whole stroke dirty rect (coverage is monotonic so
        // only the changed region really needs it; use union of segment bounds).
        var segment = RectInt.FromPoints(
            (int)Math.Floor(Math.Min(x0, x1) - DabRadius - 2), (int)Math.Floor(Math.Min(y0, y1) - DabRadius - 2),
            (int)Math.Ceiling(Math.Max(x0, x1) + DabRadius + 2), (int)Math.Ceiling(Math.Max(y0, y1) + DabRadius + 2));
        var applyRect = segment.Intersect(newDirty).Intersect(ws.Document.Bounds);
        if (applyRect.IsEmpty) return;
        ApplyStroke(layer, applyRect);
        ws.MarkDirty();
        Context.InvalidateDocument(applyRect);
    }

    public override void OnPointerUp(ToolPointerEventArgs e)
    {
        if (!_active) return;
        _active = false;
        var ws = Context.Workspace;
        if (ws == null || Stroke == null || Snapshot == null) return;
        var dirty = Stroke.DirtyRect.Intersect(ws.Document.Bounds);
        if (!dirty.IsEmpty)
        {
            var layer = ws.Document.ActiveLayer;
            var before = Snapshot.ExtractRect(dirty);
            Context.PushHistory(new SurfaceRegionMemento(Name, layer, dirty, before), IconKey);
        }
        Snapshot = null;
        Stroke = null;
        OnStrokeEnd();
    }

    public override void OnCancel()
    {
        if (!_active) return;
        _active = false;
        var ws = Context.Workspace;
        if (ws != null && Snapshot != null && Stroke != null)
        {
            var layer = ws.Document.ActiveLayer;
            var dirty = Stroke.DirtyRect.Intersect(ws.Document.Bounds);
            if (!dirty.IsEmpty)
            {
                layer.Surface.CopyRect(Snapshot, dirty);
                Context.InvalidateDocument(dirty);
            }
        }
        Snapshot = null;
        Stroke = null;
    }

    protected virtual void OnStrokeStart(ToolPointerEventArgs e) { }
    protected virtual void OnStrokeEnd() { }

    /// <summary>Blend the current stroke coverage into the layer for the given rect.</summary>
    protected abstract void ApplyStroke(Core.Layers.Layer layer, RectInt rect);

    public override void OnRenderOverlay(DrawingContext dc, CanvasTransform t)
    {
        // Brush size outline following the cursor is drawn by MainWindow's status
        // via cursor; keep simple.
    }
}

public sealed class PaintbrushTool : StrokeToolBase
{
    public override string Name => "Paintbrush";
    public override string IconKey => "Icon.Brush";
    public override ToolSettingKind[] SettingsBar => new[]
    {
        ToolSettingKind.BrushWidth, ToolSettingKind.Hardness, ToolSettingKind.Opacity, ToolSettingKind.Antialias,
    };
    public override string StatusHint => "Left mouse paints with the primary color, right mouse with the secondary color.";

    protected override void ApplyStroke(Core.Layers.Layer layer, RectInt rect) =>
        Stroke!.ApplyPaint(layer.Surface, Snapshot!, Context.Workspace!.Document.Selection, rect,
            StrokeColor, (float)Context.Environment.Opacity, layer.AlphaLocked);
}

public sealed class PencilTool : StrokeToolBase
{
    public override string Name => "Pencil";
    public override string IconKey => "Icon.Pencil";
    public override ToolSettingKind[] SettingsBar => Array.Empty<ToolSettingKind>();
    public override string StatusHint => "Draws hard-edged single pixels. Left = primary color, right = secondary.";

    protected override double DabRadius => 0.5;
    protected override double DabHardness => 1.0;
    protected override bool DabAntialias => false;

    protected override void ApplyStroke(Core.Layers.Layer layer, RectInt rect) =>
        Stroke!.ApplyPaint(layer.Surface, Snapshot!, Context.Workspace!.Document.Selection, rect,
            StrokeColor, 1f, layer.AlphaLocked);
}

public sealed class EraserTool : StrokeToolBase
{
    public override string Name => "Eraser";
    public override string IconKey => "Icon.Eraser";
    public override ToolSettingKind[] SettingsBar => new[]
    {
        ToolSettingKind.BrushWidth, ToolSettingKind.Hardness, ToolSettingKind.Opacity, ToolSettingKind.Antialias,
    };
    public override string StatusHint => "Erases pixels to transparency.";

    protected override void ApplyStroke(Core.Layers.Layer layer, RectInt rect) =>
        Stroke!.ApplyErase(layer.Surface, Snapshot!, Context.Workspace!.Document.Selection, rect,
            (float)Context.Environment.Opacity);
}

/// <summary>
/// The Color Remover brush: erases only pixels matching the target color.
/// Shares the ColorMatcher engine with Effects → Transparency → Remove Color.
/// </summary>
public sealed class ColorRemoverTool : StrokeToolBase
{
    public override string Name => "Color Remover";
    public override string IconKey => "Icon.ColorRemover";
    public override ToolSettingKind[] SettingsBar => new[]
    {
        ToolSettingKind.BrushWidth, ToolSettingKind.Hardness, ToolSettingKind.Opacity,
        ToolSettingKind.Tolerance, ToolSettingKind.Softness, ToolSettingKind.SampleModes,
    };
    public override string StatusHint =>
        "Brushes away the target color only. Left = remove secondary color (or sampled), right-click samples the target.";

    private ColorMatcher? _matcher;
    private uint _target;

    protected override void OnStrokeStart(ToolPointerEventArgs e)
    {
        var env = Context.Environment;
        // Fixed target = secondary color unless sampling from first click.
        if (env.SampleFromClick)
        {
            var ws = Context.Workspace!;
            if (ws.Document.Bounds.Contains(e.PixelX, e.PixelY))
            {
                uint sample = ws.Document.ActiveLayer.Surface[e.PixelX, e.PixelY];
                // A fully transparent pixel has no meaningful color — keep the
                // previous target so the stroke still does what the user expects.
                if (ColorBgra.A(sample) > 0)
                    _target = sample;
            }
        }
        else
        {
            _target = env.SecondaryColor;
        }
        _matcher = ColorMatcher.FromBgra(_target, env.Tolerance, env.Softness);
    }

    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        // Continuous sampling mode re-targets from the pixel under the cursor.
        if (Context.Environment.SampleContinuously && e.Button != PointerButton.None &&
            Context.Workspace != null && Context.Workspace.Document.Bounds.Contains(e.PixelX, e.PixelY) &&
            Snapshot != null)
        {
            uint sample = Snapshot[e.PixelX, e.PixelY];
            if (ColorBgra.A(sample) > 0 && sample != _target)
            {
                _target = sample;
                _matcher = ColorMatcher.FromBgra(_target, Context.Environment.Tolerance, Context.Environment.Softness);
            }
        }
        base.OnPointerMove(e);
    }

    protected override void ApplyStroke(Core.Layers.Layer layer, RectInt rect)
    {
        if (_matcher == null) return;
        Stroke!.ApplyColorRemove(layer.Surface, Snapshot!, Context.Workspace!.Document.Selection, rect,
            _matcher, (float)Context.Environment.Opacity);
    }
}

public sealed class RecolorTool : StrokeToolBase
{
    public override string Name => "Recolor";
    public override string IconKey => "Icon.Recolor";
    public override ToolSettingKind[] SettingsBar => new[]
    {
        ToolSettingKind.BrushWidth, ToolSettingKind.Hardness, ToolSettingKind.Opacity, ToolSettingKind.Tolerance,
    };
    public override string StatusHint =>
        "Left mouse replaces the secondary color with the primary; right mouse replaces primary with secondary.";

    private ColorMatcher? _matcher;
    private uint _paint;

    protected override void OnStrokeStart(ToolPointerEventArgs e)
    {
        var env = Context.Environment;
        uint target = e.Button == PointerButton.Right ? env.PrimaryColor : env.SecondaryColor;
        _paint = e.Button == PointerButton.Right ? env.SecondaryColor : env.PrimaryColor;
        _matcher = ColorMatcher.FromBgra(target, env.Tolerance, env.Tolerance / 2);
    }

    protected override void ApplyStroke(Core.Layers.Layer layer, RectInt rect)
    {
        if (_matcher == null) return;
        Stroke!.ApplyRecolor(layer.Surface, Snapshot!, Context.Workspace!.Document.Selection, rect,
            _matcher, _paint, (float)Context.Environment.Opacity);
    }
}

public sealed class CloneStampTool : StrokeToolBase
{
    public override string Name => "Clone Stamp";
    public override string IconKey => "Icon.CloneStamp";
    public override ToolSettingKind[] SettingsBar => new[]
    {
        ToolSettingKind.BrushWidth, ToolSettingKind.Hardness, ToolSettingKind.Opacity, ToolSettingKind.Antialias,
    };
    public override string StatusHint => "Ctrl+click to set the source, then paint to clone from it.";

    private int _sourceX = -1, _sourceY = -1;
    private int _offsetX, _offsetY;
    private bool _hasOffset;

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        if ((e.Modifiers & ModifierKeys.Control) != 0)
        {
            _sourceX = e.PixelX;
            _sourceY = e.PixelY;
            _hasOffset = false;
            Context.SetStatus($"Clone source set to ({_sourceX}, {_sourceY}).");
            Context.InvalidateOverlay();
            return;
        }
        if (_sourceX < 0)
        {
            Context.SetStatus("Ctrl+click first to set the clone source.");
            return;
        }
        if (!_hasOffset)
        {
            _offsetX = _sourceX - e.PixelX;
            _offsetY = _sourceY - e.PixelY;
            _hasOffset = true;
        }
        base.OnPointerDown(e);
    }

    protected override void ApplyStroke(Core.Layers.Layer layer, RectInt rect) =>
        Stroke!.ApplyClone(layer.Surface, Snapshot!, Context.Workspace!.Document.Selection, rect,
            _offsetX, _offsetY, (float)Context.Environment.Opacity);

    public override void OnRenderOverlay(DrawingContext dc, CanvasTransform t)
    {
        if (_sourceX < 0) return;
        var p = t.DocToView(_sourceX + 0.5, _sourceY + 0.5);
        var pen = new Pen(Brushes.White, 1.5);
        dc.DrawEllipse(null, pen, p, 6, 6);
        var penB = new Pen(Brushes.Black, 1.5) { DashStyle = DashStyles.Dash };
        dc.DrawEllipse(null, penB, p, 6, 6);
    }
}
