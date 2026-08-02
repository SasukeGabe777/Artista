# Adding a tool

Tools live in `src/Artista.App/Tools/`. A tool is a class deriving from `ToolBase` (or one of the richer bases below) that receives pointer events in **document coordinates** and edits the active layer through the core APIs.

## 1. Pick a base class

| Base | Use for | You implement |
|---|---|---|
| `ToolBase` | click tools, custom interactions | `OnPointerDown/Move/Up`, optionally `OnRenderOverlay`, `OnKeyDown`, `OnCommit`/`OnCancel` |
| `StrokeToolBase` | brush-like tools (one stroke = one undo) | `ApplyStroke(layer, rect)` — blend `Stroke` (coverage) + `Snapshot` (pre-stroke pixels) into the layer |
| `SelectionToolBase` | drag-defined selection shapes | `BuildMask(width, height)` |
| `ShapeToolBase` | drag-defined drawn shapes | `BuildShape(fillBuffer, outlineBuffer, antialias)` |

## 2. Minimal example

```csharp
public sealed class InvertBrushTool : StrokeToolBase
{
    public override string Name => "Invert Brush";
    public override string IconKey => "Icon.MyInvertBrush";
    public override ToolSettingKind[] SettingsBar =>
        new[] { ToolSettingKind.BrushWidth, ToolSettingKind.Hardness };
    public override string StatusHint => "Drag to invert colors under the brush.";

    protected override void ApplyStroke(Layer layer, RectInt rect) =>
        Stroke!.Apply(layer.Surface, Snapshot!, Context.Workspace!.Document.Selection, rect, 1f,
            (orig, strength) => ColorBgra.Lerp(orig, orig ^ 0x00FFFFFFu, strength));
}
```

`StrokeToolBase` already handles: layer lock/visibility checks, snapshotting, dab spacing, selection masking (via the applier), dirty-rect invalidation, cancel (Escape restores the snapshot), and pushing the single `SurfaceRegionMemento` on mouse-up.

## 3. Add an icon

Add a 16×16 `Geometry` to `src/Artista.App/Themes/Icons.xaml`:

```xml
<Geometry x:Key="Icon.MyInvertBrush">M2,2 L14,14 …</Geometry>
```

Icons are stroked `Path`s using the themed `IconBrush`, so they work in both themes automatically.

## 4. Register it

Add an instance to `ToolRegistry.CreateTools()` (`Tools/ToolRegistry.cs`). Order = position in the two-column palette. Done — the palette button, settings bar, cursor, status hint and tooltips are generated from the tool's properties.

## 5. What the context gives you

`Context` (an `IToolContext`, implemented by `MainWindow`) provides:

- `Workspace` — active `DocumentWorkspace` (document, selection, history, composite)
- `Environment` — shared settings (colors, brush width, tolerance, …)
- `InvalidateDocument(rect)` — recomposite + repaint a region
- `InvalidateOverlay()` — repaint overlays only
- `PushHistory(memento, icon)` — record an undoable step (also marks the document dirty)
- `SetStatus(text)`, `ViewZoom*/ViewPanBy` for view control

## Conventions to preserve

- Left button uses the primary color, right button the secondary (pass through `e.Button`).
- Escape cancels the in-flight operation and restores pixels exactly; Enter commits.
- Respect `layer.Locked` / `layer.Visible` / `layer.AlphaLocked` and the selection mask.
- One continuous interaction ⇒ one history entry, named after the tool.

If the tool needs a *new* shared setting, add a property to `ToolEnvironment`, a `ToolSettingKind` value, and a case in `MainWindow.BuildSettingControl`.
