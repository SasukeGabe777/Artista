using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Artista.Core.History;
using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.App.Tools;

/// <summary>Shared logic for drag-defined selection shapes.</summary>
public abstract class SelectionToolBase : ToolBase
{
    protected bool Dragging;

    public override bool IsBusy => Dragging;
    protected Point Start;
    protected Point Current;
    private byte[]? _maskBefore;

    public override ToolSettingKind[] SettingsBar => new[] { ToolSettingKind.CombineMode, ToolSettingKind.Feather };
    public override string StatusHint =>
        "Click and drag to select. Hold Ctrl to add, Alt to subtract. Shift constrains proportions.";

    protected SelectionCombineMode EffectiveCombineMode(ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Control) != 0) return SelectionCombineMode.Add;
        if ((modifiers & ModifierKeys.Alt) != 0) return SelectionCombineMode.Subtract;
        return Context.Environment.CombineMode;
    }

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        if (Context.Workspace == null) return;
        Dragging = true;
        Start = Current = new Point(e.X, e.Y);
        _maskBefore = Context.Workspace.Document.Selection.SnapshotMask();
        Context.InvalidateOverlay();
    }

    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        if (!Dragging) return;
        Current = new Point(e.X, e.Y);
        if ((e.Modifiers & ModifierKeys.Shift) != 0)
        {
            // Constrain to square/circle.
            double dx = Current.X - Start.X, dy = Current.Y - Start.Y;
            double m = Math.Max(Math.Abs(dx), Math.Abs(dy));
            Current = new Point(Start.X + Math.Sign(dx == 0 ? 1 : dx) * m, Start.Y + Math.Sign(dy == 0 ? 1 : dy) * m);
        }
        Context.InvalidateOverlay();
    }

    public override void OnPointerUp(ToolPointerEventArgs e)
    {
        if (!Dragging || Context.Workspace == null) return;
        Dragging = false;
        var doc = Context.Workspace.Document;
        var mode = EffectiveCombineMode(e.Modifiers);

        // A plain click (no meaningful drag) in Replace mode deselects,
        // like Paint.NET.
        if (Math.Abs(Current.X - Start.X) < 2 && Math.Abs(Current.Y - Start.Y) < 2 &&
            mode == SelectionCombineMode.Replace && e.Modifiers == ModifierKeys.None)
        {
            if (!doc.Selection.IsEmpty)
            {
                doc.Selection.Clear();
                Context.PushHistory(new SelectionMemento("Deselect", _maskBefore!), "Icon.Deselect");
                Context.NotifySelectionChanged();
            }
            _maskBefore = null;
            Context.InvalidateOverlay();
            return;
        }

        var mask = BuildMask(doc.Width, doc.Height);
        doc.Selection.Combine(mask, mode);
        if (Context.Environment.Feather > 0)
            doc.Selection.Feather((int)Context.Environment.Feather);
        Context.PushHistory(new SelectionMemento(Name, _maskBefore!), IconKey);
        _maskBefore = null;
        Context.NotifySelectionChanged();
        Context.InvalidateOverlay();
    }

    public override void OnCancel()
    {
        Dragging = false;
        _maskBefore = null;
        Context.InvalidateOverlay();
    }

    protected abstract byte[] BuildMask(int width, int height);

    public override void OnRenderOverlay(DrawingContext dc, CanvasTransform t)
    {
        if (!Dragging) return;
        var p0 = t.DocToView(Start.X, Start.Y);
        var p1 = t.DocToView(Current.X, Current.Y);
        var rect = new Rect(p0, p1);
        var pen = new Pen(Brushes.White, 1);
        var penDash = new Pen(Brushes.Black, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        DrawShape(dc, rect, pen);
        DrawShape(dc, rect, penDash);
    }

    protected virtual void DrawShape(DrawingContext dc, Rect rect, Pen pen) => dc.DrawRectangle(null, pen, rect);
}

