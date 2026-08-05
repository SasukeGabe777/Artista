using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Artista.Core.History;
using Artista.Core.Imaging;
using Artista.Core.IO;

namespace Artista.App.Tools;

/// <summary>
/// Text tool: click to place an insertion point, type to edit, Enter inserts a
/// newline, Escape or clicking elsewhere commits the text to the layer as one
/// history entry. Ctrl+Escape cancels without committing.
/// </summary>
public sealed class TextTool : ToolBase
{
    public override string Name => "Text";
    public override string IconKey => "Icon.Text";
    public override ToolSettingKind[] SettingsBar => new[] { ToolSettingKind.Font, ToolSettingKind.Antialias };
    public override string StatusHint =>
        "Click to place text, then type. Escape or clicking elsewhere commits; Ctrl+Escape cancels.";
    public override Cursor Cursor => Cursors.IBeam;

    private bool _editing;
    private Point _position;
    private string _text = "";
    private bool _caretVisible = true;
    private System.Windows.Threading.DispatcherTimer? _caretTimer;

    public bool IsEditing => _editing;

    public override bool IsBusy => _editing;

    public override void OnActivated()
    {
        _caretTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _caretTimer.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            if (_editing) Context.InvalidateOverlay();
        };
        _caretTimer.Start();
    }

    public override void OnDeactivated()
    {
        Commit();
        _caretTimer?.Stop();
        _caretTimer = null;
    }

    public override void OnPointerDown(ToolPointerEventArgs e)
    {
        if (_editing)
        {
            Commit();
        }
        var ws = Context.Workspace;
        if (ws == null) return;
        var layer = ws.Document.ActiveLayer;
        if (layer.Locked || !layer.Visible)
        {
            Context.SetStatus("The active layer is locked or hidden.");
            return;
        }
        _editing = true;
        _text = "";
        _position = new Point(e.X, e.Y);
        Context.SetStatus("Type your text. Escape commits, Ctrl+Escape cancels.");
        Context.InvalidateOverlay();
    }

    public void OnTextInput(string text)
    {
        if (!_editing) return;
        foreach (char c in text)
        {
            if (c == '\b') continue;
            if (!char.IsControl(c)) _text += c;
        }
        Context.InvalidateOverlay();
    }

    public override bool OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (!_editing) return false;
        switch (key)
        {
            case Key.Back:
                if (_text.Length > 0) _text = _text[..^1];
                Context.InvalidateOverlay();
                return true;
            case Key.Enter:
                _text += "\n";
                Context.InvalidateOverlay();
                return true;
            case Key.Escape:
                if ((modifiers & ModifierKeys.Control) != 0) Cancel();
                else Commit();
                return true;
        }
        return false;
    }

    public override void OnCommit() => Commit();
    public override void OnCancel() => Cancel();

    private FormattedText BuildFormattedText(double pixelsPerDip = 1.25)
    {
        var env = Context.Environment;
        var typeface = new Typeface(
            new FontFamily(env.FontFamily),
            env.FontItalic ? FontStyles.Italic : FontStyles.Normal,
            env.FontBold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        var color = env.PrimaryColor;
        var brush = new SolidColorBrush(Color.FromArgb(
            ColorBgra.A(color), ColorBgra.R(color), ColorBgra.G(color), ColorBgra.B(color)));
        return new FormattedText(
            _text.Length == 0 ? " " : _text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, env.FontSize, brush, pixelsPerDip);
    }

    private void Commit()
    {
        if (!_editing) return;
        _editing = false;
        var ws = Context.Workspace;
        if (ws == null || _text.Trim().Length == 0)
        {
            _text = "";
            Context.InvalidateOverlay();
            return;
        }
        var layer = ws.Document.ActiveLayer;

        // Rasterize at 1:1 device-independent scale (96 dpi = document pixels).
        var formatted = BuildFormattedText(1.0);
        int w = (int)Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 4;
        int h = (int)Math.Ceiling(formatted.Height) + 4;
        if (w <= 0 || h <= 0) { _text = ""; return; }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (!Context.Environment.Antialias)
                TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Aliased);
            dc.DrawText(formatted, new Point(2, 2));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var textSurface = ImageCodec.FromBitmapSource(rtb);

        int destX = (int)Math.Round(_position.X) - 2;
        int destY = (int)Math.Round(_position.Y) - 2;
        var dirty = new RectInt(destX, destY, w, h).Intersect(ws.Document.Bounds);
        if (!dirty.IsEmpty)
        {
            var before = layer.Surface.ExtractRect(dirty);

            // Respect the selection mask while blitting.
            var selection = ws.Document.Selection;
            bool hasSelection = !selection.IsEmpty;
            for (int y = dirty.Top; y < dirty.Bottom; y++)
            {
                var dstRow = layer.Surface.GetRowSpan(y, dirty.Left, dirty.Width);
                for (int x = 0; x < dirty.Width; x++)
                {
                    uint src = textSurface[dirty.Left + x - destX, y - destY];
                    if (ColorBgra.A(src) == 0) continue;
                    int scale = 255;
                    if (hasSelection)
                    {
                        scale = selection.MaskAt(dirty.Left + x, y);
                        if (scale == 0) continue;
                    }
                    dstRow[x] = ColorBgra.Over(dstRow[x], src, scale);
                }
            }
            Context.PushHistory(new SurfaceRegionMemento("Text", layer, dirty, before), IconKey);
            ws.MarkDirty();
            Context.InvalidateDocument(dirty);
        }
        _text = "";
        Context.InvalidateOverlay();
    }

    private void Cancel()
    {
        _editing = false;
        _text = "";
        Context.SetStatus("Text cancelled.");
        Context.InvalidateOverlay();
    }

    public override void OnRenderOverlay(DrawingContext dc, CanvasTransform t)
    {
        if (!_editing) return;
        var origin = t.DocToView(_position.X, _position.Y);
        dc.PushTransform(new MatrixTransform(t.Zoom, 0, 0, t.Zoom, origin.X, origin.Y));
        var formatted = BuildFormattedText(1.0);
        dc.DrawText(formatted, new Point(0, 0));

        if (_caretVisible)
        {
            // Caret at the end of the last line.
            string[] lines = _text.Split('\n');
            string lastLine = lines[^1];
            var lineText = new FormattedText(lastLine.Length == 0 ? "" : lastLine,
                CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(Context.Environment.FontFamily), Context.Environment.FontSize, Brushes.Black, 1.0);
            double cx = lineText.WidthIncludingTrailingWhitespace;
            double cy = (lines.Length - 1) * formatted.Height / Math.Max(1, lines.Length);
            double lineH = Context.Environment.FontSize * 1.3;
            var caretBrush = TryGetAccent();
            dc.DrawRectangle(caretBrush, null, new Rect(cx, cy, Math.Max(1.0, 1.5 / t.Zoom), lineH));
        }
        dc.Pop();
    }

    private Brush TryGetAccent() =>
        Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
}
