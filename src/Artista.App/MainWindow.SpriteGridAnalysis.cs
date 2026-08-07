using System.Windows;
using Artista.App.Dialogs;
using Artista.App.Models;
using Artista.Core.History;
using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.App;

public sealed partial class MainWindow
{
    private void AlignSpritesToGrid()
    {
        if (!TryGetSpriteAnalysisContext(requireEditableLayer: true,
                out var workspace, out var surface, out var selection))
            return;

        var grid = SpriteGridLayout;
        var sprites = DetectSelectedSprites(surface, selection, grid);
        if (sprites.Count == 0)
        {
            ShowSpriteAnalysisMessage("No opaque sprite pixels were detected in or immediately around the selection.");
            return;
        }

        var completePlan = SpriteGridAnalyzer.PlanAlignment(surface, sprites, grid);
        var moving = completePlan.Moves
            .Where(move => move.DeltaX != 0 || move.DeltaY != 0)
            .ToArray();
        if (moving.Length == 0)
        {
            ShowSpriteAnalysisMessage(completePlan.SkippedSprites.Count > 0
                ? $"Detected {sprites.Count} sprites, but none can be moved safely without clipping or overwriting unrelated pixels."
                : $"All {sprites.Count} detected sprites are already centered in their assigned cells.");
            return;
        }

        RectInt affected = RectInt.Empty;
        foreach (var move in moving)
            affected = affected.Union(move.Sprite.Bounds).Union(move.DestinationBounds);
        var plan = new SpriteAlignmentPlan(moving, completePlan.SkippedSprites, affected);
        string summary = $"Detected {sprites.Count} sprites from alpha-connected pixels. " +
                         $"Artista will center {moving.Length} complete sprites using integer-pixel moves; " +
                         $"{completePlan.SkippedSprites.Count} ambiguous or unsafe sprites will remain unchanged.";
        var preview = new SpriteAnalysisPreviewDialog(
            "Align Sprites to Grid", surface, selection.Bounds, sprites,
            grid, grid, moving, summary)
        {
            Owner = this,
        };
        if (preview.ShowDialog() != true) return;

        var layer = workspace.Document.ActiveLayer;
        var before = layer.Surface.ExtractRect(affected);
        SpriteGridAnalyzer.ApplyAlignment(layer.Surface, plan);
        PushHistory(new SurfaceRegionMemento("Align Sprites to Grid", layer, affected, before),
            "Icon.MovePixels");
        InvalidateDocument(affected);
        SetStatus($"Aligned {moving.Length} sprites to the grid as one undoable action.");
    }

    private void AlignGridToSprites()
    {
        if (!TryGetSpriteAnalysisContext(requireEditableLayer: false,
                out _, out var surface, out var selection))
            return;

        var current = SpriteGridLayout;
        var sprites = DetectSelectedSprites(surface, selection, current);
        if (sprites.Count == 0)
        {
            ShowSpriteAnalysisMessage("No opaque sprite pixels were detected in or immediately around the selection.");
            return;
        }

        var fit = SpriteGridAnalyzer.FindBestOrigin(sprites, current);
        var proposed = fit.Layout.WithNormalizedOrigin();
        if (proposed.OriginX == current.OriginX && proposed.OriginY == current.OriginY)
        {
            ShowSpriteAnalysisMessage(
                $"The current origin already best fits all {sprites.Count} detected sprites.");
            return;
        }

        string summary = $"Using all {sprites.Count} detected sprites, the best origin is " +
                         $"{proposed.OriginX}, {proposed.OriginY}. " +
                         $"{fit.FullyContainedSprites} sprites fit fully inside cells and {fit.CrossedSprites} remain crossed. " +
                         "Cell size and spacing will not change; pixels will not be modified.";
        var preview = new SpriteAnalysisPreviewDialog(
            "Align Grid to Sprites", surface, selection.Bounds, sprites,
            current, proposed, null, summary)
        {
            Owner = this,
        };
        if (preview.ShowDialog() != true) return;

        ApplySpriteGridLayout(proposed);
        SetStatus($"Aligned Sprite Grid origin to {proposed.OriginX}, {proposed.OriginY}; artwork was not modified.");
    }

    private void DetectSpriteGrid()
    {
        if (!TryGetSpriteAnalysisContext(requireEditableLayer: false,
                out _, out var surface, out var selection))
            return;

        int margin = Math.Clamp(Math.Min(selection.Bounds.Width, selection.Bounds.Height) / 12, 4, 64);
        var sprites = SpriteDetector.Detect(surface, selection,
            new SpriteDetectionOptions(InspectionMargin: margin));
        if (sprites.Count == 0)
        {
            ShowSpriteAnalysisMessage("No opaque sprite pixels were detected in or immediately around the selection.");
            return;
        }

        var inference = SpriteGridAnalyzer.InferGrid(sprites, selection.Bounds);
        var proposed = inference.Layout.WithNormalizedOrigin();
        string confidence = inference.Confidence >= 0.8 ? "high" :
            inference.Confidence >= 0.5 ? "moderate" : "low";
        string summary = $"Detected {sprites.Count} sprites and inferred: " +
                         $"Cell {proposed.CellWidth} x {proposed.CellHeight} px | " +
                         $"Offset {proposed.OriginX}, {proposed.OriginY} | " +
                         $"Spacing {proposed.SpacingX}, {proposed.SpacingY}. " +
                         $"Confidence is {confidence}; review the red preview before applying. Pixels will not be modified.";
        var preview = new SpriteAnalysisPreviewDialog(
            "Detect Sprite Grid", surface, selection.Bounds, sprites,
            SpriteGridLayout, proposed, null, summary)
        {
            Owner = this,
        };
        if (preview.ShowDialog() != true) return;

        ApplySpriteGridLayout(proposed);
        SetStatus($"Detected Sprite Grid: {proposed.CellWidth} x {proposed.CellHeight}, " +
                  $"offset {proposed.OriginX}, {proposed.OriginY}, spacing {proposed.SpacingX}, {proposed.SpacingY}.");
    }

    private bool TryGetSpriteAnalysisContext(
        bool requireEditableLayer,
        out DocumentWorkspace workspace,
        out Surface surface,
        out Selection selection)
    {
        workspace = null!;
        surface = null!;
        selection = null!;
        if (_active == null) return false;
        _activeTool?.OnCommit();
        var doc = _active.Document;
        if (doc.Selection.IsEmpty)
        {
            ShowSpriteAnalysisMessage("Highlight the representative sprite-sheet region first.");
            return false;
        }
        if (requireEditableLayer && (doc.ActiveLayer.Locked || !doc.ActiveLayer.Visible))
        {
            ShowSpriteAnalysisMessage("Choose an unlocked, visible sprite-sheet layer before aligning pixels.");
            return false;
        }

        workspace = _active;
        surface = doc.ActiveLayer.Surface;
        selection = doc.Selection;
        return true;
    }

    private static IReadOnlyList<DetectedSprite> DetectSelectedSprites(
        Surface surface, Selection selection, SpriteGridLayout grid)
    {
        int margin = Math.Clamp(
            Math.Max(grid.SpacingX, grid.SpacingY) + Math.Min(grid.CellWidth, grid.CellHeight) / 2,
            4, 128);
        return SpriteDetector.Detect(surface, selection, new SpriteDetectionOptions(
            InspectionMargin: margin,
            ExpectedCellWidth: grid.CellWidth,
            ExpectedCellHeight: grid.CellHeight));
    }

    private void ShowSpriteAnalysisMessage(string message)
    {
        SetStatus(message);
        MessageBox.Show(this, message, "Sprite Grid", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