public sealed class RectangleSelectTool : SelectionToolBase
{
    public override string Name => "Rectangle Select";
    public override string IconKey => "Icon.RectSelect";

    protected override byte[] BuildMask(int width, int height) =>
        SelectionRasterizer.RasterizeRectangle(width, height, Start.X, Start.Y, Current.X, Current.Y);
}

public sealed class EllipseSelectTool : SelectionToolBase
{
    public override string Name => "Ellipse Select";
    public override string IconKey => "Icon.EllipseSelect";

    protected override byte[] BuildMask(int width, int height) =>
        SelectionRasterizer.RasterizeEllipse(width, height, Start.X, Start.Y, Current.X, Current.Y);

    protected override void DrawShape(DrawingContext dc, Rect rect, Pen pen) =>
        dc.DrawEllipse(null, pen, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2);
}

public sealed class LassoSelectTool : ToolBase
{
    public override string Name => "Lasso Select";
    public override bool IsBusy => _dragging;
    public override string IconKey => "Icon.Lasso";
    public override ToolSettingKind[] SettingsBar => new[] { ToolSettingKind.CombineMode, ToolSettingKind.Feather };
    public override string StatusHint => "Click and drag to draw a freeform selection.";

    private readonly List<(double X, double Y)> _points = new();
    private bool _dragging;
    private byte[]? _maskBefore;

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        if (Context.Workspace == null) return;
        _dragging = true;
        _points.Clear();
        _points.Add((e.X, e.Y));
        _maskBefore = Context.Workspace.Document.Selection.SnapshotMask();
    }

    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        if (!_dragging) return;
        var last = _points[^1];
        if (Math.Abs(e.X - last.X) + Math.Abs(e.Y - last.Y) > 0.5)
        {
            _points.Add((e.X, e.Y));
            Context.InvalidateOverlay();
        }
    }

    public override void OnPointerUp(ToolPointerEventArgs e)
    {
        if (!_dragging || Context.Workspace == null) return;
        _dragging = false;
        var doc = Context.Workspace.Document;
        SelectionCombineMode mode = Context.Environment.CombineMode;
        if ((e.Modifiers & ModifierKeys.Control) != 0) mode = SelectionCombineMode.Add;
        else if ((e.Modifiers & ModifierKeys.Alt) != 0) mode = SelectionCombineMode.Subtract;

        var start = _points[0];
        bool plainClick = Math.Abs(e.X - start.X) < 2 && Math.Abs(e.Y - start.Y) < 2;
        if (plainClick && mode == SelectionCombineMode.Replace && e.Modifiers == ModifierKeys.None)
        {
            if (!doc.Selection.IsEmpty)
            {
                doc.Selection.Clear();
                Context.PushHistory(new SelectionMemento("Deselect", _maskBefore!), "Icon.Deselect");
                Context.NotifySelectionChanged();
            }
            _points.Clear();
            _maskBefore = null;
            Context.InvalidateOverlay();
            return;
        }

        if (_points.Count >= 3)
        {
            var mask = SelectionRasterizer.RasterizePolygon(doc.Width, doc.Height, _points);
            doc.Selection.Combine(mask, mode);
            if (Context.Environment.Feather > 0)
                doc.Selection.Feather((int)Context.Environment.Feather);
            Context.PushHistory(new SelectionMemento(Name, _maskBefore!), IconKey);
            Context.NotifySelectionChanged();
        }
        _points.Clear();
        _maskBefore = null;
        Context.InvalidateOverlay();
    }

    public override void OnCancel()
    {
        _dragging = false;
        _points.Clear();
        _maskBefore = null;
        Context.InvalidateOverlay();
    }

    public override void OnRenderOverlay(DrawingContext dc, CanvasTransform t)
    {
        if (_points.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var p0 = t.DocToView(_points[0].X, _points[0].Y);
            ctx.BeginFigure(p0, false, false);
            foreach (var p in _points.Skip(1))
                ctx.LineTo(t.DocToView(p.X, p.Y), true, false);
        }
        dc.DrawGeometry(null, new Pen(Brushes.White, 1), geometry);
        dc.DrawGeometry(null, new Pen(Brushes.Black, 1) { DashStyle = DashStyles.Dash }, geometry);
    }
}

