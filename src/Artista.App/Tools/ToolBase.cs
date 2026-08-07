using System.Windows.Input;
using System.Windows.Media;
using Artista.App.Models;
using Artista.Core.History;
using Artista.Core.Imaging;

namespace Artista.App.Tools;

/// <summary>Which shared settings a tool shows in the tool settings bar.</summary>
public enum ToolSettingKind
{
    CombineMode,
    BrushWidth,
    Hardness,
    Opacity,
    Tolerance,
    Softness,
    Antialias,
    FillStyle,
    CornerRadius,
    WandGlobal,
    GradientShape,
    GradientToTransparent,
    Font,
    SampleModes,
    Feather,
}

public enum PointerButton
{
    None,
    Left,
    Right,
}

public sealed class ToolPointerEventArgs
{
    public double X;                 // document coordinates (fractional)
    public double Y;
    public PointerButton Button;
    public ModifierKeys Modifiers;
    public int PixelX => (int)Math.Floor(X);
    public int PixelY => (int)Math.Floor(Y);
}

/// <summary>Services the active tool uses to talk to the app.</summary>
public interface IToolContext
{
    DocumentWorkspace? Workspace { get; }
    ToolEnvironment Environment { get; }

    /// <summary>Recomposites and repaints a document region.</summary>
    void InvalidateDocument(RectInt rect);

    /// <summary>Repaints overlays only (no recomposite).</summary>
    void InvalidateOverlay();

    void PushHistory(HistoryMemento memento, string? iconKey = null);
    void SetStatus(string text);
    void SetCursorHint(Cursor cursor);
    double ZoomFactor { get; }
    bool IsSpriteGridActive { get; }
    int SpriteGridCellWidth { get; }
    int SpriteGridCellHeight { get; }

    /// <summary>Refresh panels after selection/structure changes.</summary>
    void NotifySelectionChanged();
    void NotifyLayersChanged();

    // View control (used by the Zoom and Pan tools).
    void ViewZoomInAt(System.Windows.Point docPoint);
    void ViewZoomOutAt(System.Windows.Point docPoint);
    void ViewZoomToRect(RectInt docRect);
    void ViewPanBy(double viewDx, double viewDy);

    /// <summary>Builds an animation preview from the current frame regions or pasteboard pieces.</summary>
    void OpenSpritePreview();
}

/// <summary>
/// Base class for canvas tools.
///
/// To add a new tool: derive from ToolBase, implement the pointer handlers
/// (coordinates arrive in document space), declare which shared settings the
/// tool exposes via <see cref="SettingsBar"/>, add an icon geometry to
/// Themes/Icons.xaml, and register the tool in <see cref="ToolRegistry"/>.
/// History integration: take a snapshot of the layer at stroke start and push
/// one memento per completed operation.
/// </summary>
public abstract class ToolBase
{
    protected IToolContext Context { get; private set; } = null!;

    public abstract string Name { get; }
    public abstract string IconKey { get; }
    public virtual string StatusHint => "";
    public virtual Cursor Cursor => Cursors.Cross;
    public virtual ToolSettingKind[] SettingsBar => Array.Empty<ToolSettingKind>();

    public void Attach(IToolContext context) => Context = context;

    /// <summary>True while the tool has an uncommitted operation in flight
    /// (drag, stroke, floating pixels, text edit). Escape cancels the operation
    /// when busy; when idle, Escape deselects instead.</summary>
    public virtual bool IsBusy => false;

    public virtual void OnActivated() { }
    public virtual void OnDeactivated() { }
    public virtual void OnPointerDown(ToolPointerEventArgs e) { }
    public virtual void OnPointerMove(ToolPointerEventArgs e) { }
    public virtual void OnPointerUp(ToolPointerEventArgs e) { }
    public virtual bool OnKeyDown(Key key, ModifierKeys modifiers) => false;

    /// <summary>Draw a preview overlay. dc operates in view space; use the transform for doc→view.</summary>
    public virtual void OnRenderOverlay(DrawingContext dc, CanvasTransform transform) { }

    /// <summary>Enter pressed (commit pending operation).</summary>
    public virtual void OnCommit() { }

    /// <summary>Escape pressed (cancel pending operation).</summary>
    public virtual void OnCancel() { }
}

/// <summary>Doc↔view coordinate mapping snapshot used during overlay rendering.</summary>
public readonly record struct CanvasTransform(double Zoom, double OffsetX, double OffsetY)
{
    public System.Windows.Point DocToView(double x, double y) => new(x * Zoom + OffsetX, y * Zoom + OffsetY);
    public System.Windows.Point ViewToDoc(double x, double y) => new((x - OffsetX) / Zoom, (y - OffsetY) / Zoom);
}
