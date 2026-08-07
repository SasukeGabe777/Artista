using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Artista.App.Controls;
using Artista.App.Models;
using Artista.App.Panels;
using Artista.App.Tools;
using Artista.Core.History;
using Artista.Core.Imaging;
using Artista.Core.Selections;

namespace Artista.App;

/// <summary>
/// The application shell: menu bar, toolbar, tool settings bar, tool palette,
/// tabbed documents, Colors/History/Layers panels and status bar — arranged to
/// mirror the Paint.NET workspace.
/// </summary>
public sealed partial class MainWindow : Window, IShellHost, IToolContext
{
    private readonly List<DocumentWorkspace> _workspaces = new();
    private DocumentWorkspace? _active;
    private readonly IReadOnlyList<ToolBase> _tools;
    private ToolBase? _activeTool;
    private readonly ToolEnvironment _environment = new();

    private readonly DocumentView _documentView = new();
    private readonly ListBox _tabStrip = new();
    private readonly UniformGrid _toolPalette = new() { Columns = 2 };
    private readonly Grid _dockGuideOverlay = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly StackPanel _settingsBar = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _settingsToolName = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 10, 0) };
    private ColorsPanel _colorsPanel = null!;
    private HistoryPanel _historyPanel = null!;
    private LayersPanel _layersPanel = null!;

    private readonly TextBlock _statusHint = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _statusCursor = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 110 };
    private readonly TextBlock _statusSize = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 100 };
    private readonly TextBlock _statusZoom = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 48 };

    private Action<uint>? _pendingEyedropper;
    private Menu _menuBar = null!;

    public MainWindow()
    {
        Title = "Artista";
        Width = 1440;
        Height = 900;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "ForegroundBrush");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        ThemeManager.ApplyTitleBar(this);
        ThemeManager.ThemeChanged += (_, _) =>
        {
            ThemeManager.ApplyTitleBar(this);
            foreach (var site in _panelSites)
                if (site.Window != null)
                    ThemeManager.ApplyTitleBar(site.Window);
        };

        _tools = ToolRegistry.CreateTools();
        foreach (var tool in _tools)
            tool.Attach(this);

        Content = BuildLayout();
        _documentView.Canvas.ShowPixelGrid = App.Settings.ShowPixelGrid;
        _documentView.Canvas.ShowSpriteGrid = App.Settings.ShowSpriteGrid;
        _documentView.Canvas.SpriteGridCellWidth = Math.Max(1, App.Settings.SpriteGridCellWidth);
        _documentView.Canvas.SpriteGridCellHeight = Math.Max(1, App.Settings.SpriteGridCellHeight);
        _documentView.Canvas.SpriteGridOriginX = App.Settings.SpriteGridOriginX;
        _documentView.Canvas.SpriteGridOriginY = App.Settings.SpriteGridOriginY;
        _documentView.Canvas.SpriteGridSpacingX = Math.Max(0, App.Settings.SpriteGridSpacingX);
        _documentView.Canvas.SpriteGridSpacingY = Math.Max(0, App.Settings.SpriteGridSpacingY);
        BuildToolPalette();
        RegisterShortcuts();

        _documentView.CursorMoved += (_, p) => UpdateCursorStatus(p);
        _documentView.ZoomChanged += (_, _) => UpdateZoomStatus();

        AllowDrop = true;
        Drop += OnFileDrop;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        PreviewKeyDown += OnGlobalKeyDown;
        PreviewKeyUp += OnGlobalKeyUp;
        PreviewTextInput += OnGlobalTextInput;
        Closing += OnWindowClosing;
        Loaded += (_, _) => EnsureMainWindowOnScreen();

        ActivateTool(_tools[0]);
        NewDocumentFromDefaults();
    }

    private void EnsureMainWindowOnScreen()
    {
        if (WindowState != WindowState.Normal) return;
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        Left = Math.Clamp(double.IsNaN(Left) ? area.Left : Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(double.IsNaN(Top) ? area.Top : Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }

    // ---------------- layout ----------------

    private UIElement BuildLayout()
    {
        var root = new DockPanel();

        // Panel sites must exist before the View menu (its toggle items
        // reference them); the dock grid is added to the layout further down.
        BuildRightPanels();

        _menuBar = BuildMenu();
        DockPanel.SetDock(_menuBar, Dock.Top);
        root.Children.Add(_menuBar);

        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var settingsBar = BuildSettingsBar();
        DockPanel.SetDock(settingsBar, Dock.Top);
        root.Children.Add(settingsBar);

        var status = BuildStatusBar();
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        // Panels can snap around three sides of the canvas area.
        DockPanel.SetDock(_topDock, Dock.Top);
        root.Children.Add(_topDock);
        DockPanel.SetDock(_leftDock, Dock.Left);
        root.Children.Add(_leftDock);
        DockPanel.SetDock(_rightDock, Dock.Right);
        root.Children.Add(_rightDock);

        // Center: tab strip + document view.
        var center = new DockPanel();
        var tabBorder = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
        tabBorder.SetResourceReference(Border.BackgroundProperty, "ToolbarBackgroundBrush");
        tabBorder.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");
        _tabStrip.BorderThickness = new Thickness(0);
        _tabStrip.Background = Brushes.Transparent;
        var itemsPanelTemplate = new ItemsPanelTemplate(new System.Windows.FrameworkElementFactory(typeof(StackPanel)));
        ((System.Windows.FrameworkElementFactory)itemsPanelTemplate.VisualTree!).SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        _tabStrip.ItemsPanel = itemsPanelTemplate;
        _tabStrip.SelectionChanged += (_, _) =>
        {
            if (_tabStrip.SelectedItem is ListBoxItem { Tag: DocumentWorkspace ws } && ws != _active)
                ActivateWorkspace(ws);
        };
        tabBorder.Child = _tabStrip;
        DockPanel.SetDock(tabBorder, Dock.Top);
        center.Children.Add(tabBorder);
        center.Children.Add(_documentView);
        root.Children.Add(center);

        BuildDockGuideOverlay();
        var shell = new Grid();
        shell.Children.Add(root);
        shell.Children.Add(_dockGuideOverlay);
        return shell;
    }

    private void BuildDockGuideOverlay()
    {
        Border Guide(string label, HorizontalAlignment horizontal, VerticalAlignment vertical, Thickness margin)
        {
            var border = new Border
            {
                Width = vertical == VerticalAlignment.Top ? 250 : 110,
                Height = vertical == VerticalAlignment.Top ? 76 : 180,
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                Margin = margin,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(3),
                BorderBrush = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(220, 20, 115, 220)),
                Child = new TextBlock
                {
                    Text = $"DOCK\n{label}",
                    Foreground = Brushes.White,
                    FontSize = 17,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            return border;
        }

        _dockGuideOverlay.Children.Add(Guide("LEFT", HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(72, 0, 0, 0)));
        _dockGuideOverlay.Children.Add(Guide("TOP", HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, 76, 0, 0)));
        _dockGuideOverlay.Children.Add(Guide("RIGHT", HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0, 0, 18, 0)));
    }

    private void ShowDockGuides(bool visible)
    {
        _dockGuideOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _dockGuideOverlay.UpdateLayout();
        if (visible)
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private Border BuildSettingsBar()
    {
        var border = new Border
        {
            Padding = new Thickness(6, 3, 6, 3),
            BorderThickness = new Thickness(0, 0, 0, 1),
            MinHeight = 32,
        };
        border.SetResourceReference(Border.BackgroundProperty, "ToolbarBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var toolLabel = new TextBlock { Text = "Tool:", VerticalAlignment = VerticalAlignment.Center };
        toolLabel.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundDimBrush");
        panel.Children.Add(toolLabel);
        panel.Children.Add(_settingsToolName);
        panel.Children.Add(_settingsBar);
        border.Child = panel;
        return border;
    }

    // ---------------- dockable / floating panels ----------------

    private sealed class PanelSite
    {
        public string Name = "";
        public string Title = "";
        public FrameworkElement Content = null!;
        public FloatingPanelWindow? Window;
        public ScrollViewer? DockHost;
        public PanelState State = new();
        public MenuItem? MenuItem;
        public Rect DefaultFloatRect; // offsets relative to the main window
    }

    private readonly List<PanelSite> _panelSites = new();
    private readonly Grid _leftDock = new();
    private readonly Grid _rightDock = new();
    private readonly Grid _topDock = new();

    private Grid BuildRightPanels()
    {
        _colorsPanel = new ColorsPanel(this);
        _historyPanel = new HistoryPanel(this);
        _layersPanel = new LayersPanel(this);

        var toolsHost = new Border { Padding = new Thickness(4), Child = _toolPalette };
        toolsHost.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
        _toolPalette.VerticalAlignment = VerticalAlignment.Top;
        _toolPalette.HorizontalAlignment = HorizontalAlignment.Left;

        _panelSites.Add(new PanelSite
        {
            Name = "tools", Title = "Tools", Content = toolsHost,
            DefaultFloatRect = new Rect(18, 105, 112, 550),
        });

        _panelSites.Add(new PanelSite
        {
            Name = "colors", Title = "Colors", Content = _colorsPanel,
            DefaultFloatRect = new Rect(80, -540, 310, 470),   // negative Y = from bottom
        });
        _panelSites.Add(new PanelSite
        {
            Name = "history", Title = "History", Content = _historyPanel,
            DefaultFloatRect = new Rect(-340, 130, 300, 240),  // negative X = from right
        });
        _panelSites.Add(new PanelSite
        {
            Name = "layers", Title = "Layers", Content = _layersPanel,
            DefaultFloatRect = new Rect(-340, 400, 300, 330),
        });

        foreach (var site in _panelSites)
        {
            if (App.Settings.Panels.TryGetValue(site.Name, out var saved))
                site.State = saved;
            // Panels are FLOATING by default (Paint.NET style).
        }

        // Floating windows can only be created/positioned once the main window
        // is shown; the dock column can build immediately.
        RebuildDock();
        Loaded += (_, _) =>
        {
            foreach (var site in _panelSites.Where(s => !s.State.Docked && s.State.Visible))
                FloatSite(site, initial: true);
        };
        return _rightDock;
    }

    private void RebuildDock()
    {
        // Detach docked content first.
        foreach (var site in _panelSites)
        {
            if (site.DockHost != null)
            {
                site.DockHost.Content = null;
                site.DockHost = null;
            }
        }
        _rightDock.Children.Clear();
        _rightDock.RowDefinitions.Clear();
        _leftDock.Children.Clear();
        _leftDock.RowDefinitions.Clear();
        _topDock.Children.Clear();
        _topDock.ColumnDefinitions.Clear();

        BuildVerticalDock(_leftDock, PanelDockEdge.Left);
        BuildVerticalDock(_rightDock, PanelDockEdge.Right);
        BuildTopDock();
    }

    private void BuildVerticalDock(Grid grid, PanelDockEdge edge)
    {
        var docked = DockedAt(edge).ToList();
        grid.Width = docked.Count == 0 ? 0 : docked.All(s => s.Name == "tools") ? 104 : 280;
        int row = 0;
        foreach (var site in docked)
        {
            if (site.Name == "tools") _toolPalette.Columns = 2;
            grid.RowDefinitions.Add(site.Name == "colors"
                ? new RowDefinition { Height = GridLength.Auto }
                : new RowDefinition { Height = new GridLength(site.Name == "layers" ? 1.3 : 1, GridUnitType.Star) });
            var box = BuildDockBox(site);
            Grid.SetRow(box, row++);
            grid.Children.Add(box);
        }
    }

    private void BuildTopDock()
    {
        var docked = DockedAt(PanelDockEdge.Top).ToList();
        _topDock.Height = docked.Count == 0 ? 0 : 230;
        int column = 0;
        foreach (var site in docked)
        {
            if (site.Name == "tools") _toolPalette.Columns = 13;
            _topDock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
            var box = BuildDockBox(site);
            Grid.SetColumn(box, column++);
            _topDock.Children.Add(box);
        }
    }

    private IEnumerable<PanelSite> DockedAt(PanelDockEdge edge) =>
        _panelSites.Where(s => s.State.Docked && s.State.Visible &&
            string.Equals(s.State.DockSide, edge.ToString(), StringComparison.OrdinalIgnoreCase));

    private Border BuildDockBox(PanelSite site)
    {
        var outer = new Border { BorderThickness = new Thickness(1, 0, 0, 1) };
        outer.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
        outer.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");
        var dock = new DockPanel();
        var header = new Border { Padding = new Thickness(8, 3, 4, 3) };
        header.SetResourceReference(Border.BackgroundProperty, "PanelHeaderBrush");
        var headerRow = new DockPanel();
        var headerText = new TextBlock { Text = site.Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

        Button HeaderButton(string glyph, string tip)
        {
            return new Button
            {
                Content = glyph, FontSize = 11, Width = 22, Height = 20,
                Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0),
                BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
                ToolTip = tip,
            };
        }
        var closeButton = HeaderButton("✕", "Hide panel (reopen from the View menu)");
        closeButton.Click += (_, _) => SetPanelVisible(site, false);
        var floatButton = HeaderButton("⇱", "Float as a separate window");
        floatButton.Click += (_, _) => FloatSite(site);
        DockPanel.SetDock(closeButton, Dock.Right);
        DockPanel.SetDock(floatButton, Dock.Right);
        headerRow.Children.Add(closeButton);
        headerRow.Children.Add(floatButton);
        headerRow.Children.Add(headerText);
        header.Child = headerRow;
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        site.DockHost = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = site.Content,
        };
        dock.Children.Add(site.DockHost);
        outer.Child = dock;
        return outer;
    }

    private void FloatSite(PanelSite site, bool initial = false)
    {
        site.State.Docked = false;
        site.State.Visible = true;
        if (site.Name == "tools") _toolPalette.Columns = 2;
        // Detach from the dock.
        if (site.DockHost != null)
        {
            site.DockHost.Content = null;
            site.DockHost = null;
        }
        RebuildDock();

        if (site.Window == null)
        {
            site.Window = new FloatingPanelWindow(site.Title, this);
            if (site.Name == "tools")
            {
                site.Window.MinWidth = 96;
                site.Window.MinHeight = 240;
            }
            site.Window.DockRequested += edge => DockSite(site, edge);
            site.Window.DockDragStateChanged += ShowDockGuides;
            site.Window.HideRequested += (_, _) => SetPanelVisible(site, false);
            ApplyShortcuts(site.Window.InputBindings);
        }
        site.Window.PanelContent = site.Content;

        if (site.State.Width > 50)
        {
            site.Window.Left = site.State.X;
            site.Window.Top = site.State.Y;
            site.Window.Width = site.State.Width;
            site.Window.Height = site.State.Height;
        }
        else
        {
            var r = site.DefaultFloatRect;
            site.Window.Left = Left + (r.X >= 0 ? r.X : ActualWidth + r.X);
            site.Window.Top = Top + (r.Y >= 0 ? r.Y : ActualHeight + r.Y);
            site.Window.Width = r.Width;
            site.Window.Height = r.Height;
        }
        site.Window.Show();
        SyncPanelMenuChecks();
        if (!initial)
            SavePanelStates();
    }

    private void DockSite(PanelSite site, PanelDockEdge edge = PanelDockEdge.Right)
    {
        site.State.Docked = true;
        site.State.DockSide = edge.ToString();
        if (site.Window != null)
        {
            site.State.X = site.Window.Left;
            site.State.Y = site.Window.Top;
            site.State.Width = site.Window.Width;
            site.State.Height = site.Window.Height;
            site.Window.PanelContent = null;
            site.Window.Hide();
        }
        RebuildDock();
        SyncPanelMenuChecks();
        SavePanelStates();
    }

    private void SetPanelVisible(PanelSite site, bool visible)
    {
        site.State.Visible = visible;
        if (site.State.Docked)
        {
            RebuildDock();
        }
        else if (site.Window != null)
        {
            if (visible)
            {
                site.Window.PanelContent = site.Content;
                site.Window.Show();
            }
            else
            {
                site.Window.Hide();
            }
        }
        else if (visible)
        {
            FloatSite(site);
        }
        SyncPanelMenuChecks();
        SavePanelStates();
    }

    private void TogglePanel(string name)
    {
        var site = _panelSites.FirstOrDefault(s => s.Name == name);
        if (site != null)
            SetPanelVisible(site, !site.State.Visible);
    }

    private void SyncPanelMenuChecks()
    {
        foreach (var site in _panelSites)
        {
            if (site.MenuItem != null)
                site.MenuItem.IsChecked = site.State.Visible;
        }
    }

    private void SavePanelStates()
    {
        foreach (var site in _panelSites)
        {
            if (!site.State.Docked && site.Window is { IsVisible: true })
            {
                site.State.X = site.Window.Left;
                site.State.Y = site.Window.Top;
                site.State.Width = site.Window.Width;
                site.State.Height = site.Window.Height;
            }
            App.Settings.Panels[site.Name] = site.State;
        }
        App.Settings.Save();
    }

    private StatusBar BuildStatusBar()
    {
        var bar = new StatusBar { MinHeight = 24 };
        var hintItem = new StatusBarItem { Content = _statusHint };
        var zoomItem = new StatusBarItem { Content = _statusZoom, HorizontalAlignment = HorizontalAlignment.Right };
        var sizeItem = new StatusBarItem { Content = _statusSize, HorizontalAlignment = HorizontalAlignment.Right };
        var cursorItem = new StatusBarItem { Content = _statusCursor, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(zoomItem, Dock.Right);
        DockPanel.SetDock(sizeItem, Dock.Right);
        DockPanel.SetDock(cursorItem, Dock.Right);
        bar.Items.Add(zoomItem);
        bar.Items.Add(sizeItem);
        bar.Items.Add(cursorItem);
        bar.Items.Add(hintItem);
        return bar;
    }

    private static System.Windows.Shapes.Path IconPath(string key, double size = 16, double stroke = 1.4)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = (Geometry)Application.Current.FindResource(key),
            StrokeThickness = stroke,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetResourceReference(System.Windows.Shapes.Path.StrokeProperty, "IconBrush");
        return path;
    }

    // ---------------- tool palette & settings bar ----------------

    private void BuildToolPalette()
    {
        _toolPalette.Children.Clear();
        foreach (var tool in _tools)
        {
            var toggle = new ToggleButton
            {
                Content = ToolIconFactory.Create(tool),
                Tag = tool,
                Width = 42,
                Height = 38,
                Margin = new Thickness(1.5),
                Padding = new Thickness(6),
                ToolTip = tool.Name,
            };
            toggle.Click += (s, _) => ActivateTool((ToolBase)((FrameworkElement)s!).Tag);
            _toolPalette.Children.Add(toggle);
        }
    }

    private void ActivateTool(ToolBase tool)
    {
        _activeTool?.OnDeactivated();
        _activeTool = tool;
        _documentView.Canvas.ActiveTool = tool;
        _documentView.Canvas.Cursor = tool.Cursor;
        foreach (ToggleButton toggle in _toolPalette.Children)
            toggle.IsChecked = ReferenceEquals(toggle.Tag, tool);
        tool.OnActivated();
        BuildSettingsBarContent(tool);
        _settingsToolName.Text = tool.Name;
        SetStatus(tool.StatusHint);
        _documentView.Canvas.InvalidateVisual();
    }

    private void BuildSettingsBarContent(ToolBase tool)
    {
        _settingsBar.Children.Clear();
        foreach (var kind in tool.SettingsBar)
        {
            var control = BuildSettingControl(kind);
            if (control != null)
            {
                control.Margin = new Thickness(0, 0, 14, 0);
                _settingsBar.Children.Add(control);
            }
        }
    }

    private FrameworkElement? BuildSettingControl(ToolSettingKind kind) => kind switch
    {
        ToolSettingKind.CombineMode => SettingCombo("Mode:", new[] { "Replace", "Add (union)", "Subtract", "Intersect" },
            (int)_environment.CombineMode, i => _environment.CombineMode = (Core.Selections.SelectionCombineMode)i),
        ToolSettingKind.BrushWidth => SettingSlider("Size:", 1, 200, _environment.BrushWidth, v => _environment.BrushWidth = v, "px"),
        ToolSettingKind.Hardness => SettingSlider("Hardness:", 0, 100, _environment.Hardness * 100, v => _environment.Hardness = v / 100),
        ToolSettingKind.Opacity => SettingSlider("Opacity:", 0, 100, _environment.Opacity * 100, v => _environment.Opacity = v / 100),
        ToolSettingKind.Tolerance => SettingSlider("Tolerance:", 0, 100, _environment.Tolerance, v => _environment.Tolerance = v),
        ToolSettingKind.Softness => SettingSlider("Softness:", 0, 100, _environment.Softness, v => _environment.Softness = v),
        ToolSettingKind.Antialias => SettingCheck("Antialias", _environment.Antialias, v => _environment.Antialias = v),
        ToolSettingKind.FillStyle => SettingCombo("Fill:", new[] { "Outline", "Fill", "Fill + outline" },
            (int)_environment.FillStyle, i => _environment.FillStyle = (FillStyle)i),
        ToolSettingKind.CornerRadius => SettingSlider("Radius:", 0, 100, _environment.CornerRadius, v => _environment.CornerRadius = v, "px"),
        ToolSettingKind.WandGlobal => SettingCheck("Global (non-contiguous)", _environment.WandGlobal, v => _environment.WandGlobal = v),
        ToolSettingKind.GradientShape => SettingCombo("Shape:", new[] { "Linear", "Radial" },
            (int)_environment.GradientShape, i => _environment.GradientShape = (Core.Drawing.GradientShape)i),
        ToolSettingKind.GradientToTransparent => SettingCheck("To transparent", _environment.GradientToTransparent, v => _environment.GradientToTransparent = v),
        ToolSettingKind.Font => BuildFontSettings(),
        ToolSettingKind.SampleModes => BuildSampleModeSettings(),
        ToolSettingKind.Feather => SettingSlider("Feather:", 0, 50, _environment.Feather, v => _environment.Feather = v, "px"),
        _ => null,
    };

    private FrameworkElement SettingSlider(string label, double min, double max, double value, Action<double> onChange, string? unit = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 90, VerticalAlignment = VerticalAlignment.Center };
        var valueText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 34, Margin = new Thickness(4, 0, 0, 0), Text = $"{(int)value}{unit}" };
        slider.ValueChanged += (_, e) =>
        {
            onChange(e.NewValue);
            valueText.Text = $"{(int)e.NewValue}{unit}";
        };
        panel.Children.Add(text);
        panel.Children.Add(slider);
        panel.Children.Add(valueText);
        return panel;
    }

    private FrameworkElement SettingCombo(string label, string[] options, int selected, Action<int> onChange)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var combo = new ComboBox { MinWidth = 110 };
        foreach (var option in options)
            combo.Items.Add(option);
        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) => onChange(combo.SelectedIndex);
        panel.Children.Add(combo);
        return panel;
    }

    private FrameworkElement SettingCheck(string label, bool value, Action<bool> onChange)
    {
        var check = new CheckBox { Content = label, IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        check.Click += (_, _) => onChange(check.IsChecked == true);
        return check;
    }

    private FrameworkElement BuildFontSettings()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var fontCombo = new ComboBox { Width = 150 };
        foreach (var family in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
            fontCombo.Items.Add(family.Source);
        fontCombo.SelectedItem = _environment.FontFamily;
        if (fontCombo.SelectedItem == null) fontCombo.SelectedIndex = 0;
        fontCombo.SelectionChanged += (_, _) =>
        {
            if (fontCombo.SelectedItem is string s) _environment.FontFamily = s;
        };
        panel.Children.Add(fontCombo);

        var sizeBox = new TextBox { Width = 42, Margin = new Thickness(6, 0, 0, 0), Text = ((int)_environment.FontSize).ToString() };
        sizeBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && double.TryParse(sizeBox.Text, out double v))
                _environment.FontSize = v;
        };
        sizeBox.LostFocus += (_, _) =>
        {
            if (double.TryParse(sizeBox.Text, out double v)) _environment.FontSize = v;
        };
        panel.Children.Add(sizeBox);

        var bold = new ToggleButton { Content = new TextBlock { Text = "B", FontWeight = FontWeights.Bold }, Margin = new Thickness(6, 0, 0, 0), Width = 26, IsChecked = _environment.FontBold };
        bold.Click += (_, _) => _environment.FontBold = bold.IsChecked == true;
        var italic = new ToggleButton { Content = new TextBlock { Text = "I", FontStyle = FontStyles.Italic }, Margin = new Thickness(2, 0, 0, 0), Width = 26, IsChecked = _environment.FontItalic };
        italic.Click += (_, _) => _environment.FontItalic = italic.IsChecked == true;
        panel.Children.Add(bold);
        panel.Children.Add(italic);
        return panel;
    }

    private FrameworkElement BuildSampleModeSettings()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var combo = new ComboBox { MinWidth = 170 };
        combo.Items.Add("Sample target from first click");
        combo.Items.Add("Fixed target (secondary color)");
        combo.Items.Add("Sample continuously");
        combo.SelectedIndex = _environment.SampleContinuously ? 2 : _environment.SampleFromClick ? 0 : 1;
        combo.SelectionChanged += (_, _) =>
        {
            _environment.SampleFromClick = combo.SelectedIndex == 0;
            _environment.SampleContinuously = combo.SelectedIndex == 2;
        };
        var label = new TextBlock { Text = "Target:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        panel.Children.Add(label);
        panel.Children.Add(combo);
        return panel;
    }

    // ---------------- workspaces & tabs ----------------

    public DocumentWorkspace? ActiveWorkspace => _active;
    public ToolEnvironment Env => _environment;
    public AppSettings Settings => App.Settings;
    ToolEnvironment IToolContext.Environment => _environment;
    DocumentWorkspace? IToolContext.Workspace => _active;

    private void AddWorkspace(DocumentWorkspace ws)
    {
        _workspaces.Add(ws);
        ws.History.Changed += (_, _) =>
        {
            if (ws == _active)
            {
                RefreshAllPanels();
                InvalidateDocument(ws.Document.Bounds);
            }
        };
        ws.DirtyChanged += (_, _) => RefreshTabs();
        RefreshTabs();
        ActivateWorkspace(ws);
    }

    private void ActivateWorkspace(DocumentWorkspace ws)
    {
        _activeTool?.OnCommit();
        _active = ws;
        _documentView.SetWorkspace(ws);
        RefreshTabs();
        RefreshAllPanels();
        UpdateTitle();
        UpdateZoomStatus();
        _statusSize.Text = $"{ws.Document.Width} × {ws.Document.Height}";
    }

    private void RefreshTabs()
    {
        _tabStrip.Items.Clear();
        foreach (var ws in _workspaces)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = ws.DisplayName, VerticalAlignment = VerticalAlignment.Center });
            var close = new Button
            {
                Content = "✕",
                FontSize = 10,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(3, 0, 3, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Tag = ws,
            };
            close.Click += (s, e) =>
            {
                e.Handled = true;
                CloseWorkspace((DocumentWorkspace)((FrameworkElement)s!).Tag);
            };
            panel.Children.Add(close);
            var item = new ListBoxItem { Content = panel, Tag = ws, Padding = new Thickness(10, 5, 6, 5) };
            _tabStrip.Items.Add(item);
            if (ws == _active)
                _tabStrip.SelectedItem = item;
        }
        UpdateTitle();
    }

    private void UpdateTitle() =>
        Title = _active == null ? "Artista" : $"{_active.DisplayName.TrimEnd('*', ' ')}{(_active.IsDirty ? " *" : "")} - Artista";

    // ---------------- status ----------------

    private void UpdateCursorStatus(Point docPoint)
    {
        if (double.IsNaN(docPoint.X) || _active == null)
        {
            _statusCursor.Text = "";
            return;
        }
        _statusCursor.Text = $"{(int)Math.Floor(docPoint.X)}, {(int)Math.Floor(docPoint.Y)} px";
    }

    private void UpdateZoomStatus() => _statusZoom.Text = $"{_documentView.Zoom * 100:0}%";

    public void SetStatus(string text) => _statusHint.Text = text;

    // ---------------- IToolContext ----------------

    public void InvalidateDocument(RectInt rect)
    {
        _active?.InvalidateComposite(rect);
        _documentView.Canvas.InvalidateVisual();
    }

    public void InvalidateOverlay() => _documentView.Canvas.InvalidateVisual();

    public void PushHistory(HistoryMemento memento, string? iconKey = null)
    {
        _active?.History.Push(memento, iconKey);
        _active?.MarkDirty();
        RefreshAllPanels();
    }

    public void SetCursorHint(Cursor cursor) => _documentView.Canvas.Cursor = cursor;

    public double ZoomFactor => _documentView.Zoom;

    public bool IsSpriteGridActive =>
        _active != null &&
        _documentView.Canvas.ShowSpriteGrid &&
        SpriteGridLayout.IsValid &&
        SpriteGridLayout.Columns(_active.Document.Width) > 0 &&
        SpriteGridLayout.Rows(_active.Document.Height) > 0;

    public int SpriteGridCellWidth => _documentView.Canvas.SpriteGridCellWidth;
    public int SpriteGridCellHeight => _documentView.Canvas.SpriteGridCellHeight;
    public SpriteGridLayout SpriteGridLayout => _documentView.Canvas.SpriteGridLayout;

    public void NotifySelectionChanged() => _documentView.Canvas.InvalidateVisual();

    public void NotifyLayersChanged() => RefreshAllPanels();

    public void CommitActiveTool() => _activeTool?.OnCommit();

    public void ActivateLayer(int layerId)
    {
        if (_active == null) return;
        int index = _active.Document.IndexOfLayer(layerId);
        if (index < 0) return;
        CommitActiveTool();
        _active.Document.ActiveLayerIndex = index;
        SetStatus($"Active layer: {_active.Document.ActiveLayer.Name}");
    }

    public void ViewZoomInAt(Point docPoint) =>
        _documentView.SetZoom(_documentView.Zoom * 1.5, _documentView.Canvas.DocToView(docPoint));

    public void ViewZoomOutAt(Point docPoint) =>
        _documentView.SetZoom(_documentView.Zoom / 1.5, _documentView.Canvas.DocToView(docPoint));

    public void ViewZoomToRect(RectInt docRect)
    {
        if (docRect.IsEmpty) return;
        double zx = _documentView.Canvas.ActualWidth / docRect.Width;
        double zy = _documentView.Canvas.ActualHeight / docRect.Height;
        double zoom = Math.Min(zx, zy);
        _documentView.SetZoom(zoom);
        var centerDoc = new Point(docRect.Left + docRect.Width / 2.0, docRect.Top + docRect.Height / 2.0);
        _documentView.Canvas.OffsetX = _documentView.Canvas.ActualWidth / 2 - centerDoc.X * _documentView.Zoom;
        _documentView.Canvas.OffsetY = _documentView.Canvas.ActualHeight / 2 - centerDoc.Y * _documentView.Zoom;
        _documentView.Canvas.InvalidateVisual();
    }

    public void ViewPanBy(double viewDx, double viewDy)
    {
        _documentView.PanBy(viewDx, viewDy);
    }

    // ---------------- panels refresh ----------------

    public void RefreshAllPanels()
    {
        _layersPanel.Refresh();
        _historyPanel.Refresh();
        if (_active != null)
            _statusSize.Text = $"{_active.Document.Width} × {_active.Document.Height}";
        UpdateTitle();
    }

    // ---------------- input plumbing ----------------

    private void OnGlobalKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !(e.OriginalSource is TextBox))
        {
            _documentView.SpaceDown = true;
            _documentView.Canvas.Cursor = Cursors.Hand;
        }

        // Give the active tool first refusal unless a text box has focus.
        if (e.OriginalSource is TextBox) return;
        // Text is edited directly on the canvas rather than in a TextBox, so
        // it also suppresses global shortcuts such as F1 while typing.
        if (_activeTool is TextTool { IsEditing: true } editingText)
        {
            if (e.Key == Key.F1)
            {
                e.Handled = true;
                return;
            }
            if (editingText.OnKeyDown(e.Key, Keyboard.Modifiers))
                e.Handled = true;
            return;
        }
        if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            SetSpriteGridVisible(!_documentView.Canvas.ShowSpriteGrid);
            e.Handled = true;
            return;
        }
        if (_activeTool != null)
        {
            if (e.Key == Key.Escape)
            {
                CancelActiveOperationOrDeselect();
                InvalidateOverlay();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && _activeTool is not TextTool)
            {
                CommitFromEnter();
                InvalidateOverlay();
                e.Handled = true;
                return;
            }
            if (_activeTool.OnKeyDown(e.Key, Keyboard.Modifiers))
            {
                e.Handled = true;
                return;
            }
        }

        // Plain-key tool shortcuts and zoom keys.
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.Add or Key.OemPlus: _documentView.ZoomIn(); UpdateZoomStatus(); e.Handled = true; break;
                case Key.Subtract or Key.OemMinus: _documentView.ZoomOut(); UpdateZoomStatus(); e.Handled = true; break;
                case Key.X: _environment.SwapColors(); e.Handled = true; break;
                case Key.B: ActivateToolByType<PaintbrushTool>(); e.Handled = true; break;
                case Key.E: ActivateToolByType<EraserTool>(); e.Handled = true; break;
                case Key.P: ActivateToolByType<PencilTool>(); e.Handled = true; break;
                case Key.S: ActivateToolByType<RectangleSelectTool>(); e.Handled = true; break;
                case Key.W: ActivateToolByType<MagicWandTool>(); e.Handled = true; break;
                case Key.M: ActivateToolByType<MoveSelectedPixelsTool>(); e.Handled = true; break;
                case Key.K: ActivateToolByType<ColorPickerTool>(); e.Handled = true; break;
                case Key.T: ActivateToolByType<TextTool>(); e.Handled = true; break;
                case Key.F: ActivateToolByType<PaintBucketTool>(); e.Handled = true; break;
                case Key.G: ActivateToolByType<GradientTool>(); e.Handled = true; break;
                case Key.H: ActivateToolByType<PanTool>(); e.Handled = true; break;
                case Key.Z: ActivateToolByType<ZoomTool>(); e.Handled = true; break;
                case Key.Q: ActivateToolByType<ColorRemoverTool>(); e.Handled = true; break;
                case Key.Delete:
                    if (_layersPanel.IsLayerListFocused)
                        _layersPanel.DeleteSelectedLayer();
                    else
                        DeleteSelectionPixels();
                    e.Handled = true;
                    break;
            }
        }
    }

    private void CommitFromEnter()
    {
        if (_activeTool is MoveSelectedPixelsTool movePixels)
        {
            movePixels.CommitAndDeselect();
            return;
        }

        _activeTool?.OnCommit();
        if (_activeTool is SelectionToolBase or LassoSelectTool or MagicWandTool or MoveSelectionTool)
            Deselect();
    }

    private void CancelActiveOperationOrDeselect()
    {
        if (_activeTool?.IsBusy == true)
            _activeTool.OnCancel();
        else if (_active != null && !_active.Document.Selection.IsEmpty)
            Deselect();
    }

    private void ActivateToolByType<T>() where T : ToolBase
    {
        var tool = _tools.OfType<T>().FirstOrDefault();
        if (tool != null) ActivateTool(tool);
    }

    private void OnGlobalKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _documentView.SpaceDown = false;
            _documentView.Canvas.Cursor = _activeTool?.Cursor ?? Cursors.Arrow;
        }
    }

    private void OnGlobalTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (_activeTool is TextTool { IsEditing: true } textTool)
        {
            textTool.OnTextInput(e.Text);
            e.Handled = true;
        }
    }

    // Eyedropper support for effect dialogs.
    public void BeginEyedropper(Action<uint> onPicked)
    {
        _pendingEyedropper = onPicked;
        _documentView.Canvas.Cursor = Cursors.Cross;
        _documentView.Canvas.PreviewMouseDown += EyedropperClick;
        SetStatus("Click on the image to sample the target color.");
    }

    private void EyedropperClick(object sender, MouseButtonEventArgs e)
    {
        _documentView.Canvas.PreviewMouseDown -= EyedropperClick;
        var doc = _documentView.Canvas.ViewToDoc(e.GetPosition(_documentView.Canvas));
        int x = (int)doc.X, y = (int)doc.Y;
        if (_active != null && _active.Document.Bounds.Contains(x, y))
            _pendingEyedropper?.Invoke(_active.CompositeSurface[x, y]);
        _pendingEyedropper = null;
        _documentView.Canvas.Cursor = _activeTool?.Cursor ?? Cursors.Arrow;
        e.Handled = true;
    }
}