public sealed class MagicWandTool : ToolBase
{
    public override string Name => "Magic Wand";
    public override string IconKey => "Icon.MagicWand";
    public override ToolSettingKind[] SettingsBar =>
        new[] { ToolSettingKind.CombineMode, ToolSettingKind.Tolerance, ToolSettingKind.WandGlobal, ToolSettingKind.Feather };
    public override string StatusHint =>
        "Click to select a region of similar color. Ctrl adds, Alt subtracts. Global mode selects all matching pixels.";

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        var ws = Context.Workspace;
        if (ws == null) return;
        var doc = ws.Document;
        if (!doc.Bounds.Contains(e.PixelX, e.PixelY)) return;

        var before = doc.Selection.SnapshotMask();
        var mask = FloodFill.ComputeMask(
            doc.ActiveLayer.Surface, e.PixelX, e.PixelY,
            Context.Environment.Tolerance, contiguous: !Context.Environment.WandGlobal);

        SelectionCombineMode mode = Context.Environment.CombineMode;
        if ((e.Modifiers & ModifierKeys.Control) != 0) mode = SelectionCombineMode.Add;
        else if ((e.Modifiers & ModifierKeys.Alt) != 0) mode = SelectionCombineMode.Subtract;

        doc.Selection.Combine(mask, mode);
        if (Context.Environment.Feather > 0)
            doc.Selection.Feather((int)Context.Environment.Feather);
        Context.PushHistory(new SelectionMemento(Name, before), IconKey);
        Context.NotifySelectionChanged();
        Context.InvalidateOverlay();
    }
}

public sealed class MoveSelectionTool : ToolBase
{
    public override string Name => "Move Selection";
    public override string IconKey => "Icon.MoveSelection";
    public override string StatusHint => "Drag to move the selection outline without moving pixels.";
    public override Cursor Cursor => Cursors.SizeAll;

    private bool _dragging;
    private Point _last;
    private byte[]? _maskBefore;
    private bool _moved;

    public override bool IsBusy => _dragging;

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        var ws = Context.Workspace;
        if (ws == null || ws.Document.Selection.IsEmpty) return;
        _dragging = true;
        _moved = false;
        _last = new Point(e.X, e.Y);
        _maskBefore = ws.Document.Selection.SnapshotMask();
    }

    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        if (!_dragging || Context.Workspace == null) return;
        int dx = (int)Math.Round(e.X - _last.X);
        int dy = (int)Math.Round(e.Y - _last.Y);
        if (dx == 0 && dy == 0) return;
        _last = new Point(_last.X + dx, _last.Y + dy);
        Context.Workspace.Document.Selection.Translate(dx, dy);
        _moved = true;
        Context.InvalidateOverlay();
    }

    public override void OnPointerUp(ToolPointerEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (_moved && _maskBefore != null)
        {
            Context.PushHistory(new SelectionMemento(Name, _maskBefore), IconKey);
            Context.NotifySelectionChanged();
        }
        _maskBefore = null;
    }

    public override void OnCancel()
    {
        if (_dragging && _maskBefore != null && Context.Workspace != null)
        {
            Context.Workspace.Document.Selection.RestoreMask(_maskBefore);
            Context.NotifySelectionChanged();
        }
        _dragging = false;
        _moved = false;
        _maskBefore = null;
        Context.InvalidateOverlay();
    }
}

/// <summary>
/// Lifts the selected pixels into a floating chunk that follows the mouse;
/// commits on Enter, tool switch, or starting an action elsewhere.
/// </summary>
public sealed class MoveSelectedPixelsTool : ToolBase
{
    public override string Name => "Move Selected Pixels";
    public override string IconKey => "Icon.MovePixels";
    public override string StatusHint => "Drag to move. Drag a pasted image's corner to resize; hold Shift to preserve its aspect ratio.";
    public override Cursor Cursor => Cursors.SizeAll;

