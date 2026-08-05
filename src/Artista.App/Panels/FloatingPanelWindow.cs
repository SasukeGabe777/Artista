using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Artista.App.Panels;

public enum PanelDockEdge
{
    Left,
    Right,
    Top,
}

/// <summary>
/// A Paint.NET-style floating tool window: slim themed header (drag to move,
/// dock and close buttons), resizable border, always above the main window,
/// never in the taskbar. Content is reparented in and out by the panel manager.
/// </summary>
public sealed class FloatingPanelWindow : Window
{
    private readonly ScrollViewer _contentHost = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    /// <summary>Raised when the user drops the window near the dock edge or clicks the dock button.</summary>
    public event Action<PanelDockEdge>? DockRequested;

    /// <summary>Shows or hides the owner's visual docking targets during a window drag.</summary>
    public event Action<bool>? DockDragStateChanged;

    /// <summary>Raised when the user closes (hides) the panel.</summary>
    public event EventHandler? HideRequested;

    public FloatingPanelWindow(string title, Window owner)
    {
        Title = title;
        Owner = owner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        MinWidth = 180;
        MinHeight = 120;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        SetResourceReference(ForegroundProperty, "ForegroundBrush");
        SetResourceReference(BackgroundProperty, "PanelBackgroundBrush");
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(5),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        var root = new Border { BorderThickness = new Thickness(1) };
        root.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var dock = new DockPanel();

        var header = new Border { Padding = new Thickness(8, 4, 4, 4) };
        header.SetResourceReference(Border.BackgroundProperty, "PanelHeaderBrush");
        var headerRow = new DockPanel();
        var titleText = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

        var closeButton = HeaderButton("✕", "Hide panel (reopen from the View menu)");
        closeButton.Click += (_, _) => HideRequested?.Invoke(this, EventArgs.Empty);
        var dockButton = HeaderButton("⇥", "Dock left, right, or above the canvas (drag toward a highlighted target)");
        dockButton.Click += (_, _) => DockRequested?.Invoke(NearestDockEdge());
        DockPanel.SetDock(closeButton, Dock.Right);
        DockPanel.SetDock(dockButton, Dock.Right);
        headerRow.Children.Add(closeButton);
        headerRow.Children.Add(dockButton);
        headerRow.Children.Add(titleText);
        header.Child = headerRow;
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                DockRequested?.Invoke(NearestDockEdge());
                return;
            }
            DockDragStateChanged?.Invoke(true);
            try { DragMove(); } catch { /* only valid while button down */ }
            finally { DockDragStateChanged?.Invoke(false); }
            CheckDockProximity();
        };

        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(_contentHost);
        root.Child = dock;
        Content = root;
        ThemeManager.ApplyTitleBar(this);
    }

    private static Button HeaderButton(string glyph, string tip)
    {
        var button = new Button
        {
            Content = glyph,
            FontSize = 11,
            Width = 22,
            Height = 20,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            ToolTip = tip,
        };
        return button;
    }

    public FrameworkElement? PanelContent
    {
        get => _contentHost.Content as FrameworkElement;
        set => _contentHost.Content = value;
    }

    private void CheckDockProximity()
    {
        if (Owner == null) return;
        double ownerLeft = Owner.Left;
        double ownerRight = ownerLeft + Owner.ActualWidth;
        double ownerTop = Owner.Top;
        double verticalOverlap = Math.Min(Top + ActualHeight, Owner.Top + Owner.ActualHeight) -
                                 Math.Max(Top, Owner.Top);
        double horizontalOverlap = Math.Min(Left + ActualWidth, ownerRight) - Math.Max(Left, ownerLeft);
        var candidates = new List<(double Distance, PanelDockEdge Edge)>();
        if (verticalOverlap >= 32)
        {
            candidates.Add((Math.Abs(Left - ownerLeft), PanelDockEdge.Left));
            candidates.Add((Math.Abs(Left + ActualWidth - ownerRight), PanelDockEdge.Right));
        }
        if (horizontalOverlap >= 32)
            candidates.Add((Math.Abs(Top - ownerTop), PanelDockEdge.Top));
        if (candidates.Count == 0) return;
        var nearest = candidates.OrderBy(c => c.Distance).FirstOrDefault();
        if (nearest.Distance < 70)
            DockRequested?.Invoke(nearest.Edge);
    }

    private PanelDockEdge NearestDockEdge()
    {
        if (Owner == null) return PanelDockEdge.Right;
        var distances = new[]
        {
            (Math.Abs(Left - Owner.Left), PanelDockEdge.Left),
            (Math.Abs(Left + ActualWidth - (Owner.Left + Owner.ActualWidth)), PanelDockEdge.Right),
            (Math.Abs(Top - Owner.Top), PanelDockEdge.Top),
        };
        return distances.OrderBy(x => x.Item1).First().Item2;
    }
}
