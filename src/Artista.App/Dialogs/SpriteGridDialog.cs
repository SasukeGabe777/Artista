using System.Windows;
using System.Windows.Controls;

namespace Artista.App.Dialogs;

/// <summary>Configures the frame-sized canvas guide used by Rectangle Select.</summary>
public sealed class SpriteGridDialog : DialogBase
{
    private readonly int _documentWidth;
    private readonly int _documentHeight;
    private readonly TextBox _cellWidth;
    private readonly TextBox _cellHeight;
    private readonly CheckBox _squareCells = new()
    {
        Content = "Square cells (keep width and height equal)",
        Margin = new Thickness(0, 6, 0, 2),
    };
    private readonly Action<int, int>? _previewChanged;
    private bool _syncing;
    private readonly TextBlock _summary = new()
    {
        Width = 330,
        Margin = new Thickness(0, 8, 0, 0),
        TextWrapping = TextWrapping.Wrap,
    };

    public int CellWidth { get; private set; }
    public int CellHeight { get; private set; }

    public SpriteGridDialog(int documentWidth, int documentHeight, int cellWidth, int cellHeight,
        Action<int, int>? previewChanged = null)
        : base("Sprite Grid")
    {
        _documentWidth = documentWidth;
        _documentHeight = documentHeight;
        _previewChanged = previewChanged;
        _cellWidth = NumberBox(Math.Clamp(cellWidth, 1, Math.Max(1, documentWidth)));
        _cellHeight = NumberBox(Math.Clamp(cellHeight, 1, Math.Max(1, documentHeight)));
        _squareCells.IsChecked = _cellWidth.Text == _cellHeight.Text;

        Body.Children.Add(new TextBlock
        {
            Text = "Set the exact size of one animation frame. The red guide starts at the canvas's top-left corner, and Rectangle Select snaps to complete frame cells.",
            Width = 330,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        Body.Children.Add(LabeledRow("Frame width (px):", _cellWidth, 125));
        Body.Children.Add(LabeledRow("Frame height (px):", _cellHeight, 125));
        Body.Children.Add(_squareCells);
        Body.Children.Add(_summary);

        _cellWidth.TextChanged += (_, _) => HandleSizeChanged(widthChanged: true);
        _cellHeight.TextChanged += (_, _) => HandleSizeChanged(widthChanged: false);
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
        if (!TryParsePositive(_cellWidth, out int width, _documentWidth) ||
            !TryParsePositive(_cellHeight, out int height, _documentHeight))
            return;
        CellWidth = width;
        CellHeight = height;
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
        if (!int.TryParse(_cellWidth.Text, out int width) || width < 1 || width > _documentWidth ||
            !int.TryParse(_cellHeight.Text, out int height) || height < 1 || height > _documentHeight)
        {
            _summary.Text = $"Enter a size no larger than the {_documentWidth} x {_documentHeight} px canvas.";
            return;
        }

        int columns = _documentWidth / width;
        int rows = _documentHeight / height;
        int remainderX = _documentWidth % width;
        int remainderY = _documentHeight % height;
        _summary.Text = $"{columns} x {rows} complete frame cells ({columns * rows} total).";
        if (remainderX != 0 || remainderY != 0)
            _summary.Text += $" The unused right/bottom edge is {remainderX} x {remainderY} px; only complete cells will snap and preview.";
        _previewChanged?.Invoke(width, height);
    }
}
