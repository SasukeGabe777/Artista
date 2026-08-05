using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using Artista.App.Tools;
using Artista.Core.Effects;
using Artista.Core.Imaging;
using Artista.Core.IO;

namespace Artista.App;

/// <summary>
/// Automated UI self-test (run with: Artista.exe --uitest [outputDir]).
/// Drives the real window through the major workflows — document creation,
/// tools, selections, effects, undo/redo, save/reopen, export, theme switch —
/// and writes a pass/fail report plus step screenshots.
/// </summary>
public sealed partial class MainWindow
{
    private readonly List<string> _testLog = new();
    private int _testFailures;
    private string _testDir = "";
    internal bool SuppressCloseConfirmation;

    public async Task<int> RunSelfTestAsync(string outputDir)
    {
        SuppressCloseConfirmation = true;
        _testDir = outputDir;
        Directory.CreateDirectory(outputDir);
        try
        {
            await RunSelfTestStepsAsync();
        }
        catch (Exception ex)
        {
            Fail($"Unhandled self-test exception: {ex}");
        }
        _testLog.Add($"\n{(_testFailures == 0 ? "ALL PASSED" : _testFailures + " FAILURES")}");
        File.WriteAllLines(Path.Combine(outputDir, "report.txt"), _testLog);
        return _testFailures == 0 ? 0 : 1;
    }

    private void Check(bool condition, string what)
    {
        if (condition)
            _testLog.Add($"PASS  {what}");
        else
            Fail(what);
    }

    private void Fail(string what)
    {
        _testFailures++;
        _testLog.Add($"FAIL  {what}");
    }

