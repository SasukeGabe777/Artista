namespace Artista.App.Tools;

/// <summary>
/// The ordered list of tools shown in the tool palette (two columns, matching
/// Paint.NET's arrangement). Register new tools here.
/// </summary>
public static class ToolRegistry
{
    public static IReadOnlyList<ToolBase> CreateTools() => new ToolBase[]
    {
        new RectangleSelectTool(),
        new MoveSelectedPixelsTool(),
        new LassoSelectTool(),
        new MoveSelectionTool(),
        new EllipseSelectTool(),
        new ZoomTool(),
        new MagicWandTool(),
        new PanTool(),
        new PaintBucketTool(),
        new GradientTool(),
        new PaintbrushTool(),
        new EraserTool(),
        new PencilTool(),
        new ColorPickerTool(),
        new CloneStampTool(),
        new RecolorTool(),
        new ColorRemoverTool(),
        new TextTool(),
        new LineCurveTool(),
        new CurveTool(),
        new RectangleShapeTool(),
        new RoundedRectangleTool(),
        new EllipseShapeTool(),
        new FreeformShapeTool(),
        new SpritePreviewTool(),
    };
}
