using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Artista.App.Dialogs;
using Artista.App.Models;
using Artista.App.Tools;
using Artista.Core.Documents;
using Artista.Core.Effects;
using Artista.Core.History;
using Artista.Core.Imaging;
using Artista.Core.IO;
using Artista.Core.Layers;
using Artista.Core.Selections;
using Microsoft.Win32;

namespace Artista.App;

public sealed partial class MainWindow
{
    private MenuItem _recentMenu = null!;
    private MenuItem _themeDark = null!, _themeLight = null!, _themeSystem = null!;
    private MenuItem _gridMenuItem = null!, _rulersMenuItem = null!;

    // ---------------- menu construction ----------------

    private Menu BuildMenu()
    {
        var menu = new Menu { Padding = new Thickness(2) };
        menu.SetResourceReference(Menu.BackgroundProperty, "MenuBackgroundBrush");

        menu.Items.Add(BuildFileMenu());
        menu.Items.Add(BuildEditMenu());
        menu.Items.Add(BuildViewMenu());
        menu.Items.Add(BuildImageMenu());
        menu.Items.Add(BuildLayersMenu());
        menu.Items.Add(BuildEffectMenu("_Adjustments", EffectRegistry.Adjustments));
        menu.Items.Add(BuildEffectMenu("Effect_s", EffectRegistry.Effects));
        menu.Items.Add(BuildHelpMenu());
        return menu;
    }

    private static MenuItem MI(string header, string? gesture, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        item.Click += onClick;
        return item;
    }

    private MenuItem BuildFileMenu()
    {
        var file = new MenuItem { Header = "_File" };
        file.Items.Add(MI("_New…", "Ctrl+N", (_, _) => FileNew()));
        file.Items.Add(MI("_Open…", "Ctrl+O", (_, _) => FileOpen()));
        _recentMenu = new MenuItem { Header = "Open _Recent" };
        file.Items.Add(_recentMenu);
        file.Items.Add(new Separator());
        file.Items.Add(MI("_Save", "Ctrl+S", (_, _) => FileSave()));
        file.Items.Add(MI("Save _As…", "Ctrl+Shift+S", (_, _) => FileSaveAs()));
        file.Items.Add(MI("_Export…", "Ctrl+E", (_, _) => FileExport()));
        file.Items.Add(new Separator());
        file.Items.Add(MI("_Close", "Ctrl+W", (_, _) => { if (_active != null) CloseWorkspace(_active); }));
        file.Items.Add(MI("E_xit", "Alt+F4", (_, _) => Close()));
        file.SubmenuOpened += (_, _) => RebuildRecentMenu();
        RebuildRecentMenu();
        return file;
    }

    private void RebuildRecentMenu()
    {
        _recentMenu.Items.Clear();
        if (App.Settings.RecentFiles.Count == 0)
        {
            _recentMenu.Items.Add(new MenuItem { Header = "(no recent files)", IsEnabled = false });
            return;
        }
        foreach (var path in App.Settings.RecentFiles.ToList())
        {
            var item = new MenuItem { Header = Path.GetFileName(path), ToolTip = path };
            item.Click += (_, _) => OpenFile(path);
            _recentMenu.Items.Add(item);
        }
        _recentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear list" };
        clear.Click += (_, _) =>
        {
            App.Settings.RecentFiles.Clear();
            App.Settings.Save();
        };
        _recentMenu.Items.Add(clear);
    }

    private MenuItem BuildEditMenu()
    {
        var edit = new MenuItem { Header = "_Edit" };
        edit.Items.Add(MI("_Undo", "Ctrl+Z", (_, _) => Undo()));
        edit.Items.Add(MI("_Redo", "Ctrl+Y", (_, _) => Redo()));
        edit.Items.Add(new Separator());
        edit.Items.Add(MI("Cu_t", "Ctrl+X", (_, _) => EditCut()));
        edit.Items.Add(MI("_Copy", "Ctrl+C", (_, _) => EditCopy()));
        edit.Items.Add(MI("_Paste", "Ctrl+V", (_, _) => EditPaste()));
        edit.Items.Add(MI("Paste _into New Image", "Ctrl+Alt+V", (_, _) => EditPasteIntoNewImage()));
        edit.Items.Add(MI("_Delete Selection Pixels", "Del", (_, _) => DeleteSelectionPixels()));
        edit.Items.Add(new Separator());
        edit.Items.Add(MI("Select _All", "Ctrl+A", (_, _) => SelectAll()));
        edit.Items.Add(MI("D_eselect", "Ctrl+Shift+A", (_, _) => Deselect()));
        edit.Items.Add(MI("_Invert Selection", "Ctrl+I", (_, _) => InvertSelection()));
        return edit;
    }

