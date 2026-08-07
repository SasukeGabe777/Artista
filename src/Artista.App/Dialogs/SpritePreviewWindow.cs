using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Artista.Core.Imaging;
using Artista.Core.IO;
using Microsoft.Win32;

namespace Artista.App.Dialogs;

internal sealed record SpriteFrameData(Surface Surface, int DurationMs = 100, string Name = "Frame");

/// <summary>
/// A lightweight Aseprite-style animation canvas: one composited surface per
/// frame, a horizontal frame strip, playback controls, per-frame timing,
/// forward/ping-pong looping, and animated GIF import/export.
/// </summary>
internal sealed class SpritePreviewWindow : Window
{
    private readonly List<SpriteFrameData> _frames;
    private readonly SpriteCanvasView _canvas = new();
    private readonly StackPanel _timeline = new() { Orientation = Orientation.Horizontal };
    private readonly List<ToggleButton> _frameButtons = new();
    private readonly Button _playButton = new() { Content = "Play", MinWidth = 58 };
    private readonly TextBlock _frameLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 108 };
    private readonly TextBlock _speedLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 72 };
    private readonly Slider _speed = new() { Minimum = 0.25, Maximum = 4, Value = 1, Width = 105, TickFrequency = 0.25, IsSnapToTickEnabled = true };
    private readonly Slider _zoom = new() { Minimum = 1, Maximum = 16, Value = 4, Width = 90, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBox _duration = new() { Width = 52, TextAlignment = TextAlignment.Right };
    private readonly CheckBox _loop = new() { Content = "Loop", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _pingPong = new() { Content = "Ping-pong", VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
    private int _currentIndex;
    private int _direction = 1;
    private bool _updatingControls;

    internal int FrameCount => _frames.Count;
    internal int CurrentFrameIndex => _currentIndex;
    internal bool IsPlaying => _timer.IsEnabled;

    internal SpritePreviewWindow(IEnumerable<SpriteFrameData> frames, string title = "Sprite Canvas")
    {
        _frames = frames.ToList();
        if (_frames.Count == 0)
            throw new ArgumentException("A Sprite Canvas needs at least one frame.", nameof(frames));

        Title = $"{title} - {_frames.Count} Frames";
        Width = 820;
        Height = 620;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        SetResourceReference(ForegroundProperty, "ForegroundBrush");
        ThemeManager.ApplyTitleBar(this);

        Content = BuildLayout();
        _timer.Tick += (_, _) => AdvanceFrame();
        _playButton.Click += (_, _) => TogglePlayback();
        _speed.ValueChanged += (_, _) =>
        {
            _speedLabel.Text = $"Speed {_speed.Value:0.##}x";
            UpdateTimerInterval();
        };
        _zoom.ValueChanged += (_, _) => _canvas.Zoom = (int)_zoom.Value;
        _duration.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) ApplyDuration();
        };
        _duration.LostFocus += (_, _) => ApplyDuration();
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += (_, _) => _timer.Stop();

        RebuildTimeline();
        SetFrame(0);
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel();

        var toolbarBorder = new Border { Padding = new Thickness(8, 6, 8, 6), BorderThickness = new Thickness(0, 0, 0, 1) };
        toolbarBorder.SetResourceReference(Border.BackgroundProperty, "ToolbarBackgroundBrush");
        toolbarBorder.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");
        DockPanel.SetDock(toolbarBorder, Dock.Top);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(Button("|<", "First frame", (_, _) => SetFrame(0)));
        toolbar.Children.Add(Button("<", "Previous frame", (_, _) => StepFrame(-1)));
        toolbar.Children.Add(_playButton);
        toolbar.Children.Add(Button(">", "Next frame", (_, _) => StepFrame(1)));
        toolbar.Children.Add(Button(">|", "Last frame", (_, _) => SetFrame(_frames.Count - 1)));
        toolbar.Children.Add(Spacer(8));
        toolbar.Children.Add(_frameLabel);
        toolbar.Children.Add(new TextBlock { Text = "Frame ms:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 4, 0) });
        toolbar.Children.Add(_duration);
        toolbar.Children.Add(Spacer(10));
        toolbar.Children.Add(_speedLabel);
        toolbar.Children.Add(_speed);
        toolbar.Children.Add(Spacer(10));
        toolbar.Children.Add(_loop);
        toolbar.Children.Add(Spacer(6));
        toolbar.Children.Add(_pingPong);
        toolbarBorder.Child = toolbar;
        root.Children.Add(toolbarBorder);

        var actionsBorder = new Border { Padding = new Thickness(8, 5, 8, 5), BorderThickness = new Thickness(0, 0, 0, 1) };
        actionsBorder.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
        actionsBorder.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");
        DockPanel.SetDock(actionsBorder, Dock.Top);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Button("Open GIF...", "Load all frames from an animated GIF", (_, _) => OpenGif()));
        actions.Children.Add(Button("Export GIF...", "Export the Sprite Canvas as an animated GIF", (_, _) => ExportGif()));
        actions.Children.Add(Spacer(12));
        actions.Children.Add(Button("Move Earlier", "Move the active frame earlier", (_, _) => MoveCurrent(-1)));
        actions.Children.Add(Button("Move Later", "Move the active frame later", (_, _) => MoveCurrent(1)));
        actions.Children.Add(Button("Remove", "Remove the active frame", (_, _) => RemoveCurrent()));
        actions.Children.Add(Spacer(12));
        actions.Children.Add(new TextBlock { Text = "Zoom:", VerticalAlignment = VerticalAlignment.Center });
        actions.Children.Add(_zoom);
        actionsBorder.Child = actions;
        root.Children.Add(actionsBorder);

        var timelineBorder = new Border { Padding = new Thickness(8, 7, 8, 7), BorderThickness = new Thickness(0, 1, 0, 0) };
        timelineBorder.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
        timelineBorder.SetResourceReference(Border.BorderBrushProperty, "BorderLightBrush");
        DockPanel.SetDock(timelineBorder, Dock.Bottom);
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _timeline,
        };
        timelineBorder.Child = scroll;
        root.Children.Add(timelineBorder);

        root.Children.Add(_canvas);
        return root;
    }

    private static FrameworkElement Spacer(double width) => new Border { Width = width };

    private static Button Button(string content, string tooltip, RoutedEventHandler click)
    {
        var button = new Button { Content = content, ToolTip = tooltip, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2, 0, 2, 0) };
        button.Click += click;
        return button;
    }

    private void RebuildTimeline()
    {
        _timeline.Children.Clear();
        _frameButtons.Clear();
        for (int i = 0; i < _frames.Count; i++)
        {
            int index = i;
            var image = new Image
            {
                Source = ToFrozenBitmap(_frames[i].Surface),
                Width = 52,
                Height = 52,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            var content = new StackPanel();
            content.Children.Add(image);
            content.Children.Add(new TextBlock { Text = (i + 1).ToString(), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
            var button = new ToggleButton
            {
                Content = content,
                ToolTip = $"Frame {i + 1} - {_frames[i].DurationMs} ms",
                Padding = new Thickness(5),
                Margin = new Thickness(2),
                MinWidth = 66,
            };
            button.Click += (_, _) => SetFrame(index);
            _frameButtons.Add(button);
            _timeline.Children.Add(button);
        }
        _canvas.SetFrames(_frames);
    }

    private void SetFrame(int index)
    {
        if (_frames.Count == 0) return;
        _currentIndex = Math.Clamp(index, 0, _frames.Count - 1);
        _canvas.CurrentFrameIndex = _currentIndex;
        for (int i = 0; i < _frameButtons.Count; i++)
            _frameButtons[i].IsChecked = i == _currentIndex;
        _updatingControls = true;
        _duration.Text = _frames[_currentIndex].DurationMs.ToString();
        _updatingControls = false;
        _frameLabel.Text = $"Frame {_currentIndex + 1}/{_frames.Count}";
        UpdateTimerInterval();
    }

    private void StepFrame(int amount)
    {
        if (_frames.Count == 0) return;
        int next = _currentIndex + amount;
        if (_loop.IsChecked == true)
            next = (next % _frames.Count + _frames.Count) % _frames.Count;
        SetFrame(Math.Clamp(next, 0, _frames.Count - 1));
    }

    private void TogglePlayback()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            _playButton.Content = "Play";
            return;
        }
        _direction = 1;
        UpdateTimerInterval();
        _timer.Start();
        _playButton.Content = "Pause";
    }

    private void AdvanceFrame()
    {
        if (_frames.Count <= 1) return;
        int next = _currentIndex + _direction;
        if (_pingPong.IsChecked == true)
        {
            if (next >= _frames.Count || next < 0)
            {
                if (_loop.IsChecked != true)
                {
                    _timer.Stop();
                    _playButton.Content = "Play";
                    return;
                }
                _direction *= -1;
                next = _currentIndex + _direction;
            }
        }
        else if (next >= _frames.Count)
        {
            if (_loop.IsChecked == true) next = 0;
            else
            {
                SetFrame(_frames.Count - 1);
                _timer.Stop();
                _playButton.Content = "Play";
                return;
            }
        }
        SetFrame(next);
    }

    internal void AdvanceFrameForTest() => AdvanceFrame();

    private void UpdateTimerInterval()
    {
        if (_frames.Count == 0) return;
        _speedLabel.Text = $"Speed {_speed.Value:0.##}x";
        _timer.Interval = TimeSpan.FromMilliseconds(
            Math.Clamp(_frames[_currentIndex].DurationMs / _speed.Value, 10, 10_000));
    }

    private void ApplyDuration()
    {
        if (_updatingControls || _frames.Count == 0) return;
        if (int.TryParse(_duration.Text, out int ms))
        {
            ms = Math.Clamp(ms, 10, 10_000);
            _frames[_currentIndex] = _frames[_currentIndex] with { DurationMs = ms };
            _duration.Text = ms.ToString();
            _frameButtons[_currentIndex].ToolTip = $"Frame {_currentIndex + 1} - {ms} ms";
            UpdateTimerInterval();
        }
        else
        {
            _duration.Text = _frames[_currentIndex].DurationMs.ToString();
        }
    }

    private void MoveCurrent(int delta)
    {
        int target = _currentIndex + delta;
        if (target < 0 || target >= _frames.Count) return;
        (_frames[_currentIndex], _frames[target]) = (_frames[target], _frames[_currentIndex]);
        _currentIndex = target;
        RebuildTimeline();
        SetFrame(target);
    }

    private void RemoveCurrent()
    {
        if (_frames.Count <= 1) return;
        _frames.RemoveAt(_currentIndex);
        _currentIndex = Math.Min(_currentIndex, _frames.Count - 1);
        RebuildTimeline();
        SetFrame(_currentIndex);
        Title = $"Sprite Canvas - {_frames.Count} Frames";
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        switch (e.Key)
        {
            case Key.Space: TogglePlayback(); e.Handled = true; break;
            case Key.Left: StepFrame(-1); e.Handled = true; break;
            case Key.Right: StepFrame(1); e.Handled = true; break;
            case Key.Home: SetFrame(0); e.Handled = true; break;
            case Key.End: SetFrame(_frames.Count - 1); e.Handled = true; break;
        }
    }

    private void OpenGif()
    {
        var dialog = new OpenFileDialog { Filter = "Animated GIF (*.gif)|*.gif", Title = "Open GIF in Sprite Canvas" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            LoadGif(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the GIF:\n\n{ex.Message}", "Open GIF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal void LoadGif(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("The GIF contains no frames.");
        _frames.Clear();
        for (int i = 0; i < decoder.Frames.Count; i++)
        {
            int delay = ReadGifDelay(decoder.Frames[i]);
            _frames.Add(new SpriteFrameData(ImageCodec.FromBitmapSource(decoder.Frames[i]), delay, $"GIF frame {i + 1}"));
        }
        _currentIndex = 0;
        RebuildTimeline();
        SetFrame(0);
        Title = $"Sprite Canvas - {_frames.Count} GIF Frames";
    }

    private static int ReadGifDelay(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/grctlext/Delay") is { } value)
                return Math.Clamp(Convert.ToInt32(value) * 10, 10, 10_000);
        }
        catch { }
        return 100;
    }

    private void ExportGif()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Animated GIF (*.gif)|*.gif",
            DefaultExt = ".gif",
            AddExtension = true,
            FileName = "sprite-animation.gif",
            Title = "Export Sprite Canvas",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            SaveGif(dialog.FileName);
            MessageBox.Show(this, $"Exported {_frames.Count} frames.", "Export GIF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not export the GIF:\n\n{ex.Message}", "Export GIF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal void SaveGif(string path)
    {
        int width = _frames.Max(f => f.Surface.Width);
        int height = _frames.Max(f => f.Surface.Height);
        var encoder = new GifBitmapEncoder();
        for (int i = 0; i < _frames.Count; i++)
        {
            var normalized = new Surface(width, height);
            int x = (width - _frames[i].Surface.Width) / 2;
            int y = (height - _frames[i].Surface.Height) / 2;
            normalized.DrawSurfaceOver(_frames[i].Surface, x, y);
            var source = ToFrozenBitmap(normalized);
            encoder.Frames.Add(CreateGifFrame(source, _frames[i].DurationMs, i == 0));
        }
        SafeSave.Write(path, encoder.Save);
    }

    private static BitmapFrame CreateGifFrame(BitmapSource source, int durationMs, bool first)
    {
        var metadata = new BitmapMetadata("gif");
        try { metadata.SetQuery("/grctlext/Delay", (ushort)Math.Clamp((durationMs + 5) / 10, 1, ushort.MaxValue)); } catch { }
        try { metadata.SetQuery("/grctlext/Disposal", (byte)2); } catch { }
        if (first)
        {
            try { metadata.SetQuery("/appext/Application", new byte[] { 0x4E, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2E, 0x30 }); } catch { }
            try { metadata.SetQuery("/appext/Data", new byte[] { 0x03, 0x01, 0x00, 0x00 }); } catch { }
        }
        return BitmapFrame.Create(source, null, metadata, null);
    }

    private static BitmapSource ToFrozenBitmap(Surface surface)
    {
        var bitmap = ImageCodec.ToBitmapSource(surface);
        bitmap.Freeze();
        return bitmap;
    }
}

internal sealed class SpriteCanvasView : FrameworkElement
{
    private IReadOnlyList<SpriteFrameData> _frames = Array.Empty<SpriteFrameData>();
    private readonly List<BitmapSource> _bitmaps = new();
    private int _currentFrameIndex;
    private int _zoom = 4;

    public int CurrentFrameIndex
    {
        get => _currentFrameIndex;
        set { _currentFrameIndex = value; InvalidateVisual(); }
    }

    public int Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, 1, 16); InvalidateVisual(); }
    }

    public SpriteCanvasView()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Focusable = true;
    }

    public void SetFrames(IReadOnlyList<SpriteFrameData> frames)
    {
        _frames = frames;
        _bitmaps.Clear();
        foreach (var frame in frames)
        {
            var bitmap = ImageCodec.ToBitmapSource(frame.Surface);
            bitmap.Freeze();
            _bitmaps.Add(bitmap);
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var surround = TryFindResource("CanvasSurroundBrush") as Brush ?? Brushes.DimGray;
        dc.DrawRectangle(surround, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_frames.Count == 0 || _currentFrameIndex >= _frames.Count) return;

        int canvasWidth = _frames.Max(f => f.Surface.Width);
        int canvasHeight = _frames.Max(f => f.Surface.Height);
        double width = canvasWidth * Zoom, height = canvasHeight * Zoom;
        var artboard = new Rect(
            Math.Round((ActualWidth - width) / 2),
            Math.Round((ActualHeight - height) / 2), width, height);
        dc.DrawRectangle(GetCheckerBrush(), new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), 1), artboard);

        var frame = _frames[_currentFrameIndex];
        double x = artboard.X + (canvasWidth - frame.Surface.Width) * Zoom / 2.0;
        double y = artboard.Y + (canvasHeight - frame.Surface.Height) * Zoom / 2.0;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        dc.DrawImage(_bitmaps[_currentFrameIndex], new Rect(x, y, frame.Surface.Width * Zoom, frame.Surface.Height * Zoom));
    }

    private Brush GetCheckerBrush()
    {
        Color light = (Color)(TryFindResource("CheckerLightColor") ?? Colors.White);
        Color dark = (Color)(TryFindResource("CheckerDarkColor") ?? Colors.LightGray);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(light), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(dark), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(dark), null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
        };
    }
}
