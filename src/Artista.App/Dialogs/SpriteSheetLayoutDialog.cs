using System.Windows;
using System.Windows.Controls;

namespace Artista.App.Dialogs;

/// <summary>
/// Asks how a single selected or parked sprite sheet should be divided when
/// its frame layout cannot be inferred unambiguously.
/// </summary>
public sealed class SpriteSheetLayoutDialog : DialogBase
{
    private readonly int _sheetWidth;
    private readonly int _sheetHeight;
    private readonly TextBox _columns;
    private readonly TextBox _rows;
    private readonly TextBlock _summary = new()
    {
        Margin = new Thickness(0, 8, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        Width = 310,
    };

    public int Columns { get; private set; }
    public int Rows { get; private set; }

    public SpriteSheetLayoutDialog(int sheetWidth, int sheetHeight, string sourceName)
        : base("Sprite Sheet Layout")
    {
        _sheetWidth = sheetWidth;
        _sheetHeight = sheetHeight;
        var suggested = SuggestLayout(sheetWidth, sheetHeight);
        _columns = NumberBox(suggested.Columns);
        _rows = NumberBox(suggested.Rows);

        Body.Children.Add(new TextBlock
        {
            Text = $"Artista found one sprite sheet ({sourceName}, {sheetWidth} x {sheetHeight} px). " +
                   "Choose how it should be divided into animation frames.",
            TextWrapping = TextWrapping.Wrap,
            Width = 310,
            Margin = new Thickness(0, 0, 0, 8),
        });
        Body.Children.Add(LabeledRow("Frames across:", _columns));
        Body.Children.Add(LabeledRow("Frames down:", _rows));
        Body.Children.Add(_summary);

        _columns.TextChanged += (_, _) => UpdateSummary();
        _rows.TextChanged += (_, _) => UpdateSummary();
        UpdateSummary();
        Loaded += (_, _) =>
        {
            _columns.Focus();
            _columns.SelectAll();
        };
    }

    protected override void TryAccept()
    {
        if (!TryParsePositive(_columns, out int columns, 128) ||
            !TryParsePositive(_rows, out int rows, 128))
            return;

        int count = columns * rows;
        if (count < 2 || count > 128)
        {
            MessageBox.Show(this, "Choose a layout containing between 2 and 128 frames.",
                "Invalid sprite layout", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_sheetWidth % columns != 0 || _sheetHeight % rows != 0)
        {
            MessageBox.Show(this,
                $"{_sheetWidth} x {_sheetHeight} pixels cannot be divided evenly into {columns} columns and {rows} rows.",
                "Invalid sprite layout", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Columns = columns;
        Rows = rows;
        DialogResult = true;
    }

    private void UpdateSummary()
    {
        if (!int.TryParse(_columns.Text, out int columns) || columns < 1 ||
            !int.TryParse(_rows.Text, out int rows) || rows < 1)
        {
            _summary.Text = "Enter whole-number columns and rows.";
            return;
        }

        if (_sheetWidth % columns == 0 && _sheetHeight % rows == 0)
        {
            int frameWidth = _sheetWidth / columns;
            int frameHeight = _sheetHeight / rows;
            _summary.Text = $"{columns * rows} frames, each {frameWidth} x {frameHeight} px. " +
                            "Frames are read left-to-right, then top-to-bottom.";
        }
        else
        {
            _summary.Text = "That grid does not divide the selected pixels evenly.";
        }
    }

    private static (int Columns, int Rows) SuggestLayout(int width, int height)
    {
        if (width >= height)
        {
            int desired = Math.Clamp((int)Math.Round((double)width / Math.Max(1, height)), 2, 128);
            return (NearestDivisor(width, desired), 1);
        }

        int desiredRows = Math.Clamp((int)Math.Round((double)height / Math.Max(1, width)), 2, 128);
        return (1, NearestDivisor(height, desiredRows));
    }

    private static int NearestDivisor(int value, int desired)
    {
        int best = desired;
        int bestDistance = int.MaxValue;
        for (int candidate = 2; candidate <= Math.Min(128, value); candidate++)
        {
            if (value % candidate != 0) continue;
            int distance = Math.Abs(candidate - desired);
            if (distance >= bestDistance) continue;
            best = candidate;
            bestDistance = distance;
        }
        return best;
    }
}
