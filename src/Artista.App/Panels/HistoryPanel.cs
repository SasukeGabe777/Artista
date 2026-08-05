using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Artista.App.Panels;

/// <summary>
/// History panel: the list of performed actions with the current position;
/// clicking an entry jumps backward/forward through history like Paint.NET.
/// </summary>
public sealed class HistoryPanel : DockPanel
{
    private readonly IShellHost _host;
    private readonly ListBox _list = new();
    private bool _refreshing;

    public HistoryPanel(IShellHost host)
    {
        _host = host;
        _list.SelectionChanged += (_, _) =>
        {
            if (_refreshing || _host.ActiveWorkspace == null || _list.SelectedIndex < 0) return;
            // Item 0 = initial state → JumpTo(0); item N = after N actions.
            int targetIndex = _list.SelectedIndex;
            _host.CommitActiveTool();
            _host.ActiveWorkspace.History.JumpTo(targetIndex);
            _host.InvalidateDocument(_host.ActiveWorkspace.Document.Bounds);
            _host.RefreshAllPanels();
        };
        Children.Add(_list);
    }

    public void Refresh()
    {
        _refreshing = true;
        try
        {
            _list.Items.Clear();
            var ws = _host.ActiveWorkspace;
            if (ws == null) return;

            _list.Items.Add(MakeRow("Initial state", null, dimmed: false));
            foreach (var entry in ws.History.UndoEntries)
                _list.Items.Add(MakeRow(entry.Name, entry.IconKey, dimmed: false));
            foreach (var entry in ws.History.RedoEntries.Reverse())
                _list.Items.Add(MakeRow(entry.Name, entry.IconKey, dimmed: true));

            _refreshing = true;
            _list.SelectedIndex = ws.History.UndoEntries.Count;
            if (_list.SelectedItem != null)
                _list.ScrollIntoView(_list.SelectedItem);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static FrameworkElement MakeRow(string text, string? iconKey, bool dimmed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var iconHolder = new Border { Width = 18, Height = 14 };
        if (iconKey != null && Application.Current.TryFindResource(iconKey) is Geometry geometry)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = geometry, StrokeThickness = 1.2, Stretch = Stretch.Uniform,
                Width = 13, Height = 13,
            };
            path.SetResourceReference(System.Windows.Shapes.Path.StrokeProperty, "IconBrush");
            iconHolder.Child = path;
        }
        row.Children.Add(iconHolder);
        var label = new TextBlock { Text = text, Margin = new Thickness(4, 0, 0, 0) };
        if (dimmed)
            label.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundDisabledBrush");
        row.Children.Add(label);
        return row;
    }
}
