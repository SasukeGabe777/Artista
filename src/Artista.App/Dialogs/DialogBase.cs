using System.Windows;
using System.Windows.Controls;

namespace Artista.App.Dialogs;

/// <summary>Base for all app dialogs: themed background, dark title bar, OK/Cancel row.</summary>
public abstract class DialogBase : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(14) };
    protected readonly Button OkButton = new() { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
    protected readonly Button CancelButton = new() { Content = "Cancel", IsCancel = true, MinWidth = 80 };

    protected DialogBase(string title)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "ForegroundBrush");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 12;
        UseLayoutRounding = true;
        ThemeManager.ApplyTitleBar(this);

        var root = new DockPanel();
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(14, 0, 14, 14),
        };
        buttonRow.Children.Add(OkButton);
        buttonRow.Children.Add(CancelButton);
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);
        root.Children.Add(Body);
        Content = root;

        OkButton.Click += (_, _) => TryAccept();
    }

    /// <summary>Validate input; set DialogResult = true when acceptable.</summary>
    protected virtual void TryAccept() => DialogResult = true;

    protected static DockPanel LabeledRow(string label, FrameworkElement control, double labelWidth = 110)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var text = new TextBlock { Text = label, Width = labelWidth, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(text);
        row.Children.Add(control);
        return row;
    }

    protected static TextBox NumberBox(int value, double width = 80) => new()
    {
        Text = value.ToString(),
        Width = width,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    protected static bool TryParsePositive(TextBox box, out int value, int max = 32768)
    {
        if (int.TryParse(box.Text.Trim(), out value) && value > 0 && value <= max)
            return true;
        MessageBox.Show($"Enter a whole number between 1 and {max}.", "Invalid value",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus();
        box.SelectAll();
        return false;
    }
}
