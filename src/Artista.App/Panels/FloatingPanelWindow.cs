using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Artista.App.Panels;

/// <summary>
/// A Paint.NET-style floating tool window: slim themed header (drag to move,
/// dock and close buttons), resizable border, always above the main window,
/// never in the taskbar. Content is reparented in and out by the panel manager.
/// </summary>
public sealed class FloatingPanelWindow : Window
{
    private readonly ScrollViewer _contentHost = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    /// <summary>Raised when the user drops the window near the dock edge or clicks the dock button.</summary>
    public event EventHandler? DockRequested;

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
        var dockButton = HeaderButton("⇥", "Dock into the side panel (drag near the window edge also docks)");
        dockButton.Click += (_, _) => DockRequested?.Invoke(this, EventArgs.Empty);
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
                DockRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            try { DragMove(); } catch { /* only valid while button down */ }
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
        double ownerRight = Owner.Left + Owner.ActualWidth;
        double verticalOverlap = Math.Min(Top + ActualHeight, Owner.Top + Owner.ActualHeight) -
                                 Math.Max(Top, Owner.Top);
        if (Math.Abs(Left + ActualWidth - ownerRight) < 45 && verticalOverlap >= 32)
        {
            DockRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