    private Surface? _floating;          // lifted pixels (tight rect)
    private RectInt _floatSourceRect;    // where they were lifted from
    private RectInt _operationStartRect; // original position, retained for history
    private int _offsetX, _offsetY;      // current offset from source
    private Surface? _layerSnapshot;     // layer before lift (for history/cancel)
    private byte[]? _selectionSnapshot;
    private int _layerId = -1;
    private bool _dragging;
    private bool _externalFloat;
    private bool _hasMoved;
    private Point _dragStartDoc;
    private int _dragStartOffsetX, _dragStartOffsetY;
    private ResizeCorner _resizeCorner;
    private RectInt _resizeStartRect;
    private Surface? _resizeSource;

    private enum ResizeCorner { None, TopLeft, TopRight, BottomLeft, BottomRight }

    public bool IsFloating => _floating != null;
    internal RectInt FloatingBounds => _floating == null ? RectInt.Empty : FloatRect();

    public override bool IsBusy => IsFloating;

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        var ws = Context.Workspace;
        if (ws == null) return;
        var doc = ws.Document;
        var layer = doc.ActiveLayer;

        if (_floating == null)
        {
            if (doc.Selection.IsEmpty || layer.Locked || !layer.Visible)
            {
                Context.SetStatus(doc.Selection.IsEmpty
                    ? "Make a selection first, then drag to move its pixels."
                    : "The active layer is locked or hidden.");
                return;
            }
            LiftSelection(ws, layer);
            if (_floating == null) return;
        }