    private MenuItem BuildViewMenu()
    {
        var view = new MenuItem { Header = "_View" };
        view.Items.Add(MI("Zoom _In", "+", (_, _) => { _documentView.ZoomIn(); UpdateZoomStatus(); }));
        view.Items.Add(MI("Zoom _Out", "-", (_, _) => { _documentView.ZoomOut(); UpdateZoomStatus(); }));
        view.Items.Add(MI("_Fit to Window", "Ctrl+B", (_, _) => { _documentView.FitToWindow(); UpdateZoomStatus(); }));
        view.Items.Add(MI("_Actual Size", "Ctrl+Shift+1", (_, _) => { _documentView.ActualSize(); UpdateZoomStatus(); }));
        view.Items.Add(new Separator());
        _gridMenuItem = new MenuItem { Header = "Pixel _Grid (high zoom)", IsCheckable = true, IsChecked = App.Settings.ShowPixelGrid };
        _gridMenuItem.Click += (_, _) =>
        {
            App.Settings.ShowPixelGrid = _gridMenuItem.IsChecked;
            _documentView.Canvas.ShowPixelGrid = _gridMenuItem.IsChecked;
            App.Settings.Save();
            _documentView.Canvas.InvalidateVisual();
        };
        view.Items.Add(_gridMenuItem);
        _rulersMenuItem = new MenuItem { Header = "_Rulers", IsCheckable = true, IsChecked = App.Settings.ShowRulers };
        _rulersMenuItem.Click += (_, _) =>
        {
            App.Settings.ShowRulers = _rulersMenuItem.IsChecked;
            _documentView.ShowRulers = _rulersMenuItem.IsChecked;
            App.Settings.Save();
        };
        view.Items.Add(_rulersMenuItem);
        view.Items.Add(new Separator());

        var theme = new MenuItem { Header = "_Theme" };
        _themeDark = new MenuItem { Header = "_Dark", IsCheckable = true };
        _themeLight = new MenuItem { Header = "_Light", IsCheckable = true };
        _themeSystem = new MenuItem { Header = "Follow _Windows setting", IsCheckable = true };
        _themeDark.Click += (_, _) => SetTheme(AppTheme.Dark);
        _themeLight.Click += (_, _) => SetTheme(AppTheme.Light);
        _themeSystem.Click += (_, _) => SetTheme(AppTheme.System);
        theme.Items.Add(_themeDark);
        theme.Items.Add(_themeLight);
        theme.Items.Add(_themeSystem);
        view.Items.Add(theme);
        SyncThemeChecks();

        view.Items.Add(new Separator());
        foreach (var (name, header, gesture) in new[]
        {
            ("colors", "_Colors panel", "F8"),
            ("history", "H_istory panel", "F6"),
            ("layers", "La_yers panel", "F7"),
        })
        {
            var site = _panelSites.FirstOrDefault(s => s.Name == name);
            var item = new MenuItem { Header = header, InputGestureText = gesture, IsCheckable = true, IsChecked = site?.State.Visible ?? true };
            string captured = name;
            item.Click += (_, _) => TogglePanel(captured);
            if (site != null) site.MenuItem = item;
            view.Items.Add(item);
        }
        return view;
    }

    private void SetTheme(AppTheme theme)
    {
        ThemeManager.Apply(theme);
        App.Settings.Theme = theme.ToString();
        App.Settings.Save();
        SyncThemeChecks();
        _documentView.Canvas.InvalidateVisual();
    }

    private void SyncThemeChecks()
    {
        _themeDark.IsChecked = ThemeManager.Theme == AppTheme.Dark;
        _themeLight.IsChecked = ThemeManager.Theme == AppTheme.Light;
        _themeSystem.IsChecked = ThemeManager.Theme == AppTheme.System;
    }

    private MenuItem BuildImageMenu()
    {
        var image = new MenuItem { Header = "_Image" };
        image.Items.Add(MI("_Crop to Selection", "Ctrl+Shift+X", (_, _) => CropToSelection()));
        image.Items.Add(MI("_Resize…", "Ctrl+R", (_, _) => ResizeImage()));
        image.Items.Add(MI("Canvas _Size…", "Ctrl+Shift+R", (_, _) => ResizeCanvas()));
        image.Items.Add(new Separator());
        image.Items.Add(MI("Flip _Horizontal", null, (_, _) => TransformImage("Flip Horizontal", DocumentTransforms.FlipHorizontal)));
        image.Items.Add(MI("Flip _Vertical", null, (_, _) => TransformImage("Flip Vertical", DocumentTransforms.FlipVertical)));
        image.Items.Add(MI("Rotate 90° Clock_wise", null, (_, _) => TransformImage("Rotate 90° CW", d => DocumentTransforms.Rotate90(d, true))));
        image.Items.Add(MI("Rotate 90° Counter-cloc_kwise", null, (_, _) => TransformImage("Rotate 90° CCW", d => DocumentTransforms.Rotate90(d, false))));
        image.Items.Add(MI("Rotate _180°", null, (_, _) => TransformImage("Rotate 180°", DocumentTransforms.Rotate180)));
        image.Items.Add(new Separator());
        image.Items.Add(MI("_Flatten", "Ctrl+Shift+F", (_, _) => TransformImage("Flatten", DocumentTransforms.Flatten)));
        image.Items.Add(MI("_Properties…", null, (_, _) =>
        {
            if (_active != null) new ImagePropertiesDialog(_active) { Owner = this }.ShowDialog();
        }));
        return image;
    }

    private MenuItem BuildLayersMenu()
    {
        var layers = new MenuItem { Header = "_Layers" };
        layers.Items.Add(MI("_Add New Layer", "Ctrl+Shift+N", (_, _) => LayerAdd()));
        layers.Items.Add(MI("_Delete Layer", null, (_, _) => LayerDelete()));
        layers.Items.Add(MI("D_uplicate Layer", "Ctrl+Shift+D", (_, _) => LayerDuplicate()));
        layers.Items.Add(MI("_Merge Layer Down", "Ctrl+M", (_, _) => LayerMergeDown()));
        layers.Items.Add(new Separator());
        layers.Items.Add(MI("Import From _File…", null, (_, _) => LayerImportFromFile()));
        layers.Items.Add(new Separator());
        layers.Items.Add(MI("Move Layer _Up", null, (_, _) => LayerMoveUp()));
        layers.Items.Add(MI("Move Layer Dow_n", null, (_, _) => LayerMoveDown()));
        layers.Items.Add(new Separator());
        layers.Items.Add(MI("Layer _Properties…", "F4", (_, _) => LayerProperties()));
        return layers;
    }

    private MenuItem BuildEffectMenu(string header, IReadOnlyList<EffectBase> effects)
    {
        var root = new MenuItem { Header = header };
        var byCategory = effects.GroupBy(e => e.Category).OrderBy(g => g.Key);
        foreach (var group in byCategory)
        {
            ItemsControl parent = root;
            if (!string.IsNullOrEmpty(group.Key))
            {
                var sub = new MenuItem { Header = group.Key };
                root.Items.Add(sub);
                parent = sub;
            }
            foreach (var effect in group.OrderBy(e => e.Name))
            {
                var capturedEffect = effect;
                var item = new MenuItem { Header = effect.Name + (effect.IsConfigurable ? "…" : "") };
                item.Click += (_, _) => RunEffect(capturedEffect);
                parent.Items.Add(item);
            }
        }
        return root;
    }

