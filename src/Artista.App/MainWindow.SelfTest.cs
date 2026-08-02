using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        topLayer.Opacity = 128;
        _active.InvalidateComposite(_active.Document.Bounds);
        LayerDelete();
        await PumpAsync();
        Check(_active.Document.Layers.Count == layerCountBefore, "layer deleted");
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