        _resizeCorner = _externalFloat ? HitResizeHandle(e.X, e.Y) : ResizeCorner.None;
        _dragging = true;
        if (_resizeCorner != ResizeCorner.None)
        {
            _resizeStartRect = FloatRect();
            _resizeSource = _floating.Clone();
            return;
        }
        _dragStartDoc = new Point(e.X, e.Y);
        _dragStartOffsetX = _offsetX;
        _dragStartOffsetY = _offsetY;
    }

    private void LiftSelection(Models.DocumentWorkspace ws, Core.Layers.Layer layer)
    {
        var doc = ws.Document;
        var bounds = doc.Selection.Bounds;
        if (bounds.IsEmpty) return;

        _layerId = layer.Id;
        _layerSnapshot = layer.Surface.Clone();
        _selectionSnapshot = doc.Selection.SnapshotMask();
        _floatSourceRect = bounds;
        _operationStartRect = bounds;
        _offsetX = _offsetY = 0;
        _externalFloat = false;
        _hasMoved = false;

        _floating = new Surface(bounds.Width, bounds.Height);
        for (int y = 0; y < bounds.Height; y++)
        {
            var srcRow = layer.Surface.GetRowSpan(bounds.Top + y, bounds.Left, bounds.Width);
            var dstRow = _floating.GetRow(y);
            for (int x = 0; x < bounds.Width; x++)
            {
                byte cov = doc.Selection.MaskAt(bounds.Left + x, bounds.Top + y);
                if (cov == 0) continue;
                uint c = srcRow[x];
                dstRow[x] = ColorBgra.WithAlpha(c, (byte)(ColorBgra.A(c) * cov / 255));
            }
        }
        // Keep the source pixels exact until the pointer actually moves. The
        // first MoveTo call restores/clears from the snapshot and stamps the
        // floating pixels at their new position.
        Context.InvalidateOverlay();
    }

    /// <summary>Starts an immediately movable paste while retaining source
    /// pixels that extend outside the fixed document canvas.</summary>
    public void BeginPaste(Surface source, Core.Layers.Layer layer, int originX, int originY)
    {
        var ws = Context.Workspace;
        if (ws == null) return;
        if (_floating != null) Commit();

        _layerId = layer.Id;
        _layerSnapshot = layer.Surface.Clone();
        _floatSourceRect = new RectInt(originX, originY, source.Width, source.Height);
        _operationStartRect = _floatSourceRect;
        _offsetX = _offsetY = 0;
        _floating = source;
        _externalFloat = true;
        _hasMoved = false;
        SetSelectionToFloatRect();
        _selectionSnapshot = ws.Document.Selection.SnapshotMask();
        Context.NotifySelectionChanged();
        Context.InvalidateDocument(_floatSourceRect.Intersect(ws.Document.Bounds));
    }

    public override void OnPointerMove(ToolPointerEventArgs e)
    {
        if (_floating == null || Context.Workspace == null) return;
        if (!_dragging)
        {
            var corner = _externalFloat ? HitResizeHandle(e.X, e.Y) : ResizeCorner.None;
            Context.SetCursorHint(corner switch
            {
                ResizeCorner.TopLeft or ResizeCorner.BottomRight => Cursors.SizeNWSE,
                ResizeCorner.TopRight or ResizeCorner.BottomLeft => Cursors.SizeNESW,
                _ => Cursor,
            });
            return;
        }
        if (_resizeCorner != ResizeCorner.None)
        {
            ResizeTo(e.X, e.Y, (e.Modifiers & ModifierKeys.Shift) != 0);
            return;
        }
        int newOffsetX = _dragStartOffsetX + (int)Math.Round(e.X - _dragStartDoc.X);
        int newOffsetY = _dragStartOffsetY + (int)Math.Round(e.Y - _dragStartDoc.Y);
        MoveTo(newOffsetX, newOffsetY);
    }

    private ResizeCorner HitResizeHandle(double x, double y)
    {
        var r = FloatRect();
        double radius = 7 / Math.Max(0.05, Context.ZoomFactor);
        var handles = new[]
        {
            (DistanceSquared(x, y, r.Left, r.Top), ResizeCorner.TopLeft),
            (DistanceSquared(x, y, r.Right, r.Top), ResizeCorner.TopRight),
            (DistanceSquared(x, y, r.Left, r.Bottom), ResizeCorner.BottomLeft),
            (DistanceSquared(x, y, r.Right, r.Bottom), ResizeCorner.BottomRight),
        };
        var nearest = handles.OrderBy(h => h.Item1).First();
        return nearest.Item1 <= radius * radius ? nearest.Item2 : ResizeCorner.None;
    }

    private static double DistanceSquared(double x, double y, double hx, double hy) =>
        (x - hx) * (x - hx) + (y - hy) * (y - hy);

    private void ResizeTo(double x, double y, bool preserveAspect)
    {
        if (_resizeSource == null || _floating == null || Context.Workspace == null) return;
        var oldRect = FloatRect();
        bool fromLeft = _resizeCorner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft;
        bool fromTop = _resizeCorner is ResizeCorner.TopLeft or ResizeCorner.TopRight;
        int fixedX = fromLeft ? _resizeStartRect.Right : _resizeStartRect.Left;
        int fixedY = fromTop ? _resizeStartRect.Bottom : _resizeStartRect.Top;
        int movingX = (int)Math.Round(x);
        int movingY = (int)Math.Round(y);
        int width = Math.Max(1, fromLeft ? fixedX - movingX : movingX - fixedX);
        int height = Math.Max(1, fromTop ? fixedY - movingY : movingY - fixedY);

        if (preserveAspect)
        {
            double scaleX = (double)width / _resizeStartRect.Width;
            double scaleY = (double)height / _resizeStartRect.Height;
            if (scaleX >= scaleY)
                height = Math.Max(1, (int)Math.Round(width * (double)_resizeStartRect.Height / _resizeStartRect.Width));
            else
                width = Math.Max(1, (int)Math.Round(height * (double)_resizeStartRect.Width / _resizeStartRect.Height));
        }

        int left = fromLeft ? fixedX - width : fixedX;
        int top = fromTop ? fixedY - height : fixedY;
        var newRect = new RectInt(left, top, width, height);
        if (newRect == oldRect) return;

        _floating = _resizeSource.Resized(width, height, ResampleMode.Bilinear);
        _floatSourceRect = newRect;
        _offsetX = _offsetY = 0;
        _hasMoved = true;

        var ws = Context.Workspace;
        var layer = ws.Document.FindLayer(_layerId);
        if (layer == null) return;
        var dirty = oldRect.Union(newRect).Intersect(ws.Document.Bounds);
        RestoreClearedRegion(layer, dirty);
        layer.Surface.DrawSurfaceOver(_floating, newRect.Left, newRect.Top);
        SetSelectionToFloatRect();
        Context.InvalidateDocument(dirty);
        Context.NotifySelectionChanged();
    }

    private void MoveTo(int newOffsetX, int newOffsetY)
    {
        if (_floating == null || Context.Workspace == null) return;
        if (newOffsetX == _offsetX && newOffsetY == _offsetY) return;
        var ws = Context.Workspace;
        var layer = ws.Document.FindLayer(_layerId);
        if (layer == null) return;

        var oldRect = FloatRect();
        _offsetX = newOffsetX;
        _offsetY = newOffsetY;
        _hasMoved = true;
        var newRect = FloatRect();

        // Restore the layer under the old position (from cleared snapshot state),
        // then stamp the floating pixels at the new position.
        var dirty = oldRect.Union(newRect).Intersect(ws.Document.Bounds);
        RestoreClearedRegion(layer, dirty);
        layer.Surface.DrawSurfaceOver(_floating, newRect.Left, newRect.Top);

        // Move the selection outline along with the pixels.
        if (_externalFloat)
            SetSelectionToFloatRect();
        else
        {
            ws.Document.Selection.RestoreMask(_selectionSnapshot!);
            ws.Document.Selection.Translate(_offsetX, _offsetY);
        }
        Context.InvalidateDocument(dirty);
    }

    private void RestoreClearedRegion(Core.Layers.Layer layer, RectInt rect)
    {
        // The "cleared" state = snapshot with selection coverage removed.
        var r = rect.Intersect(layer.Surface.Bounds);
        for (int y = r.Top; y < r.Bottom; y++)
        {
            var snapRow = _layerSnapshot!.GetRowSpan(y, r.Left, r.Width);
            var dstRow = layer.Surface.GetRowSpan(y, r.Left, r.Width);
            for (int x = 0; x < r.Width; x++)
            {
                int sx = r.Left + x, sy = y;
                byte cov = InOriginalSelection(sx, sy);
                uint c = snapRow[x];
                dstRow[x] = cov == 0 ? c : ColorBgra.WithAlpha(c, (byte)(ColorBgra.A(c) * (255 - cov) / 255));
            }
        }
    }

    private byte InOriginalSelection(int x, int y)
    {
        if (_selectionSnapshot == null || Context.Workspace == null) return 0;
        var doc = Context.Workspace.Document;
        if (x < 0 || y < 0 || x >= doc.Width || y >= doc.Height) return 0;
        return _selectionSnapshot[y * doc.Width + x];
    }

    private RectInt FloatRect() =>
        new(_floatSourceRect.Left + _offsetX, _floatSourceRect.Top + _offsetY,
            _floatSourceRect.Width, _floatSourceRect.Height);

    public override void OnPointerUp(ToolPointerEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _resizeCorner = ResizeCorner.None;
        _resizeSource = null;
        // Keep floating until commit so the user can keep adjusting.
        SyncSelectionToFloat();
    }

    public override void OnRenderOverlay(DrawingContext dc, CanvasTransform transform)
    {
        if (!_externalFloat || _floating == null) return;
        var r = FloatRect();
        var tl = transform.DocToView(r.Left, r.Top);
        var br = transform.DocToView(r.Right, r.Bottom);
        var outline = new Rect(tl, br);
        var border = new Pen(Brushes.DodgerBlue, 1.25);
        border.Freeze();
        dc.DrawRectangle(null, border, outline);
        const double size = 9;
        foreach (var p in new[] { tl, new Point(br.X, tl.Y), new Point(tl.X, br.Y), br })
            dc.DrawRectangle(Brushes.White, border, new Rect(p.X - size / 2, p.Y - size / 2, size, size));
    }

    private void SyncSelectionToFloat()
    {
        var ws = Context.Workspace;
        if (ws == null || _selectionSnapshot == null) return;
        if (_externalFloat)
            SetSelectionToFloatRect();
        else
        {
            // Selection outline = original selection translated by the offset.
            ws.Document.Selection.RestoreMask(_selectionSnapshot);
            ws.Document.Selection.Translate(_offsetX, _offsetY);
        }
        Context.NotifySelectionChanged();
        Context.InvalidateOverlay();
    }

    private void SetSelectionToFloatRect()
    {
        var ws = Context.Workspace;
        if (ws == null) return;
        var selection = ws.Document.Selection;
        selection.Clear();
        var visible = FloatRect().Intersect(ws.Document.Bounds);
        for (int y = visible.Top; y < visible.Bottom; y++)
            selection.Mask.AsSpan(y * selection.Width + visible.Left, visible.Width).Fill(255);
        selection.MarkChanged();
    }

    public override bool OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (_floating == null) return false;
        int step = (modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        switch (key)
        {
            case Key.Left: MoveTo(_offsetX - step, _offsetY); SyncSelectionToFloat(); return true;
            case Key.Right: MoveTo(_offsetX + step, _offsetY); SyncSelectionToFloat(); return true;
            case Key.Up: MoveTo(_offsetX, _offsetY - step); SyncSelectionToFloat(); return true;
            case Key.Down: MoveTo(_offsetX, _offsetY + step); SyncSelectionToFloat(); return true;
        }
        return false;
    }

    public override void OnCommit() => Commit();
    public override void OnDeactivated() => Commit();

    public void Commit()
    {
        var ws = Context.Workspace;
        if (_floating == null || ws == null) return;
        var layer = ws.Document.FindLayer(_layerId);
        if (_hasMoved && layer != null && _layerSnapshot != null && _selectionSnapshot != null)
        {
            // Layer pixels already reflect the final state; build one history step
            // covering the whole affected area plus the selection change.
            var affected = _operationStartRect.Union(FloatRect()).Intersect(ws.Document.Bounds);
            var beforePixels = _layerSnapshot.ExtractRect(affected);
            var mementos = new List<HistoryMemento>
            {
                new SurfaceRegionMemento(Name, layer, affected, beforePixels),
                new SelectionMemento(Name, _selectionSnapshot),
            };
            Context.PushHistory(new CompositeMemento("Move Selected Pixels", mementos), IconKey);
            ws.MarkDirty();
        }
        ResetFloat();
        Context.NotifySelectionChanged();
    }

    public override void OnCancel()
    {
        var ws = Context.Workspace;
        if (_floating == null || ws == null) return;
        var layer = ws.Document.FindLayer(_layerId);
        if (layer != null && _layerSnapshot != null)
        {
            layer.Surface.CopyFrom(_layerSnapshot);
            if (_selectionSnapshot != null)
                ws.Document.Selection.RestoreMask(_selectionSnapshot);
            Context.InvalidateDocument(ws.Document.Bounds);
        }
        ResetFloat();
        Context.NotifySelectionChanged();
    }

    private void ResetFloat()
    {
        _floating = null;
        _layerSnapshot = null;
        _selectionSnapshot = null;
        _layerId = -1;
        _offsetX = _offsetY = 0;
        _dragging = false;
        _resizeCorner = ResizeCorner.None;
        _resizeSource = null;
        _externalFloat = false;
        _hasMoved = false;
    }
}