    private MenuItem BuildHelpMenu()
    {
        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(MI("_Keyboard Shortcuts", "F1", (_, _) => new ShortcutsDialog { Owner = this }.ShowDialog()));
        help.Items.Add(MI("_About Artista", null, (_, _) => MessageBox.Show(this,
            "Artista — a personal Paint.NET-style image editor.\n\n" +
            "Built on .NET " + Environment.Version + " and WPF.\n" +
            "Layered .artz project format, OKLab color engine, full dark/light theming.",
            "About Artista", MessageBoxButton.OK, MessageBoxImage.Information)));
        return help;
    }

    private Border BuildToolbar()
    {
        var border = new Border
        {
            Padding = new Thickness(4, 3, 4, 3),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        border.SetResourceReference(Border.BackgroundProperty, "ToolbarBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        void AddButton(string icon, string tip, RoutedEventHandler onClick)
        {
            var button = new Button { Content = IconPath(icon, 15, 1.3), ToolTip = tip, Margin = new Thickness(1, 0, 1, 0) };
            button.SetResourceReference(StyleProperty, "IconButton");
            button.Click += onClick;
            panel.Children.Add(button);
        }
        void AddSep() => panel.Children.Add(new Border
        {
            Width = 1, Margin = new Thickness(5, 2, 5, 2),
            Background = (System.Windows.Media.Brush)FindResource("BorderLightBrush"),
        });

        AddButton("Icon.New", "New (Ctrl+N)", (_, _) => FileNew());
        AddButton("Icon.Open", "Open (Ctrl+O)", (_, _) => FileOpen());
        AddButton("Icon.Save", "Save (Ctrl+S)", (_, _) => FileSave());
        AddSep();
        AddButton("Icon.Cut", "Cut (Ctrl+X)", (_, _) => EditCut());
        AddButton("Icon.Copy", "Copy (Ctrl+C)", (_, _) => EditCopy());
        AddButton("Icon.Paste", "Paste (Ctrl+V)", (_, _) => EditPaste());
        AddSep();
        AddButton("Icon.Crop", "Crop to selection (Ctrl+Shift+X)", (_, _) => CropToSelection());
        AddButton("Icon.Deselect", "Deselect (Ctrl+Shift+A)", (_, _) => Deselect());
        AddSep();
        AddButton("Icon.Undo", "Undo (Ctrl+Z)", (_, _) => Undo());
        AddButton("Icon.Redo", "Redo (Ctrl+Y)", (_, _) => Redo());

        border.Child = panel;
        return border;
    }

    // ---------------- keyboard shortcuts ----------------

    private readonly List<(Key Key, ModifierKeys Mods, Action Action)> _shortcuts = new();

    /// <summary>Applies the shared shortcut set to a window's input bindings
    /// (the main window and every floating panel window).</summary>
    private void ApplyShortcuts(InputBindingCollection bindings)
    {
        foreach (var (key, mods, action) in _shortcuts)
            bindings.Add(new KeyBinding(new RelayCommand(action), key, mods));
    }

    private void RegisterShortcuts()
    {
        void Bind(Key key, ModifierKeys mods, Action action) => _shortcuts.Add((key, mods, action));
        Bind(Key.N, ModifierKeys.Control, FileNew);
        Bind(Key.O, ModifierKeys.Control, FileOpen);
        Bind(Key.S, ModifierKeys.Control, FileSave);
        Bind(Key.S, ModifierKeys.Control | ModifierKeys.Shift, FileSaveAs);
        Bind(Key.E, ModifierKeys.Control, FileExport);
        Bind(Key.W, ModifierKeys.Control, () => { if (_active != null) CloseWorkspace(_active); });
        Bind(Key.Z, ModifierKeys.Control, Undo);
        Bind(Key.Y, ModifierKeys.Control, Redo);
        Bind(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, Redo);
        Bind(Key.X, ModifierKeys.Control, EditCut);
        Bind(Key.C, ModifierKeys.Control, EditCopy);
        Bind(Key.V, ModifierKeys.Control, EditPaste);
        Bind(Key.V, ModifierKeys.Control | ModifierKeys.Alt, EditPasteIntoNewImage);
        Bind(Key.A, ModifierKeys.Control, SelectAll);
        Bind(Key.A, ModifierKeys.Control | ModifierKeys.Shift, Deselect);
        Bind(Key.D, ModifierKeys.Control, Deselect); // Paint.NET's deselect
        Bind(Key.I, ModifierKeys.Control, InvertSelection);
        Bind(Key.R, ModifierKeys.Control, ResizeImage);
        Bind(Key.R, ModifierKeys.Control | ModifierKeys.Shift, ResizeCanvas);
        Bind(Key.X, ModifierKeys.Control | ModifierKeys.Shift, CropToSelection);
        Bind(Key.F, ModifierKeys.Control | ModifierKeys.Shift, () => TransformImage("Flatten", DocumentTransforms.Flatten));
        Bind(Key.N, ModifierKeys.Control | ModifierKeys.Shift, LayerAdd);
        Bind(Key.D, ModifierKeys.Control | ModifierKeys.Shift, LayerDuplicate);
        Bind(Key.M, ModifierKeys.Control, LayerMergeDown);
        Bind(Key.F4, ModifierKeys.None, LayerProperties);
        Bind(Key.B, ModifierKeys.Control, () => { _documentView.FitToWindow(); UpdateZoomStatus(); });
        Bind(Key.D1, ModifierKeys.Control | ModifierKeys.Shift, () => { _documentView.ActualSize(); UpdateZoomStatus(); });
        Bind(Key.F1, ModifierKeys.None, () => new ShortcutsDialog { Owner = this }.ShowDialog());
        Bind(Key.F6, ModifierKeys.None, () => TogglePanel("history"));
        Bind(Key.F7, ModifierKeys.None, () => TogglePanel("layers"));
        Bind(Key.F8, ModifierKeys.None, () => TogglePanel("colors"));
        ApplyShortcuts(InputBindings);
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action _action;
        public RelayCommand(Action action) => _action = action;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    // ---------------- file operations ----------------

    public void NewDocumentFromDefaults()
    {
        var settings = App.Settings;
        CreateDocument(settings.DefaultDocumentWidth, settings.DefaultDocumentHeight, settings.DefaultDocumentBackground);
    }

    private void FileNew()
    {
        var dialog = new NewDocumentDialog(App.Settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        App.Settings.DefaultDocumentWidth = dialog.DocWidth;
        App.Settings.DefaultDocumentHeight = dialog.DocHeight;
        App.Settings.DefaultDocumentBackground = dialog.BackgroundKind;
        App.Settings.Save();
        CreateDocument(dialog.DocWidth, dialog.DocHeight, dialog.BackgroundKind);
    }

    private void CreateDocument(int width, int height, string background)
    {
        var doc = new Document(width, height);
        var layer = new Layer(width, height, "Background");
        switch (background)
        {
            case "White": layer.Surface.Clear(0xFFFFFFFFu); break;
            case "Color": layer.Surface.Clear(_environment.PrimaryColor); break;
            // Transparent: leave cleared.
        }
        doc.Layers.Add(layer);
        AddWorkspace(new DocumentWorkspace(doc));
    }

    private void FileOpen()
    {
        var dialog = new OpenFileDialog { Filter = ImageCodec.OpenFilter, Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var path in dialog.FileNames)
            OpenFile(path);
    }

    public void OpenFilesOnStartup(string[] paths)
    {
        foreach (var path in paths)
            if (File.Exists(path))
                OpenFile(path);
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped) return;
        var files = dropped.Where(File.Exists).ToArray();
        if (files.Length == 0) return;
        e.Handled = true;

        // Explorer may reclaim foreground activation until its OLE drop callback
        // returns. Defer the modal window so it is owned and activated afterward.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => ShowFileDropDialog(files)));
    }

    private void ShowFileDropDialog(string[] files)
    {
        Activate();
        var dialog = new FileDropDialog(files.Length) { Owner = this, Topmost = true };
        if (dialog.ShowDialog() != true) return;
        if (dialog.Choice == FileDropChoice.Open)
        {
            foreach (var file in files)
                OpenFile(file);
        }
        else if (dialog.Choice == FileDropChoice.AddAsLayers)
        {
            foreach (var file in files)
                ImportLayerFromPath(file);
        }
    }

    public void OpenFile(string path)
    {
        try
        {
            // Already open? Just activate it.
            var existing = _workspaces.FirstOrDefault(w =>
                string.Equals(w.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ActivateWorkspace(existing);
                return;
            }

            DocumentWorkspace ws;
            if (Path.GetExtension(path).Equals(ArtzFormat.Extension, StringComparison.OrdinalIgnoreCase))
            {
                var doc = ArtzFormat.Load(path);
                ws = new DocumentWorkspace(doc) { FilePath = path };
            }
            else
            {
                var surface = ImageCodec.Load(path);
                var doc = new Document(surface.Width, surface.Height);
                doc.Layers.Add(new Layer(surface, "Background"));
                ws = new DocumentWorkspace(doc) { FilePath = path };
            }
            ws.MarkSaved();
            AddWorkspace(ws);
            App.Settings.AddRecentFile(path);
            App.Settings.Save();
            SetStatus($"Opened {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Could not open \"{Path.GetFileName(path)}\":\n\n{ex.Message}",
                "Open failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FileSave()
    {
        if (_active == null) return;
        if (_active.FilePath == null)
        {
            FileSaveAs();
            return;
        }
        SaveWorkspaceTo(_active, _active.FilePath);
    }

    private void FileSaveAs()
    {
        if (_active == null) return;
        var dialog = new SaveFileDialog
        {
            Filter = ImageCodec.SaveFilter,
            FileName = Path.GetFileNameWithoutExtension(_active.FilePath ?? "Untitled"),
            DefaultExt = ".artz",
        };
        if (dialog.ShowDialog(this) != true) return;
        SaveWorkspaceTo(_active, dialog.FileName);
    }

    private void FileExport()
    {
        if (_active == null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|BMP (*.bmp)|*.bmp|GIF (*.gif)|*.gif|TIFF (*.tiff)|*.tiff",
            FileName = Path.GetFileNameWithoutExtension(_active.FilePath ?? "Untitled"),
            DefaultExt = ".png",
        };
        if (dialog.ShowDialog(this) != true) return;
        _activeTool?.OnCommit();
        try
        {
            var format = ImageCodec.FormatFromExtension(dialog.FileName) ?? ImageFormat.Png;
            ImageCodec.Save(_active.Document.Flatten(), dialog.FileName, format, App.Settings.JpegQuality);
            SetStatus($"Exported to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed:\n\n{ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool SaveWorkspaceTo(DocumentWorkspace ws, string path)
    {
        try
        {
            if (ReferenceEquals(ws, _active))
                _activeTool?.OnCommit();
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ArtzFormat.Extension)
            {
                ArtzFormat.Save(ws.Document, path);
            }
            else
            {
                var format = ImageCodec.FormatFromExtension(path);
                if (format == null)
                {
                    path += ".artz";
                    ArtzFormat.Save(ws.Document, path);
                }
                else
                {
                    if (ws.Document.Layers.Count > 1 && format != ImageFormat.Png)
                        SetStatus("Note: flat image formats save the flattened image. Use .artz to keep layers.");
                    ImageCodec.Save(ws.Document.Flatten(), path, format.Value, App.Settings.JpegQuality);
                }
            }
            ws.FilePath = path;
            ws.MarkSaved();
            App.Settings.AddRecentFile(path);
            App.Settings.Save();
            RefreshTabs();
            SetStatus($"Saved {Path.GetFileName(path)}.");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Save failed:\n\n{ex.Message}", "Save", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void CloseWorkspace(DocumentWorkspace ws)
    {
        if (!ConfirmCloseWorkspace(ws)) return;
        int index = _workspaces.IndexOf(ws);
        _workspaces.Remove(ws);
        if (_active == ws)
        {
            _active = null;
            if (_workspaces.Count > 0)
                ActivateWorkspace(_workspaces[Math.Clamp(index, 0, _workspaces.Count - 1)]);
            else
            {
                _documentView.SetWorkspace(null);
                RefreshTabs();
                RefreshAllPanels();
            }
        }
        else
        {
            RefreshTabs();
        }
    }

    private bool ConfirmCloseWorkspace(DocumentWorkspace ws)
    {
        if (!ws.IsDirty || SuppressCloseConfirmation) return true;
        ActivateWorkspace(ws);
        var result = MessageBox.Show(this,
            $"Save changes to {ws.DisplayName.TrimEnd('*', ' ')}?",
            "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.Yes)
        {
            if (ws.FilePath == null)
            {
                var dialog = new SaveFileDialog { Filter = ImageCodec.SaveFilter, DefaultExt = ".artz" };
                if (dialog.ShowDialog(this) != true) return false;
                return SaveWorkspaceTo(ws, dialog.FileName);
            }
            return SaveWorkspaceTo(ws, ws.FilePath);
        }
        return true;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        foreach (var ws in _workspaces.ToList())
        {
            if (!ConfirmCloseWorkspace(ws))
            {
                e.Cancel = true;
                return;
            }
        }
        SavePanelStates();
        App.Settings.Save();
    }

    // ---------------- edit operations ----------------

    private void Undo()
    {
        if (_active == null || !_active.History.CanUndo) return;
        _activeTool?.OnCancel();
        _active.History.Undo();
        _active.NotifyStructureChanged();
        RefreshAllPanels();
        _documentView.Canvas.InvalidateVisual();
        SetStatus("Undone.");
    }

    private void Redo()
    {
        if (_active == null || !_active.History.CanRedo) return;
        _activeTool?.OnCancel();
        _active.History.Redo();
        _active.NotifyStructureChanged();
        RefreshAllPanels();
        _documentView.Canvas.InvalidateVisual();
        SetStatus("Redone.");
    }

    private void EditCopy()
    {
        TryCopyToClipboard();
    }

    private bool TryCopyToClipboard()
    {
        if (_active == null) return false;
        _activeTool?.OnCommit();
        var doc = _active.Document;
        var source = doc.ActiveLayer.Surface;
        var bounds = doc.Selection.IsEmpty ? FindContentBounds(source) : doc.Selection.Bounds;
        if (bounds.IsEmpty)
        {
            SetStatus("The active layer has no pixels to copy.");
            return false;
        }
        var region = new Surface(bounds.Width, bounds.Height);
        for (int y = 0; y < bounds.Height; y++)
        {
            var src = source.GetRowSpan(bounds.Top + y, bounds.Left, bounds.Width);
            var dst = region.GetRow(y);
            src.CopyTo(dst);
            if (!doc.Selection.IsEmpty)
            {
                for (int x = 0; x < bounds.Width; x++)
                {
                    byte cov = doc.Selection.MaskAt(bounds.Left + x, bounds.Top + y);
                    if (cov == 255) continue;
                    dst[x] = ColorBgra.WithAlpha(dst[x], (byte)(ColorBgra.A(dst[x]) * cov / 255));
                }
            }
        }
        try
        {
            // Standard bitmap for other apps + "PNG" format so transparency
            // survives a round trip (plain DIBs have no alpha channel).
            var data = new DataObject();
            var bitmapSource = ImageCodec.ToBitmapSource(region);
            data.SetImage(bitmapSource);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            var png = new MemoryStream();
            encoder.Save(png);
            png.Position = 0;
            data.SetData("PNG", png);
            Clipboard.SetDataObject(data, copy: true);
            SetStatus("Copied.");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Copy failed: {ex.Message}");
            return false;
        }
    }

    private static RectInt FindContentBounds(Surface surface)
    {
        int left = surface.Width, top = surface.Height, right = -1, bottom = -1;
        for (int y = 0; y < surface.Height; y++)
        {
            var row = surface.GetRow(y);
            for (int x = 0; x < surface.Width; x++)
            {
                if (ColorBgra.A(row[x]) == 0) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }
        return right < left ? RectInt.Empty : RectInt.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private void EditCut()
    {
        if (TryCopyToClipboard())
            DeleteSelectionPixels();
    }

    private void DeleteSelectionPixels()
    {
        if (_active == null) return;
        var doc = _active.Document;
        var layer = doc.ActiveLayer;
        if (layer.Locked || !layer.Visible)
        {
            SetStatus("The active layer is locked or hidden.");
            return;
        }
        var bounds = doc.Selection.EffectiveBounds;
        var before = layer.Surface.ExtractRect(bounds);
        bool hasSelection = !doc.Selection.IsEmpty;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            var row = layer.Surface.GetRowSpan(y, bounds.Left, bounds.Width);
            for (int x = 0; x < bounds.Width; x++)
            {
                byte cov = hasSelection ? doc.Selection.MaskAt(bounds.Left + x, y) : (byte)255;
                if (cov == 0) continue;
                row[x] = cov == 255 ? 0 : ColorBgra.WithAlpha(row[x], (byte)(ColorBgra.A(row[x]) * (255 - cov) / 255));
            }
        }
        PushHistory(new SurfaceRegionMemento("Delete Selection", layer, bounds, before), "Icon.Delete");
        InvalidateDocument(bounds);
    }

    private void EditPaste()
    {
        if (_active == null)
        {
            EditPasteIntoNewImage();
            return;
        }
        var surface = GetClipboardSurface();
        if (surface == null)
        {
            SetStatus("Clipboard has no image.");
            return;
        }
        var doc = _active.Document;
        bool expandCanvas = false;
        if (surface.Width > doc.Width || surface.Height > doc.Height)
        {
            var dialog = new PasteSizeDialog(surface) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Choice == PasteSizeChoice.Cancel)
            {
                SetStatus("Paste cancelled.");
                return;
            }
            expandCanvas = dialog.Choice == PasteSizeChoice.ExpandCanvas;
        }
        PasteSurface(surface, expandCanvas);
    }

    private void PasteSurface(Surface surface, bool expandCanvas)
    {
        if (_active == null) return;
        _activeTool?.OnCommit();
        var doc = _active.Document;
        var targetLayer = doc.ActiveLayer;
        if (targetLayer.Locked || !targetLayer.Visible)
        {
            SetStatus("The selected layer is locked or hidden.");
            return;
        }
        HistoryMemento? pasteMemento = null;
        byte[] selectionBefore = doc.Selection.SnapshotMask();

        if (expandCanvas)
        {
            pasteMemento = new DocumentStructureMemento("Paste", doc);
            int newWidth = Math.Max(doc.Width, surface.Width);
            int newHeight = Math.Max(doc.Height, surface.Height);
            DocumentTransforms.ResizeCanvas(doc, newWidth, newHeight, AnchorPosition.TopLeft);
        }

        int ox = (doc.Width - surface.Width) / 2;
        int oy = (doc.Height - surface.Height) / 2;
        var pasteRect = new RectInt(ox, oy, surface.Width, surface.Height).Intersect(doc.Bounds);
        var layerBeforePaste = targetLayer.Surface.Clone();

        if (!expandCanvas)
        {
            pasteMemento = new CompositeMemento("Paste", new HistoryMemento[]
            {
                new SurfaceRegionMemento("Paste", targetLayer, pasteRect),
                new SelectionMemento("Paste", selectionBefore),
            });
        }

        targetLayer.Surface.DrawSurfaceOver(surface, ox, oy);
        _active.InvalidateComposite(pasteRect);
        var moveTool = _tools.OfType<MoveSelectedPixelsTool>().First();
        ActivateTool(moveTool);
        moveTool.BeginPaste(surface, targetLayer, layerBeforePaste, ox, oy);
        PushHistory(pasteMemento ?? throw new InvalidOperationException("Paste history was not initialized."), "Icon.Paste");
        SetStatus(expandCanvas
            ? "Pasted into the selected layer and expanded the canvas. Drag a corner to resize; hold Shift to preserve aspect ratio."
            : "Pasted into the selected layer. Drag a corner to resize (Shift preserves aspect ratio); Enter finishes and deselects.");
    }

    private void EditPasteIntoNewImage()
    {
        var surface = GetClipboardSurface();
        if (surface == null)
        {
            SetStatus("Clipboard has no image.");
            return;
        }
        var doc = new Document(surface.Width, surface.Height);
        doc.Layers.Add(new Layer(surface, "Background"));
        AddWorkspace(new DocumentWorkspace(doc));
    }

    private static Surface? GetClipboardSurface()
    {
        try
        {
            // Prefer the "PNG" clipboard format (preserves alpha; written by us,
            // GIMP, Chrome, etc.), falling back to the alpha-less standard bitmap.
            var data = Clipboard.GetDataObject();
            if (data?.GetDataPresent("PNG") == true && data.GetData("PNG") is Stream pngStream)
            {
                using var ms = new MemoryStream();
                pngStream.CopyTo(ms);
                ms.Position = 0;
                var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count > 0)
                    return ImageCodec.FromBitmapSource(decoder.Frames[0]);
            }
            if (!Clipboard.ContainsImage()) return null;
            var image = Clipboard.GetImage();
            return image == null ? null : ImageCodec.FromBitmapSource(image);
        }
        catch
        {
            return null;
        }
    }

    private void SelectAll()
    {
        if (_active == null) return;
        var before = _active.Document.Selection.SnapshotMask();
        _active.Document.Selection.SelectAll();
        PushHistory(new SelectionMemento("Select All", before), "Icon.RectSelect");
        NotifySelectionChanged();
    }

    private void Deselect()
    {
        if (_active == null || _active.Document.Selection.IsEmpty) return;
        _activeTool?.OnCommit();
        var before = _active.Document.Selection.SnapshotMask();
        _active.Document.Selection.Clear();
        PushHistory(new SelectionMemento("Deselect", before), "Icon.Deselect");
        NotifySelectionChanged();
    }

    private void InvertSelection()
    {
        if (_active == null || _active.Document.Selection.IsEmpty) return;
        var before = _active.Document.Selection.SnapshotMask();
        _active.Document.Selection.Invert();
        PushHistory(new SelectionMemento("Invert Selection", before), "Icon.RectSelect");
        NotifySelectionChanged();
    }

    // ---------------- image operations ----------------

    private void TransformImage(string name, Action<Document> transform)
    {
        if (_active == null) return;
        _activeTool?.OnCancel();
        var memento = new DocumentStructureMemento(name, _active.Document);
        transform(_active.Document);
        PushHistory(memento, "Icon.Properties");
        _active.NotifyStructureChanged();
        RefreshAllPanels();
        _documentView.FitToWindow();
    }

    private void ResizeImage()
    {
        if (_active == null) return;
        var dialog = new ResizeImageDialog(_active.Document.Width, _active.Document.Height) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        TransformImage("Resize Image", d => DocumentTransforms.ResizeImage(d, dialog.NewWidth, dialog.NewHeight, dialog.Mode));
    }

    private void ResizeCanvas()
    {
        if (_active == null) return;
        var dialog = new CanvasSizeDialog(_active.Document.Width, _active.Document.Height) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        TransformImage("Canvas Size", d => DocumentTransforms.ResizeCanvas(d, dialog.NewWidth, dialog.NewHeight, dialog.Anchor));
    }

    private void CropToSelection()
    {
        if (_active == null || _active.Document.Selection.IsEmpty)
        {
            SetStatus("Make a selection first.");
            return;
        }
        _activeTool?.OnCommit();
        var mask = _active.Document.Selection.SnapshotMask();
        var bounds = _active.Document.Selection.Bounds;
        TransformImage("Crop to Selection", d => DocumentTransforms.CropTo(d, bounds, mask));
    }

    // ---------------- layer operations ----------------

    public void LayerAdd()
    {
        if (_active == null) return;
        _activeTool?.OnCommit();
        var doc = _active.Document;
        var layer = new Layer(doc.Width, doc.Height, $"Layer {doc.Layers.Count + 1}");
        doc.Layers.Insert(doc.ActiveLayerIndex + 1, layer);
        doc.ActiveLayerIndex++;
        PushHistory(new LayerAddedMemento("Add Layer", layer), "Icon.Plus");
        _active.NotifyStructureChanged();
        RefreshAllPanels();
    }

    public void LayerDelete()
    {
        if (_active == null || _active.Document.Layers.Count <= 1)
        {
            SetStatus("A document must keep at least one layer.");
            return;
        }
        _activeTool?.OnCommit();
        var doc = _active.Document;
        int index = doc.ActiveLayerIndex;
        var layer = doc.Layers[index];
        doc.Layers.RemoveAt(index);
        doc.ActiveLayerIndex = Math.Clamp(index - 1, 0, doc.Layers.Count - 1);
        PushHistory(new LayerRemovedMemento("Delete Layer", layer, index), "Icon.Delete");
        _active.NotifyStructureChanged();
        RefreshAllPanels();
    }

    public void LayerDuplicate()
    {
        if (_active == null) return;
        _activeTool?.OnCommit();
        var doc = _active.Document;
        var copy = doc.ActiveLayer.Clone(doc.ActiveLayer.Name + " copy");
        doc.Layers.Insert(doc.ActiveLayerIndex + 1, copy);
        doc.ActiveLayerIndex++;
        PushHistory(new LayerAddedMemento("Duplicate Layer", copy), "Icon.Duplicate");
        _active.NotifyStructureChanged();
        RefreshAllPanels();
    }

    public void LayerMergeDown()
    {
        if (_active == null || _active.Document.ActiveLayerIndex == 0)
        {
            SetStatus("There is no layer below to merge into.");
            return;
        }
        TransformImage("Merge Layer Down", d => DocumentTransforms.MergeDown(d, d.ActiveLayerIndex));
    }

    public void LayerMoveUp() => MoveLayer(+1);
    public void LayerMoveDown() => MoveLayer(-1);

    private void MoveLayer(int delta)
    {
        if (_active == null) return;
        var doc = _active.Document;
        int from = doc.ActiveLayerIndex;
        int to = from + delta;
        if (to < 0 || to >= doc.Layers.Count) return;
        _activeTool?.OnCommit();
        var layer = doc.Layers[from];
        var memento = new LayerOrderMemento("Reorder Layers", layer.Id, from);
        doc.Layers.RemoveAt(from);
        doc.Layers.Insert(to, layer);
        doc.ActiveLayerIndex = to;
        PushHistory(memento, delta > 0 ? "Icon.ArrowUp" : "Icon.ArrowDown");
        _active.NotifyStructureChanged();
        RefreshAllPanels();
    }

    public void LayerProperties()
    {
        if (_active == null) return;
        _activeTool?.OnCommit();
        var layer = _active.Document.ActiveLayer;
        var dialog = new LayerPropertiesDialog(layer) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var memento = new LayerPropertiesMemento("Layer Properties", layer);
        layer.Name = dialog.LayerName;
        layer.Opacity = dialog.LayerOpacity;
        layer.BlendMode = dialog.Blend;
        layer.Visible = dialog.LayerVisible;
        layer.Locked = dialog.LayerLocked;
        layer.AlphaLocked = dialog.LayerAlphaLocked;
        PushHistory(memento, "Icon.Properties");
        _active.MarkDirty();
        InvalidateDocument(_active.Document.Bounds);
        RefreshAllPanels();
    }

    private void LayerImportFromFile()
    {
        if (_active == null) return;
        var dialog = new OpenFileDialog { Filter = ImageCodec.OpenFilter };
        if (dialog.ShowDialog(this) != true) return;
        ImportLayerFromPath(dialog.FileName);
    }

    private bool ImportLayerFromPath(string path)
    {
        if (_active == null) return false;
        try
        {
            _activeTool?.OnCommit();
            var surface = Path.GetExtension(path).Equals(ArtzFormat.Extension, StringComparison.OrdinalIgnoreCase)
                ? ArtzFormat.Load(path).Flatten()
                : ImageCodec.Load(path);
            var doc = _active.Document;
            var layer = new Layer(doc.Width, doc.Height, Path.GetFileNameWithoutExtension(path));
            int ox = (doc.Width - surface.Width) / 2;
            int oy = (doc.Height - surface.Height) / 2;
            layer.Surface.DrawSurfaceOver(surface, ox, oy);
            doc.Layers.Insert(doc.ActiveLayerIndex + 1, layer);
            doc.ActiveLayerIndex++;
            PushHistory(new LayerAddedMemento("Import Layer", layer), "Icon.Open");
            _active.NotifyStructureChanged();
            RefreshAllPanels();
            SetStatus($"Added {Path.GetFileName(path)} as a layer.");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not import \"{Path.GetFileName(path)}\":\n\n{ex.Message}",
                "Import layer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    // ---------------- effects ----------------

    private void RunEffect(EffectBase effect)
    {
        if (_active == null) return;
        var ws = _active;
        var layer = ws.Document.ActiveLayer;
        if (layer.Locked || !layer.Visible)
        {
            SetStatus("The active layer is locked or hidden. Unlock it to apply effects.");
            return;
        }
        _activeTool?.OnCommit();

        ParameterSet parameters;
        if (effect.IsConfigurable)
        {
            var dialog = new EffectDialog(effect, ws) { Owner = this };
            dialog.EyedropperRequested += callback => BeginEyedropper(c =>
            {
                callback(c);
                dialog.Activate();
            });
            if (dialog.ShowDialog() != true)
            {
                // Cancelled: EffectDialog already removed the preview; the layer
                // itself was never modified.
                SetStatus($"{effect.Name} cancelled.");
                return;
            }
            parameters = dialog.Parameters;
        }
        else
        {
            parameters = ParameterSet.FromDefaults(effect.CreateParameters());
        }
        ApplyEffect(effect, parameters);
    }

    /// <summary>Synchronous effect application used by the self-test harness.</summary>
    public void ApplyEffectSync(EffectBase effect, ParameterSet parameters)
    {
        if (_active == null) return;
        var doc = _active.Document;
        var targets = ResolveEffectTargets(effect, parameters, doc);
        if (targets.Count == 0) return;
        var selection = ResolveEffectSelection(effect, parameters, doc);
        var roi = selection.IsEmpty ? doc.Bounds : selection.Bounds;
        var results = new List<(Layer Layer, Surface Result)>();
        foreach (var target in targets)
        {
            var result = target.Surface.Clone();
            EffectRunner.RunMasked(effect, target.Surface, result, parameters, selection, doc.Bounds, CancellationToken.None);
            results.Add((target, result));
        }
        CommitEffectResults(effect, results, roi);
    }

    private static List<Layer> ResolveEffectTargets(EffectBase effect, ParameterSet parameters, Document doc)
    {
        var targets = new List<Layer> { doc.ActiveLayer };
        if (effect is RemoveColorEffect)
        {
            int scope = parameters.GetEnum(RemoveColorEffect.ScopeParamId);
            if (scope != RemoveColorEffect.ScopeCurrentLayer)
            {
                targets = doc.Layers
                    .Where(l => l.Visible || scope == RemoveColorEffect.ScopeAllLayers)
                    .ToList();
            }
        }
        targets.RemoveAll(l => l.Locked);
        return targets;
    }

    private static Selection ResolveEffectSelection(EffectBase effect, ParameterSet parameters, Document doc)
    {
        if (effect is RemoveColorEffect && !parameters.GetBool("limitToSelection"))
            return new Selection(doc.Width, doc.Height);
        return doc.Selection;
    }

    private void CommitEffectResults(EffectBase effect, List<(Layer Layer, Surface Result)> results, RectInt roi)
    {
        if (_active == null) return;
        var mementos = new List<HistoryMemento>();
        foreach (var (target, result) in results)
        {
            var before = target.Surface.ExtractRect(roi);
            mementos.Add(new SurfaceRegionMemento(effect.Name, target, roi, before));
            target.Surface.CopyRect(result, roi);
        }
        PushHistory(mementos.Count == 1 ? mementos[0] : new CompositeMemento(effect.Name, mementos), "Icon.Properties");
        InvalidateDocument(_active.Document.Bounds);
        SetStatus($"{effect.Name} applied.");
    }

    private void ApplyEffect(EffectBase effect, ParameterSet parameters)
    {
        if (_active == null) return;
        var ws = _active;
        var doc = ws.Document;

        var targets = ResolveEffectTargets(effect, parameters, doc);
        if (targets.Count == 0)
        {
            SetStatus("No editable layers in scope.");
            return;
        }
        var selection = ResolveEffectSelection(effect, parameters, doc);
        var roi = selection.IsEmpty ? doc.Bounds : selection.Bounds;
        var progress = new ProgressDialog(effect.Name, this);
        var results = new List<(Layer Layer, Surface Result)>();
        var task = Task.Run(() =>
        {
            foreach (var target in targets)
            {
                progress.Cts.Token.ThrowIfCancellationRequested();
                var src = target.Surface;
                var result = src.Clone();
                EffectRunner.RunMasked(effect, src, result, parameters, selection, doc.Bounds, progress.Cts.Token);
                lock (results)
                    results.Add((target, result));
            }
        });
        task.ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            progress.Close();
            if (t.IsFaulted)
            {
                var ex = t.Exception?.GetBaseException();
                if (ex is OperationCanceledException)
                {
                    SetStatus($"{effect.Name} cancelled — image unchanged.");
                }
                else
                {
                    MessageBox.Show(this, $"The effect failed:\n\n{ex?.Message}", effect.Name,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }
            if (t.IsCanceled || progress.Cts.IsCancellationRequested)
            {
                SetStatus($"{effect.Name} cancelled — image unchanged.");
                return;
            }
            // Commit all results as one history step.
            lock (results)
                CommitEffectResults(effect, results, roi);
        }));
        progress.ShowDialog();
    }
}

/// <summary>Keyboard shortcut reference (Help → Keyboard Shortcuts, F1).</summary>
public sealed class ShortcutsDialog : DialogBase
{
    public ShortcutsDialog() : base("Keyboard Shortcuts")
    {
        CancelButton.Visibility = Visibility.Collapsed;
        var grid = new Grid { Margin = new Thickness(4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var shortcuts = new (string Keys, string Action)[]
        {
            ("Ctrl+N / Ctrl+O", "New / Open"),
            ("Ctrl+S / Ctrl+Shift+S", "Save / Save As"),
            ("Ctrl+E", "Export flattened image"),
            ("Ctrl+Z / Ctrl+Y", "Undo / Redo"),
            ("Ctrl+X / Ctrl+C / Ctrl+V", "Cut / Copy / Paste"),
            ("Ctrl+Alt+V", "Paste into new image"),
            ("Ctrl+A", "Select all"),
            ("Ctrl+D / Ctrl+Shift+A / Esc", "Deselect (Esc when no operation is active)"),
            ("Ctrl+I", "Invert selection"),
            ("Delete", "Clear selected pixels"),
            ("Ctrl+R / Ctrl+Shift+R", "Resize image / canvas"),
            ("Ctrl+Shift+X", "Crop to selection"),
            ("Mouse wheel / Shift+wheel", "Scroll vertically / horizontally"),
            ("+ / - or Ctrl+wheel", "Zoom in / out (cursor centered)"),
            ("Ctrl+B / Ctrl+Shift+1", "Fit to window / actual size"),
            ("Space+drag or middle-drag", "Pan the view (works while scrolling)"),
            ("F6 / F7 / F8", "Toggle History / Layers / Colors panels"),
            ("Escape / Enter", "Cancel / commit the current operation"),
            ("X", "Swap primary and secondary colors"),
            ("B / E / P", "Paintbrush / Eraser / Pencil"),
            ("S / W / M", "Rectangle select / Magic wand / Move pixels"),
            ("F / G / K / T", "Bucket / Gradient / Color picker / Text"),
            ("Q", "Color Remover brush"),
            ("H / Z", "Pan / Zoom tool"),
            ("F1", "This reference"),
        };
        int row = 0;
        foreach (var (keys, action) in shortcuts)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            var keyText = new TextBlock { Text = keys, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 2, 0, 2) };
            var actionText = new TextBlock { Text = action, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetRow(keyText, row);
            Grid.SetColumn(keyText, 0);
            Grid.SetRow(actionText, row);
            Grid.SetColumn(actionText, 2);
            grid.Children.Add(keyText);
            grid.Children.Add(actionText);
            row++;
        }
        Body.Children.Add(grid);
    }
}
