using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Artista.Core.Imaging;
using Artista.Core.IO;

namespace Artista.App.Dialogs;

public enum PasteSizeChoice
{
    ExpandCanvas,
    KeepCanvasSize,
    Cancel,
}

public enum FileDropChoice
{
    Open,
    AddAsLayers,
    Cancel,
}

/// <summary>Paint.NET-style oversized-paste decision window.</summary>
public sealed class PasteSizeDialog : Window
{
    public PasteSizeChoice Choice { get; private set; } = PasteSizeChoice.Cancel;

    public PasteSizeDialog(Surface image)
    {
        Title = "Paste";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "ForegroundBrush");
        ThemeManager.ApplyTitleBar(this);

        var root = new StackPanel { Margin = new Thickness(16) };
        var intro = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        intro.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        intro.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var preview = new Border
        {
            Width = 180,
            Height = 140,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new Image { Source = ImageCodec.ToBitmapSource(image), Stretch = Stretch.Uniform },
        };
        preview.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        intro.Children.Add(preview);

        var prompt = new TextBlock
        {
            Text = "The image being pasted is larger than the canvas size.\nWhat do you want to do?",
            FontSize = 20,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, 4, 0, 0),
        };
        Grid.SetColumn(prompt, 1);
        intro.Children.Add(prompt);
        root.Children.Add(intro);

        root.Children.Add(ChoiceButton("▧", "Expand canvas",
            "Automatically expands the canvas to fit the image being pasted.", true,
            () => Accept(PasteSizeChoice.ExpandCanvas)));
        root.Children.Add(ChoiceButton("⌗", "Keep canvas size",
            "Keeps the current canvas. Move the pasted image to choose which part stays within its boundaries.", false,
            () => Accept(PasteSizeChoice.KeepCanvasSize)));
        root.Children.Add(ChoiceButton("↶", "Cancel", "Cancels the paste action.", false,
            () => Accept(PasteSizeChoice.Cancel)));
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Choice = PasteSizeChoice.Cancel;
            DialogResult = false;
        };
    }

    private void Accept(PasteSizeChoice choice)
    {
        Choice = choice;
        DialogResult = choice != PasteSizeChoice.Cancel;
    }

    private static Button ChoiceButton(string glyph, string title, string description, bool isDefault, Action click)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 21, Foreground = Brushes.DodgerBlue });
        text.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        });
        var content = new DockPanel();
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = 27,
            Foreground = Brushes.SteelBlue,
            Width = 44,
            VerticalAlignment = VerticalAlignment.Top,
            TextAlignment = TextAlignment.Center,
        };
        DockPanel.SetDock(icon, Dock.Left);
        content.Children.Add(icon);
        content.Children.Add(text);
        var button = new Button
        {
            Content = content,
            IsDefault = isDefault,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 3, 0, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(isDefault ? 1 : 0),
        };
        button.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
        button.Click += (_, _) => click();
        return button;
    }
}

/// <summary>Paint.NET-style choice shown when image files are dropped on the workspace.</summary>
public sealed class FileDropDialog : Window
{
    public FileDropChoice Choice { get; private set; } = FileDropChoice.Cancel;

    public FileDropDialog(int fileCount)
    {
        Title = "Drag and Drop";
        Width = 510;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "ForegroundBrush");
        ThemeManager.ApplyTitleBar(this);

        string noun = fileCount == 1 ? "the file" : $"the {fileCount} files";
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = $"What would you like to do with {noun}?",
            FontSize = 19,
            Margin = new Thickness(0, 0, 0, 12),
        });
        root.Children.Add(ChoiceButton("▰", "Open", fileCount == 1 ? "Opens the image." : "Opens each image as a document.", true,
            () => Accept(FileDropChoice.Open)));
        root.Children.Add(ChoiceButton("⊞", "Add layer",
            fileCount == 1 ? "Loads the image as a new layer in the current image." : "Loads each image as a new layer in the current image.", false,
            () => Accept(FileDropChoice.AddAsLayers)));
        root.Children.Add(ChoiceButton("↶", "Cancel", "Cancels the drag-and-drop action.", false,
            () => Accept(FileDropChoice.Cancel)));
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Choice = FileDropChoice.Cancel;
            DialogResult = false;
        };
    }

    private void Accept(FileDropChoice choice)
    {
        Choice = choice;
        DialogResult = choice != FileDropChoice.Cancel;
    }

    private static Button ChoiceButton(string glyph, string title, string description, bool isDefault, Action click)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 21, Foreground = Brushes.DodgerBlue });
        text.Children.Add(new TextBlock { Text = description, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        var content = new DockPanel();
        var icon = new TextBlock { Text = glyph, FontSize = 27, Foreground = Brushes.SteelBlue, Width = 44, TextAlignment = TextAlignment.Center };
        DockPanel.SetDock(icon, Dock.Left);
        content.Children.Add(icon);
        content.Children.Add(text);
        var button = new Button
        {
            Content = content,
            IsDefault = isDefault,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 3, 0, 3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(isDefault ? 1 : 0),
        };
        button.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
        button.Click += (_, _) => click();
        return button;
    }
}