    private async Task PumpAsync(int ms = 30)
    {
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        await Task.Delay(ms);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Snapshot(string name)
    {
        try
        {
            var bmp = new RenderTargetBitmap(
                (int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(this);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(_testDir, name + ".png"));
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            _testLog.Add($"WARN  screenshot {name} failed: {ex.Message}");
        }
    }

    private ToolPointerEventArgs Pt(double x, double y, PointerButton button = PointerButton.Left, ModifierKeys mods = ModifierKeys.None) =>
        new() { X = x, Y = y, Button = button, Modifiers = mods };

    private void DragTool(ToolBase tool, double x0, double y0, double x1, double y1, PointerButton button = PointerButton.Left)
    {
        tool.OnPointerDown(Pt(x0, y0, button));
        int steps = 8;
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            tool.OnPointerMove(Pt(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, button));
        }
        tool.OnPointerUp(Pt(x1, y1, button));
    }

    private T Tool<T>() where T : ToolBase
    {
        var tool = _tools.OfType<T>().First();
        ActivateTool(tool);
        return tool;
    }

    private async Task RunSelfTestStepsAsync()
    {
        string tempFile(string name) => Path.Combine(_testDir, name);

        // 1. Startup state: a default document exists.
        await PumpAsync(200);
        Check(_active != null, "application started with a default document");
        Check(WindowStyle == WindowStyle.SingleBorderWindow && ResizeMode == ResizeMode.CanResize,
            "startup window exposes the standard minimize, maximize, and close controls");
        Check(Top >= SystemParameters.WorkArea.Top && Left >= SystemParameters.WorkArea.Left,
            "startup title bar is kept inside the usable screen");
        Snapshot("01-startup");

        // 2. New transparent document.
        CreateDocument(320, 240, "Transparent");
        await PumpAsync();
        Check(_active!.Document.Width == 320 && _active.Document.Height == 240, "created 320x240 transparent document");
        Check(_active.CompositeSurface[10, 10] == 0, "new document is transparent");

        // 3. Paint a brush stroke.
        _environment.PrimaryColor = Core.Imaging.ColorBgra.Pack(0, 0, 255, 255); // red
        _environment.BrushWidth = 24;
        _environment.Opacity = 1.0;
        var brush = Tool<PaintbrushTool>();
        DragTool(brush, 40, 60, 240, 60);
        await PumpAsync();
        var layer = _active.Document.ActiveLayer;
        Check(Core.Imaging.ColorBgra.A(layer.Surface[140, 60]) == 255, "brush stroke painted opaque pixels");
        Check(Core.Imaging.ColorBgra.R(layer.Surface[140, 60]) == 255, "brush stroke used primary color");
        Check(_active.History.UndoEntries.Count == 1, "one stroke = one history entry");
        Snapshot("02-brush");

        // 4. Undo / redo the stroke.
        Undo();
        await PumpAsync();
        Check(Core.Imaging.ColorBgra.A(layer.Surface[140, 60]) == 0, "undo removed the stroke");
        Redo();
        await PumpAsync();
        Check(Core.Imaging.ColorBgra.A(layer.Surface[140, 60]) == 255, "redo restored the stroke");

        // 5. Eraser.
        var eraser = Tool<EraserTool>();
        DragTool(eraser, 130, 60, 150, 60);
        await PumpAsync();
        Check(Core.Imaging.ColorBgra.A(layer.Surface[140, 60]) < 255, "eraser reduced alpha");
        Undo();
        await PumpAsync();

        // 6. Rectangle selection + delete inside it.
        var rectSel = Tool<RectangleSelectTool>();
        DragTool(rectSel, 30, 40, 120, 90);
        await PumpAsync();
        Check(!_active.Document.Selection.IsEmpty, "rectangle selection created");
        Check(_active.Document.Selection.MaskAt(60, 60) == 255, "selection covers dragged rect");
        Check(_active.Document.Selection.MaskAt(200, 60) == 0, "selection excludes outside");
        DeleteSelectionPixels();
        await PumpAsync();
        Check(Core.Imaging.ColorBgra.A(layer.Surface[60, 60]) == 0, "delete cleared selected pixels");
        Check(Core.Imaging.ColorBgra.A(layer.Surface[200, 60]) == 255, "delete left unselected pixels");
        Snapshot("03-selection-delete");

        // 7. Magic wand on the stroke remainder.
        Deselect();
        var wand = Tool<MagicWandTool>();
        _environment.Tolerance = 20;
        wand.OnPointerDown(Pt(200, 60));
        await PumpAsync();
        Check(!_active.Document.Selection.IsEmpty, "magic wand selected a region");
        Check(_active.Document.Selection.MaskAt(200, 60) == 255, "wand selection includes clicked pixel");

        // 8. Move selected pixels.
        var move = Tool<MoveSelectedPixelsTool>();
        var beforeClickWithoutMove = (uint[])layer.Surface.Pixels.Clone();
        move.OnPointerDown(Pt(200, 60));
        move.OnPointerUp(Pt(200, 60));
        move.Commit();
        Check(layer.Surface.Pixels.SequenceEqual(beforeClickWithoutMove),
            "clicking Move Selected Pixels without dragging preserves pixels");
        DragTool(move, 200, 60, 200, 130);
        move.Commit();
        await PumpAsync();
        Check(Core.Imaging.ColorBgra.A(layer.Surface[200, 130]) == 255, "moved pixels landed at target");
        Snapshot("04-move-pixels");
        Deselect();

        // 9. Secondary color with right mouse (paint blue with RMB).
        _environment.SecondaryColor = Core.Imaging.ColorBgra.Pack(255, 0, 0, 255); // blue
        var brush2 = Tool<PaintbrushTool>();
        DragTool(brush2, 60, 180, 220, 180, PointerButton.Right);
        await PumpAsync();
        Check(Core.Imaging.ColorBgra.B(layer.Surface[140, 180]) == 255, "right mouse painted secondary color");

        // 10. Layers: add, draw, reorder, opacity, merge.
        int layerCountBefore = _active.Document.Layers.Count;
        LayerAdd();
        await PumpAsync();
        Check(_active.Document.Layers.Count == layerCountBefore + 1, "layer added");
        var topLayer = _active.Document.ActiveLayer;
        var bucket = Tool<PaintBucketTool>();
        _environment.Tolerance = 0;
        _environment.WandGlobal = false;
        bucket.OnPointerDown(Pt(10, 10));
        await PumpAsync();
        Check(Core.Imaging.ColorBgra.A(topLayer.Surface[10, 10]) == 255, "bucket filled the new layer");
        Check(_layersPanel.SelectLayer(layer.Id), "clicking a layer row activates that layer");
        uint topPixelBeforeLayerStroke = topLayer.Surface[20, 220];
        uint bottomPixelBeforeLayerStroke = layer.Surface[20, 220];
        var layerBrush = Tool<PaintbrushTool>();
        DragTool(layerBrush, 20, 220, 30, 220);
        Check(layer.Surface[20, 220] != bottomPixelBeforeLayerStroke,
            "drawing edits the selected non-top layer");
        Check(topLayer.Surface[20, 220] == topPixelBeforeLayerStroke,
            "drawing leaves the unselected top layer unchanged");
        Check(_layersPanel.SelectLayer(topLayer.Id), "top layer can be reselected");
        topLayer.Opacity = 128;
        _active.InvalidateComposite(_active.Document.Bounds);
        _layersPanel.DeleteSelectedLayer();
        await PumpAsync();
        Check(_active.Document.Layers.Count == layerCountBefore, "Delete in the Layers panel deletes the selected layer");
        Undo();
        await PumpAsync();
        Check(_active.Document.Layers.Count == layerCountBefore + 1, "undo restored deleted layer");
        Redo();
        await PumpAsync();

        // 11. Gaussian blur effect (sync path), then undo restores exactly.
        var blurEffect = EffectRegistry.Effects.First(e => e is GaussianBlurEffect);
        var pixelsBefore = (uint[])layer.Surface.Pixels.Clone();
        var blurParams = ParameterSet.FromDefaults(blurEffect.CreateParameters());
        blurParams.Set("radius", 8);
        ApplyEffectSync(blurEffect, blurParams);
        await PumpAsync();
        Check(!layer.Surface.Pixels.SequenceEqual(pixelsBefore), "gaussian blur changed pixels");
        Undo();
        await PumpAsync();
        Check(layer.Surface.Pixels.SequenceEqual(pixelsBefore), "undo restored exact pre-blur pixels");
        Snapshot("05-effects");

        // 12. Remove Color effect: remove the red stroke globally.
        var removeColor = EffectRegistry.Effects.First(e => e is RemoveColorEffect);
        var rcParams = ParameterSet.FromDefaults(removeColor.CreateParameters());
        rcParams.Set("target", Core.Imaging.ColorBgra.Pack(0, 0, 255, 255));
        rcParams.Set("tolerance", 15);
        rcParams.Set("softness", 25);
        ApplyEffectSync(removeColor, rcParams);
        await PumpAsync();
        Check(Core.Imaging.ColorBgra.A(layer.Surface[200, 130]) == 0, "Remove Color removed the red pixels");
        Check(Core.Imaging.ColorBgra.A(layer.Surface[140, 180]) == 255, "Remove Color left blue pixels intact");
        Undo();
        await PumpAsync();
        Check(Core.Imaging.ColorBgra.A(layer.Surface[200, 130]) == 255, "undo restored removed color");

        // 13. Color Remover brush: only removes target color under the brush.
        var remover = Tool<ColorRemoverTool>();
        _environment.SampleFromClick = true;
        _environment.SampleContinuously = false;
        _environment.Tolerance = 15;
        _environment.Softness = 10;
        _environment.BrushWidth = 40;
        // Start the stroke ON a red pixel so sample-from-click targets red, then
        // cross the blue row (180).
        DragTool(remover, 200, 130, 200, 190);
        await PumpAsync();
        Check(Core.Imaging.ColorBgra.A(layer.Surface[200, 130]) == 0, "Color Remover erased sampled (red) color");
        Check(Core.Imaging.ColorBgra.A(layer.Surface[200, 180]) == 255, "Color Remover left different (blue) color");
        Check(_active.History.PeekUndoName == "Color Remover", "Color Remover stroke recorded in history");
        Snapshot("06-color-remover");

        // 14. Save layered project, reopen, verify.
        string artzPath = tempFile("selftest.artz");
        LayerAdd();
        await PumpAsync();
        int savedLayerCount = _active.Document.Layers.Count;
        Check(SaveWorkspaceTo(_active, artzPath), "saved .artz project");
        Check(!_active.IsDirty, "workspace marked clean after save");
        CloseWorkspace(_active);
        await PumpAsync();
        OpenFile(artzPath);
        await PumpAsync();
        Check(_active != null && _active.FilePath == artzPath, "reopened .artz project");
        Check(_active!.Document.Layers.Count == savedLayerCount, "project preserved layer count");

        // 15. Export PNG with transparency and reload.
        string pngPath = tempFile("selftest-export.png");
        ImageCodec.Save(_active.Document.Flatten(), pngPath, Core.IO.ImageFormat.Png);
        var reloaded = ImageCodec.Load(pngPath);
        Check(Core.Imaging.ColorBgra.A(reloaded[5, 5]) == 0, "exported PNG preserves transparency");

        // 16. Zoom and pan.
        double zoomBefore = _documentView.Zoom;
        _documentView.ZoomIn();
        Check(_documentView.Zoom > zoomBefore, "zoom in increases zoom");
        _documentView.FitToWindow();
        _documentView.ActualSize();
        Check(Math.Abs(_documentView.Zoom - 1.0) < 0.001, "actual size returns to 100%");
        _documentView.CenterImage();
        double panStartX = _documentView.Canvas.OffsetX;
        double panStartY = _documentView.Canvas.OffsetY;
        _documentView.PanBy(60, 40);
        Check(Math.Abs(_documentView.Canvas.OffsetX - panStartX) > 1 &&
              Math.Abs(_documentView.Canvas.OffsetY - panStartY) > 1,
            "middle-drag pan can move a canvas smaller than the viewport");
        _documentView.PanBy(-100000, -100000);
        Check(Math.Abs(_documentView.Canvas.OffsetX) < 0.01 &&
              Math.Abs(_documentView.Canvas.OffsetY) < 0.01,
            "canvas can be dragged flush into the top-left corner");
        _documentView.PanBy(200000, 200000);
        Check(Math.Abs(_documentView.Canvas.OffsetX -
                       (_documentView.Canvas.ActualWidth - _active.Document.Width * _documentView.Zoom)) < 0.01 &&
              Math.Abs(_documentView.Canvas.OffsetY -
                       (_documentView.Canvas.ActualHeight - _active.Document.Height * _documentView.Zoom)) < 0.01,
            "canvas can be dragged flush into the bottom-right corner");
        _documentView.CenterImage();

        // 17. Theme switching.
        SetTheme(AppTheme.Light);
        await PumpAsync(150);
        Snapshot("07-light-theme");
        Check(!ThemeManager.IsDarkEffective, "light theme active");
        var resolvedMenuBrush = (FindResource("MenuBackgroundBrush") as SolidColorBrush)?.Color;
        var actualMenuBrush = (_menuBar.Background as SolidColorBrush)?.Color;
        _testLog.Add($"INFO  MenuBackgroundBrush resolves to {resolvedMenuBrush}, menu.Background is {actualMenuBrush}");
        Check(actualMenuBrush == resolvedMenuBrush, "menu bar background follows theme");
        SetTheme(AppTheme.Dark);
        await PumpAsync(150);
        Check(ThemeManager.IsDarkEffective, "dark theme active");
        Snapshot("08-dark-theme");

        // 18. Text tool commit.
        var text = Tool<TextTool>();
        _environment.FontSize = 32;
        _environment.PrimaryColor = Core.Imaging.ColorBgra.Pack(0, 128, 0, 255);
        text.OnPointerDown(Pt(40, 40));
        text.OnTextInput("Hi");
        text.OnCommit();
        await PumpAsync();
        Check(_active.History.PeekUndoName == "Text", "text committed as one history entry");

        // 19. Gradient tool.
        var gradient = Tool<GradientTool>();
        DragTool(gradient, 0, 200, 320, 200);
        await PumpAsync();
        Check(_active.History.PeekUndoName == "Gradient", "gradient recorded in history");

        // 20. Shapes.
        var rectShape = Tool<RectangleShapeTool>();
        _environment.FillStyle = Models.FillStyle.FillAndOutline;
        DragTool(rectShape, 250, 30, 300, 80);
        await PumpAsync();
        Check(_active.History.PeekUndoName == "Rectangle", "rectangle shape recorded in history");
        Snapshot("09-final");

        // 21. Dirty-close confirmation path is exercised interactively; verify flag.
        Check(_active.IsDirty, "document marked dirty after edits");

        // 21b. Copy without a pixel selection uses the active layer's content
        // bounds rather than the merged, full-size canvas.
        try
        {
            CreateDocument(50, 40, "Transparent");
            await PumpAsync();
            var copyLayer = _active!.Document.ActiveLayer;
            uint copyRed = Core.Imaging.ColorBgra.Pack(0, 0, 255, 255);
            copyLayer.Surface.FillRect(new RectInt(7, 9, 11, 6), copyRed);
            LayerAdd();
            var pasteTarget = _active.Document.ActiveLayer;
            uint pasteBlue = Core.Imaging.ColorBgra.Pack(255, 0, 0, 255);
            pasteTarget.Surface.Clear(pasteBlue);
            _layersPanel.SelectLayer(copyLayer.Id);
            Check(TryCopyToClipboard(), "Ctrl+C copied the selected active layer");
            var croppedCopy = GetClipboardSurface();
            Check(croppedCopy is { Width: 11, Height: 6 } && croppedCopy[0, 0] == copyRed,
                "Ctrl+C crops active-layer content instead of selecting the entire canvas");
            _layersPanel.SelectLayer(pasteTarget.Id);
            int copyPasteLayerCount = _active.Document.Layers.Count;
            EditPaste();
            var layerPasteMove = (MoveSelectedPixelsTool)_activeTool!;
            Check(_active.Document.Layers.Count == copyPasteLayerCount &&
                  _active.Document.ActiveLayer.Id == pasteTarget.Id,
                "pasting copied layer pixels stays inside the selected target layer");
            Check(layerPasteMove.IsFloating && layerPasteMove.FloatingBounds is { Width: 11, Height: 6 },
                "pixels pasted into an existing layer receive resize handles");
            var initialLayerPaste = layerPasteMove.FloatingBounds;
            DragTool(layerPasteMove,
                initialLayerPaste.Left + initialLayerPaste.Width / 2.0,
                initialLayerPaste.Top + initialLayerPaste.Height / 2.0,
                initialLayerPaste.Left + initialLayerPaste.Width / 2.0 + 15,
                initialLayerPaste.Top + initialLayerPaste.Height / 2.0);
            Check(pasteTarget.Surface[initialLayerPaste.Left, initialLayerPaste.Top] == pasteBlue,
                "moving a paste restores pixels that were already in the target layer");
            CommitFromEnter();
            Check(_active.Document.Selection.IsEmpty,
                "Enter-style Move Selected Pixels commit deselects the floating pixels");
            Undo();
            Check(pasteTarget.Surface[initialLayerPaste.Left, initialLayerPaste.Top] == copyRed,
                "undo restores the paste to its initial position inside the target layer");
            Undo();
            Check(pasteTarget.Surface[initialLayerPaste.Left, initialLayerPaste.Top] == pasteBlue &&
                  _active.Document.Layers.Count == copyPasteLayerCount,
                "undo paste restores target pixels without changing the layer stack");
            CloseWorkspace(_active);
            await PumpAsync();
        }
        catch (Exception ex)
        {
            Fail($"active-layer clipboard copy threw: {ex.Message}");
        }

        // 21c. Copy/paste preserves transparency (PNG clipboard format).
        try
        {
            CreateDocument(60, 60, "Transparent");
            await PumpAsync();
            var cpLayer = _active!.Document.ActiveLayer;
            cpLayer.Surface.FillRect(new Core.Imaging.RectInt(0, 0, 30, 60), Core.Imaging.ColorBgra.Pack(0, 0, 255, 255));
            _active.InvalidateComposite(_active.Document.Bounds);
            SelectAll();
            EditCut();
            await PumpAsync();
            Check(Core.Imaging.ColorBgra.A(cpLayer.Surface[10, 10]) == 0, "Ctrl+X cleared the selected pixels");
            EditPaste();
            await PumpAsync();
            var pasted = _active.Document.ActiveLayer;
            Check(ReferenceEquals(pasted, cpLayer) && _active.Document.Layers.Count == 1,
                "paste writes into the selected layer instead of creating a Pasted layer");
            Check(Core.Imaging.ColorBgra.A(pasted.Surface[10, 10]) == 255 &&
                  Core.Imaging.ColorBgra.R(pasted.Surface[10, 10]) == 255, "pasted pixels kept their color");
            Check(Core.Imaging.ColorBgra.A(pasted.Surface[45, 10]) == 0,
                "pasted transparency preserved (no black background)");
            _activeTool?.OnCancel(); // release the move-tool float from paste
            (_activeTool as MoveSelectedPixelsTool)?.OnCancel();
            CloseWorkspace(_active);
            await PumpAsync();
        }
        catch (Exception ex)
        {
            Fail($"clipboard round trip threw: {ex.Message}");
        }

        // 21d. Oversized paste retains off-canvas pixels and can expand canvas.
        try
        {
            var oversized = new Surface(80, 40);
            uint edgeRed = Core.Imaging.ColorBgra.Pack(0, 0, 255, 255);
            uint edgeBlue = Core.Imaging.ColorBgra.Pack(255, 0, 0, 255);
            oversized[0, 10] = edgeRed;
            oversized[79, 10] = edgeBlue;

            CreateDocument(40, 40, "Transparent");
            await PumpAsync();
            PasteSurface(oversized, expandCanvas: false);
            var keepDoc = _active!.Document;
            var keepLayer = keepDoc.ActiveLayer;
            var keepMove = (MoveSelectedPixelsTool)_activeTool!;
            Check(keepDoc.Width == 40 && keepDoc.Height == 40,
                "Keep canvas size leaves oversized-paste dimensions unchanged");
            Check(keepMove.IsFloating, "oversized paste remains movable before commit");
            DragTool(keepMove, 20, 20, 40, 20);
            keepMove.Commit();
            Check(keepLayer.Surface[0, 10] == edgeRed,
                "moving oversized paste reveals pixels that began outside the canvas");
            Undo();
            Check(keepLayer.Surface[0, 10] == 0,
                "undo restores oversized paste to its initial centered position");
            Redo();
            Check(keepLayer.Surface[0, 10] == edgeRed,
                "redo restores the moved oversized paste");
            CloseWorkspace(_active);
            await PumpAsync();

            CreateDocument(40, 40, "Transparent");
            await PumpAsync();
            PasteSurface(oversized, expandCanvas: true);
            var expandMove = (MoveSelectedPixelsTool)_activeTool!;
            expandMove.Commit();
            Check(_active!.Document.Width == 80 && _active.Document.Height == 40,
                "Expand canvas grows the document to fit pasted pixels");
            Check(_active.Document.ActiveLayer.Surface[0, 10] == edgeRed &&
                  _active.Document.ActiveLayer.Surface[79, 10] == edgeBlue,
                "expanded paste retains both source edges");
            Undo();
            Check(_active.Document.Width == 40 && _active.Document.Height == 40,
                "undo restores canvas dimensions from expanded paste");
            CloseWorkspace(_active);
            await PumpAsync();
        }
        catch (Exception ex)
        {
            Fail($"oversized paste workflows threw: {ex.Message}");
        }

        // 21e. Pasted pixels expose corner resize handles; Shift preserves ratio.
        try
        {
            CreateDocument(100, 70, "Transparent");
            await PumpAsync();
            var resizeSource = new Surface(12, 6);
            resizeSource.Clear(Core.Imaging.ColorBgra.Pack(20, 180, 80, 255));
            PasteSurface(resizeSource, expandCanvas: false);
            var resizeMove = (MoveSelectedPixelsTool)_activeTool!;
            var start = resizeMove.FloatingBounds;
            resizeMove.OnPointerDown(Pt(start.Right, start.Bottom));
            resizeMove.OnPointerMove(Pt(start.Right + 24, start.Bottom + 18,
                PointerButton.Left, ModifierKeys.Shift));
            resizeMove.OnPointerUp(Pt(start.Right + 24, start.Bottom + 18));
            var resized = resizeMove.FloatingBounds;
            Check(resized.Width == 48 && resized.Height == 24,
                "Shift-dragging a paste corner resizes while preserving aspect ratio");

            // Temporarily release Shift during another resize, then reapply it.
            // The preview may distort while Shift is up, but Shift must return to
            // the immutable source ratio rather than preserving that preview ratio.
            resizeMove.OnPointerDown(Pt(resized.Right, resized.Bottom));
            resizeMove.OnPointerMove(Pt(resized.Right + 20, resized.Bottom + 20));
            var unconstrained = resizeMove.FloatingBounds;
            Check(unconstrained.Width != unconstrained.Height * 2,
                "unconstrained paste resize can preview a temporary aspect ratio");
            resizeMove.OnPointerMove(Pt(resized.Right + 20, resized.Bottom + 20,
                PointerButton.Left, ModifierKeys.Shift));
            resizeMove.OnPointerUp(Pt(resized.Right + 20, resized.Bottom + 20));
            resized = resizeMove.FloatingBounds;
            Check(resized.Width == resized.Height * 2,
                "reapplying Shift restores the original source aspect ratio");

            var beforeRotate = resized;
            var rotateHandle = resizeMove.RotationHandle;
            double handleDistance = 26 / Math.Max(0.05, ZoomFactor);
            double centerX = beforeRotate.Left + beforeRotate.Width / 2.0;
            double centerY = beforeRotate.Top + beforeRotate.Height / 2.0;
            resizeMove.OnPointerDown(Pt(rotateHandle.X, rotateHandle.Y));
            resizeMove.OnPointerMove(Pt(centerX + handleDistance, centerY,
                PointerButton.Left, ModifierKeys.Shift));
            resizeMove.OnPointerUp(Pt(centerX + handleDistance, centerY));
            resized = resizeMove.FloatingBounds;
            Check(Math.Abs(Math.Abs(resizeMove.RotationDegrees) - 90) < 0.1 && resized.Height > resized.Width,
                "round transform handle rotates pasted pixels and Shift snaps to 15-degree steps");
            await PumpAsync();
            Snapshot("16-paste-resize-handles");
            resizeMove.Commit();
            Check(Core.Imaging.ColorBgra.A(_active!.Document.ActiveLayer.Surface[(int)centerX, (int)centerY]) == 255,
                "resized and rotated pasted pixels commit from the immutable source");
            CloseWorkspace(_active);
            await PumpAsync();
        }
        catch (Exception ex)
        {
            Fail($"pasted-image resize threw: {ex.Message}");
        }

        int layersBeforeDroppedImport = _active!.Document.Layers.Count;
        bool importedDroppedLayer = ImportLayerFromPath(pngPath);
        Check(importedDroppedLayer && _active.Document.Layers.Count == layersBeforeDroppedImport + 1,
            "drag/drop Add layer imports into the current document");
        Check(_active.Document.ActiveLayer.Name == Path.GetFileNameWithoutExtension(pngPath),
            "drag/drop imported layer becomes active");
        Undo();

        // 21f. Click-to-deselect and Escape cancel/deselect behavior.
        var wand2 = Tool<MagicWandTool>();
        wand2.OnPointerDown(Pt(10, 10));
        await PumpAsync();
        Check(!_active!.Document.Selection.IsEmpty, "wand selection exists before click-deselect");
        var rectSel2 = Tool<RectangleSelectTool>();
        rectSel2.OnPointerDown(Pt(50, 50));
        rectSel2.OnPointerUp(Pt(50, 50));
        await PumpAsync();
        Check(_active.Document.Selection.IsEmpty, "plain click with a selection tool deselects");

        SelectAll();
        var lasso2 = Tool<LassoSelectTool>();
        lasso2.OnPointerDown(Pt(50, 50));
        lasso2.OnPointerUp(Pt(50, 50));
        Check(_active.Document.Selection.IsEmpty, "plain click with lasso select deselects");

        SelectAll();
        var escapeBrush = Tool<PaintbrushTool>();
        uint pixelBeforeCancelledStroke = _active.Document.ActiveLayer.Surface[5, 5];
        escapeBrush.OnPointerDown(Pt(5, 5));
        Check(escapeBrush.IsBusy, "stroke reports an operation in progress");
        CancelActiveOperationOrDeselect();
        Check(!escapeBrush.IsBusy && _active.Document.ActiveLayer.Surface[5, 5] == pixelBeforeCancelledStroke,
            "Escape cancels an active stroke and restores pixels");
        Check(!_active.Document.Selection.IsEmpty, "canceling an active tool preserves the selection");
        CancelActiveOperationOrDeselect();
        Check(_active.Document.Selection.IsEmpty, "Escape deselects when the active tool is idle");

        ActivateTool(rectSel2);
        DragTool(rectSel2, 20, 20, 70, 70);
        Check(!_active.Document.Selection.IsEmpty, "rectangle selection exists before Enter");
        CommitFromEnter();
        Check(_active.Document.Selection.IsEmpty, "Enter commits and deselects Rectangle Select");

        // 21g. Floating panels can dock on every supported canvas edge.
        var historySite = _panelSites.First(s => s.Name == "history");
        var toolsSite = _panelSites.First(s => s.Name == "tools");
        FloatSite(toolsSite);
        await PumpAsync();
        Check(toolsSite.Window is { IsVisible: true } && _toolPalette.Children.Count == 24,
            "familiar two-column Tools palette floats as a real panel");
        Check(_toolPalette.Children.OfType<ToggleButton>().All(b => b.Width == 42 && b.Height == 38),
            "Tools palette uses larger Paint.NET-style hit targets");
        Check(_toolPalette.Children.OfType<ToggleButton>().All(b =>
                b.Content is Viewbox { Child: Canvas canvas } && canvas.Children.Count > 0),
            "all tools use the redesigned colored vector icon system");
        if (toolsSite.Window != null)
        {
            SnapshotVisual(toolsSite.Window, "18-floating-tools");
            SetTheme(AppTheme.Light);
            await PumpAsync();
            SnapshotVisual(toolsSite.Window, "19-floating-tools-light");
            SetTheme(AppTheme.Dark);
            await PumpAsync();
        }
        DockSite(toolsSite, Panels.PanelDockEdge.Top);
        await PumpAsync();
        Check(toolsSite.State.Docked && _toolPalette.Columns == 12,
            "Tools palette reflows horizontally when docked above the canvas");
        FloatSite(toolsSite);
        await PumpAsync();
        Check(!toolsSite.State.Docked && _toolPalette.Columns == 2,
            "Tools palette returns to two columns when floated");
        ShowDockGuides(true);
        await PumpAsync();
        Check(_dockGuideOverlay.IsVisible && _dockGuideOverlay.Children.Count == 3,
            "panel dragging shows left, top, and right docking targets");
        Snapshot("17-panel-dock-targets");
        ShowDockGuides(false);
        FloatSite(historySite);
        await PumpAsync();
        Check(!historySite.State.Docked, "history panel can be floated");
        Check(historySite.Window is { IsVisible: true }, "history floating window is shown");
        if (historySite.Window != null)
            SnapshotVisual(historySite.Window, "13-floating-history");
        DockSite(historySite, Panels.PanelDockEdge.Left);
        await PumpAsync();
        Check(historySite.State.Docked && historySite.DockHost != null && historySite.State.DockSide == "Left",
            "history panel docks on the left");
        DockSite(historySite, Panels.PanelDockEdge.Top);
        await PumpAsync();
        Check(historySite.State.DockSide == "Top" && _topDock.Children.Count > 0,
            "history panel docks above the canvas");
        DockSite(historySite, Panels.PanelDockEdge.Right);
        await PumpAsync();
        Check(historySite.State.DockSide == "Right" && _rightDock.Children.Count > 0,
            "history panel docks on the right");
        FloatSite(historySite);
        await PumpAsync();
        Check(!historySite.State.Docked && historySite.Window is { IsVisible: true }, "history panel floated again");
        TogglePanel("history");
        await PumpAsync();
        Check(historySite.Window is { IsVisible: false }, "F6 toggle hides the history panel");
        TogglePanel("history");
        await PumpAsync();
        Check(historySite.Window is { IsVisible: true }, "F6 toggle shows the history panel again");

        // 22. The File menu opens, lays out items, and renders themed.
        var fileMenu = (System.Windows.Controls.MenuItem)_menuBar.Items[0];
        fileMenu.IsSubmenuOpen = true;
        await PumpAsync(200);
        var popup = fileMenu.Template.FindName("PART_Popup", fileMenu) as System.Windows.Controls.Primitives.Popup;
        Check(popup is { IsOpen: true }, "File menu popup opens");
        if (popup?.Child is FrameworkElement popupChild && popupChild.ActualHeight > 0)
        {
            Check(popupChild.ActualHeight > 100, "File menu popup laid out its items");
            SnapshotVisual(popupChild, "10-file-menu");
        }
        else
        {
            Fail("File menu popup child not laid out");
        }
        fileMenu.IsSubmenuOpen = false;
        await PumpAsync();

        // 23. The auto-generated effect dialog builds, previews and closes cleanly.
        var dlg = new Dialogs.EffectDialog(EffectRegistry.Effects.First(e => e is GaussianBlurEffect), _active!)
        {
            Owner = this,
        };
        dlg.Show();
        await PumpAsync(400); // give the live preview a chance to render
        Check(dlg.IsVisible, "effect dialog opened");
        SnapshotVisual(dlg, "11-effect-dialog");
        dlg.Close();
        await PumpAsync(100);
        Check(_active!.PreviewSurface == null, "effect preview removed after dialog close");

        // 24. New-document dialog builds and closes.
        var newDlg = new Dialogs.NewDocumentDialog(App.Settings) { Owner = this };
        newDlg.Show();
        await PumpAsync(100);
        Check(newDlg.IsVisible, "new document dialog opened");
        SnapshotVisual(newDlg, "12-new-dialog");
        newDlg.Close();

        // 25. Paint.NET-style paste and drag/drop choice dialogs render.
        var choicePreview = new Surface(80, 50);
        choicePreview.Clear(Core.Imaging.ColorBgra.Pack(30, 120, 210, 255));
        var pasteChoiceDlg = new Dialogs.PasteSizeDialog(choicePreview) { Owner = this };
        pasteChoiceDlg.Show();
        await PumpAsync(100);
        Check(pasteChoiceDlg.IsVisible, "oversized-paste choice dialog opened");
        Check(pasteChoiceDlg.Topmost, "oversized-paste choice stays above its source window");
        SnapshotVisual(pasteChoiceDlg, "14-paste-choice-dialog");
        pasteChoiceDlg.Close();

        var dropChoiceDlg = new Dialogs.FileDropDialog(1) { Owner = this };
        dropChoiceDlg.Show();
        await PumpAsync(100);
        Check(dropChoiceDlg.IsVisible, "drag-and-drop choice dialog opened");
        Check(dropChoiceDlg.Topmost, "drag-and-drop choice stays visible above File Explorer");
        SnapshotVisual(dropChoiceDlg, "15-drop-choice-dialog");
        dropChoiceDlg.Close();
    }

    private void SnapshotVisual(FrameworkElement element, string name)
    {
        try
        {
            int w = (int)Math.Max(1, element.ActualWidth);
            int h = (int)Math.Max(1, element.ActualHeight);
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(_testDir, name + ".png"));
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            _testLog.Add($"WARN  visual snapshot {name} failed: {ex.Message}");
        }
    }
}
