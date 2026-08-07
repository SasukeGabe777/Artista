using System.Windows;
using System.Windows.Controls;
using Artista.Core.Selections;

namespace Artista.App.Dialogs;

/// <summary>Configures frame size, origin, and spacing with a live canvas preview.</summary>
public sealed class SpriteGridDialog : DialogBase
{
    private readonly int _documentWidth;
    private readonly int _documentHeight;
    private readonly TextBox _cellWidth;
    private readonly TextBox _cellHeight;
    private readonly TextBox _originX;
    private readonly TextBox _originY;
    private readonly TextBox _spacingX;
    private readonly TextBox _spacingY;
    private readonly CheckBox _squareCells = new()
    {
        Content = "Square cells (keep width and height equal)",
        Margin = new Thickness(0, 6, 0, 2),
    };
    private readonly TextBlock _summary = new()
    {
        Width = 350,
        Margin = new Thickness(0, 8, 0, 0),
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Action<SpriteGridLayout>? _previewChanged;
    private bool _syncing;

    public SpriteGridLayout Layout { get; private set; }
    public int CellWidth => Layout.CellWidth;
    public int CellHeight => Layout.CellHeight;

    public SpriteGridDialog(
        int documentWidth,
        int documentHeight,
        SpriteGridLayout layout,
        Action<SpriteGridLayout>? previewChanged = null)
        : base("Sprite Grid")
    {
        _documentWidth = documentWidth;
        _documentHeight = documentHeight;
        _previewChanged = previewChanged;
        layout = layout.IsValid ? layout : new SpriteGridLayout(32, 32);
        _cellWidth = NumberBox(Math.Clamp(layout.CellWidth, 1, Math.Max(1, documentWidth)));
        _cellHeight = NumberBox(Math.Clamp(layout.CellHeight, 1, Math.Max(1, documentHeight)));
        _originX = NumberBox(layout.OriginX);
        _originY = NumberBox(layout.OriginY);
        _spacingX = NumberBox(Math.Max(0, layout.SpacingX));
        _spacingY = NumberBox(Math.Max(0, layout.SpacingY));
        _squareCells.IsChecked = _cellWidth.Text == _cellHeight.Text;

        Body.Children.Add(new TextBlock
        {
            Text = "Set one frame's dimensions, the grid origin, and transparent spacing between cells. Changes preview live; Rectangle Select snaps only to cell interiors.",
            Width = 350,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        Body.Children.Add(LabeledRow("Frame width (px):", _cellWidth, 145));
        Body.Children.Add(LabeledRow("Frame height (px):", _cellHeight, 145));
        Body.Children.Add(_squareCells);
        Body.Children.Add(new Separator { Margin = new Thickness(0, 7, 0, 7) });
        Body.Children.Add(LabeledRow("Grid origin X (px):", _originX, 145));
        Body.Children.Add(LabeledRow("Grid origin Y (px):", _originY, 145));
        Body.Children.Add(LabeledRow("Horizontal spacing:", _spacingX, 145));
        Body.Children.Add(LabeledRow("Vertical spacing:", _spacingY, 145));
        Body.Children.Add(_summary);

        _cellWidth.TextChanged += (_, _) => HandleSizeChanged(widthChanged: true);
        _cellHeight.TextChanged += (_, _) => HandleSizeChanged(widthChanged: false);
        _originX.TextChanged += (_, _) => UpdateSummaryAndPreview();
        _originY.TextChanged += (_, _) => UpdateSummaryAndPreview();
        _spacingX.TextChanged += (_, _) => UpdateSummaryAndPreview();
        _spacingY.TextChanged += (_, _) => UpdateSummaryAndPreview();
        _squareCells.Checked += (_, _) => MakeSquare();
        _squareCells.Unchecked += (_, _) => UpdateSummaryAndPreview();
        UpdateSummaryAndPreview();
        Loaded += (_, _) =>
        {
            _cellWidth.Focus();
            _cellWidth.SelectAll();
        };
    }

    protected override void TryAccept()
    {
        if (!TryReadLayout(showMessage: true, out var layout)) return;
        Layout = layout;
        DialogResult = true;
    }

    private void HandleSizeChanged(bool widthChanged)
    {
        if (_syncing) return;
        if (_squareCells.IsChecked == true)
        {
            var source = widthChanged ? _cellWidth : _cellHeight;
            var target = widthChanged ? _cellHeight : _cellWidth;
            if (int.TryParse(source.Text, out int size) && size >= 1 &&
                size <= Math.Min(_documentWidth, _documentHeight))
            {
                _syncing = true;
                target.Text = source.Text;
                _syncing = false;
            }
        }
        UpdateSummaryAndPreview();
    }

    private void MakeSquare()
    {
        if (_syncing) return;
        int limit = Math.Min(_documentWidth, _documentHeight);
        int size = int.TryParse(_cellWidth.Text, out int width)
            ? Math.Clamp(width, 1, limit)
            : 1;
        _syncing = true;
        _cellWidth.Text = size.ToString();
        _cellHeight.Text = size.ToString();
        _syncing = false;
        UpdateSummaryAndPreview();
    }

    private void UpdateSummaryAndPreview()
    {
        if (!TryReadLayout(showMessage: false, out var layout))
        {
            _summary.Text = "Enter valid whole-pixel cell sizes, offsets, and non-negative spacing.";
            return;
        }

        int columns = layout.Columns(_documentWidth);
        int rows = layout.Rows(_documentHeight);
        _summary.Text = $"{columns} x {rows} complete cells ({columns * rows} total). " +
                        $"Pitch: {layout.PitchX} x {layout.PitchY} px; origin: {layout.OriginX}, {layout.OriginY}.";
        _previewChanged?.Invoke(layout);
    }

    private bool TryReadLayout(bool showMessage, out SpriteGridLayout layout)
    {
        layout = default;
        int width = 0, height = 0, originX = 0, originY = 0, spacingX = 0, spacingY = 0;
        bool valid =
            int.TryParse(_cellWidth.Text.Trim(), out width) && width is > 0 and <= 32768 &&
            int.TryParse(_cellHeight.Text.Trim(), out height) && height is > 0 and <= 32768 &&
            int.TryParse(_originX.Text.Trim(), out originX) && Math.Abs((long)originX) <= 32768 &&
            int.TryParse(_originY.Text.Trim(), out originY) && Math.Abs((long)originY) <= 32768 &&
            int.TryParse(_spacingX.Text.Trim(), out spacingX) && spacingX is >= 0 and <= 32768 &&
            int.TryParse(_spacingY.Text.Trim(), out spacingY) && spacingY is >= 0 and <= 32768;
        if (valid)
        {
            layout = new SpriteGridLayout(width, height, originX, originY, spacingX, spacingY);
            valid = layout.PitchX <= 32768 && layout.PitchY <= 32768 &&
                    layout.Columns(_documentWidth) > 0 && layout.Rows(_documentHeight) > 0;
        }
        if (!valid && showMessage)
            MessageBox.Show(this,
                "Use whole-pixel values. Cell sizes must be positive, spacing cannot be negative, and at least one complete cell must fit on the canvas.",
                "Invalid Sprite Grid", MessageBoxButton.OK, MessageBoxImage.Warning);
        return valid;
    }
}
