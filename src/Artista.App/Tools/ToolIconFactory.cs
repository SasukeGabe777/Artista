using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Artista.App.Tools;

/// <summary>
/// Builds the colored 24px tool artwork used by the palette. The drawings are
/// original vectors, but intentionally use the familiar visual vocabulary of
/// desktop paint editors: blue selections, a gold hand and wand, colored
/// paint media, and clearly differentiated shape tools.
/// </summary>
internal static class ToolIconFactory
{
    private static readonly Brush Blue = Brush("#4F9DD9");
    private static readonly Brush BlueDark = Brush("#2569A8");
    private static readonly Brush BluePale = Brush("#D8EEFA");
    private static readonly Brush Gold = Brush("#E8A63C");
    private static readonly Brush GoldPale = Brush("#FFD786");
    private static readonly Brush Purple = Brush("#B45AD5");
    private static readonly Brush PurpleDark = Brush("#76349A");
    private static readonly Brush Orange = Brush("#EE8D45");
    private static readonly Brush Green = Brush("#65B78A");
    private static readonly Brush Red = Brush("#DB675D");
    private static readonly Brush Gray = Brush("#87939E");
    private static readonly Brush GrayDark = Brush("#53606B");
    private static readonly Brush White = Brush("#FFFDF8");

    public static FrameworkElement Create(ToolBase tool)
    {
        var c = new Canvas { Width = 24, Height = 24, SnapsToDevicePixels = true };
        switch (tool)
        {
            case RectangleSelectTool: RectangleSelect(c); break;
            case MoveSelectedPixelsTool: MovePixels(c); break;
            case LassoSelectTool: Lasso(c); break;
            case MoveSelectionTool: MoveSelection(c); break;
            case EllipseSelectTool: EllipseSelect(c); break;
            case ZoomTool: Zoom(c); break;
            case MagicWandTool: MagicWand(c); break;
            case PanTool: Pan(c); break;
            case PaintBucketTool: Bucket(c); break;
            case GradientTool: Gradient(c); break;
            case PaintbrushTool: Paintbrush(c); break;
            case EraserTool: Eraser(c); break;
            case PencilTool: Pencil(c); break;
            case ColorPickerTool: Picker(c); break;
            case CloneStampTool: CloneStamp(c); break;
            case RecolorTool: Recolor(c); break;
            case ColorRemoverTool: ColorRemover(c); break;
            case TextTool: Text(c); break;
            case LineCurveTool: Line(c); break;
            case CurveTool: Curve(c); break;
            case RectangleShapeTool: RectangleShape(c); break;
            case RoundedRectangleTool: RoundedRectangle(c); break;
            case EllipseShapeTool: EllipseShape(c); break;
            case FreeformShapeTool: Freeform(c); break;
            case SpritePreviewTool: SpritePreview(c); break;
            default: ResourceGlyph(c, tool.IconKey); break;
        }
        return new Viewbox { Width = 26, Height = 26, Stretch = Stretch.Uniform, Child = c };
    }

    private static void RectangleSelect(Canvas c)
    {
        AddRect(c, 3, 4, 17, 15, BluePale, BlueDark, 1.25, dash: true);
        AddPath(c, "M3,7 L3,4 L6,4 M17,4 L20,4 L20,7 M20,16 L20,19 L17,19 M6,19 L3,19 L3,16", null, BlueDark, 1.7);
    }

    private static void MovePixels(Canvas c)
    {
        AddPath(c, "M3,2 L3,17 L7.2,13.2 L10.2,20 L13,18.7 L10,12.2 L16,12 Z", Blue, BlueDark, 1.1);
        AddPath(c, "M17,10 L17,21 M12,15.5 L22,15.5 M17,10 L15.2,12 M17,10 L18.8,12 M17,21 L15.2,19 M17,21 L18.8,19 M12,15.5 L14,13.8 M12,15.5 L14,17.2 M22,15.5 L20,13.8 M22,15.5 L20,17.2", null, null, 1.25);
    }

    private static void Lasso(Canvas c)
    {
        AddPath(c, "M12,4 C18,4 21,7 20,11 C19,15 14,17 9,16 C5,15 3,13 4,10 C5,7 8,5 12,4", BluePale, Gray, 1.5);
        AddPath(c, "M8,15 C5,17 5,20 6,22 M6,22 C4,22 3,20.5 4,19", null, BlueDark, 1.6);
    }

    private static void MoveSelection(Canvas c)
    {
        AddPath(c, "M3,2 L3,17 L7.2,13.2 L10.2,20 L13,18.7 L10,12.2 L16,12 Z", White, BlueDark, 1.35);
        AddPath(c, "M17,11 L17,21 M12.5,16 L21.5,16 M17,11 L15.4,12.8 M17,11 L18.6,12.8 M17,21 L15.4,19.2 M17,21 L18.6,19.2", null, GrayDark, 1.2);
    }

    private static void EllipseSelect(Canvas c)
    {
        AddEllipse(c, 3, 4, 17, 16, BluePale, BlueDark, 1.3, dash: true);
    }

    private static void Zoom(Canvas c)
    {
        AddEllipse(c, 3, 2, 14, 14, White, BlueDark, 2);
        AddPath(c, "M14,14 L21,21", null, GrayDark, 3);
        AddPath(c, "M7,4 L7,14 M11,3 L11,15 M4,7 L16,7 M3,11 L15,11", null, Blue, .75);
    }

    private static void MagicWand(Canvas c)
    {
        AddPath(c, "M5,20 L16,9 L19,12 L8,23 Z", Gray, GrayDark, 1.1);
        AddPath(c, "M7,3 L8,5.3 L10.5,6 L8,6.8 L7,9 L6,6.8 L3.5,6 L6,5.3 Z M16,1 L16.8,3 L19,3.7 L17,4.5 L16.2,6.5 L15.3,4.5 L13.2,3.7 L15.2,3 Z M20,7 L20.6,8.5 L22,9 L20.6,9.5 L20,11 L19.4,9.5 L18,9 L19.4,8.5 Z", GoldPale, Gold, .7);
    }

    private static void Pan(Canvas c)
    {
        AddPath(c, "M7,11 L7,5 C7,3.3 9.2,3.3 9.2,5 L9.2,10 L9.2,3.5 C9.2,1.8 11.5,1.8 11.5,3.5 L11.5,10 L11.5,4 C11.5,2.5 13.8,2.5 13.8,4 L13.8,10 L13.8,6 C13.8,4.5 16,4.5 16,6 L16,14 C16,19 13.5,22 9.5,22 C6,22 4.5,20 3,16 L1.8,13 C1.2,11.5 3.4,10.5 4.3,12 L7,16 Z", GoldPale, Brush("#9C641C"), 1.2);
    }

    private static void Bucket(Canvas c)
    {
        AddPath(c, "M5,5 L16,16 L10,22 L2,14 Z", Brush("#CDD2D5"), GrayDark, 1.25);
        AddPath(c, "M4,13 L15,13 L11,18 L6,18 Z", Blue, BlueDark, .8);
        AddPath(c, "M5,5 L3,2", null, GrayDark, 1.4);
        AddPath(c, "M19,14 C22,18 22,20 19,21 C16,20 16,18 19,14 Z", Blue, BlueDark, .8);
    }

    private static void Gradient(Canvas c)
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1), EndPoint = new Point(1, 0),
            GradientStops = new GradientStopCollection
            {
                new(Color.FromRgb(51, 112, 199), 0), new(Color.FromRgb(103, 204, 225), .35),
                new(Color.FromRgb(244, 226, 116), .68), new(Color.FromRgb(221, 91, 112), 1),
            },
        };
        AddRect(c, 3, 3, 18, 18, gradient, GrayDark, 1.35);
        AddPath(c, "M5,19 L19,5", null, White, .9);
    }

    private static void Paintbrush(Canvas c)
    {
        AddPath(c, "M16,2 C20,1 21,3 19,6 L12,14 L9,11 Z", Gray, GrayDark, 1.1);
        AddPath(c, "M8.5,11 L12.5,15 C11,20 7,22 2,22 C5,19 3,15 8.5,11 Z", Blue, BlueDark, 1.05);
        AddPath(c, "M4,19 C6,20 9,18 10,15", null, Brush("#BFE9FF"), 1);
    }

    private static void Eraser(Canvas c)
    {
        AddPath(c, "M4,15 L13,5 L21,12 L12,22 Z", Purple, PurpleDark, 1.25);
        AddPath(c, "M8,11 L16,18", null, White, 1.2);
        AddPath(c, "M12,22 L22,22", null, GrayDark, 1.3);
    }

    private static void Pencil(Canvas c)
    {
        AddPath(c, "M4,17 L16,4 L20,8 L8,21 L2,22 Z", Orange, Brush("#9E5930"), 1.1);
        AddPath(c, "M16,4 L18,2 L22,6 L20,8 Z", Brush("#E8B1B4"), GrayDark, .9);
        AddPath(c, "M2,22 L4,17 L8,21 Z", GoldPale, GrayDark, .8);
    }

    private static void Picker(Canvas c)
    {
        AddPath(c, "M15,2 C19,0 22,3 20,6 L17,9 L12,4 Z", Blue, BlueDark, 1.05);
        AddPath(c, "M11,5 L17,11 L8,20 L3,21 L4,16 Z", Gray, GrayDark, 1.2);
        AddPath(c, "M4,20 L8,20", null, Blue, 2.1);
    }

    private static void CloneStamp(Canvas c)
    {
        AddPath(c, "M8,4 C8,1 16,1 16,4 L15,10 L9,10 Z", Gray, GrayDark, 1.1);
        AddPath(c, "M5,11 L19,11 L19,16 L5,16 Z", Orange, Brush("#9E5930"), 1.1);
        AddPath(c, "M3,18 L21,18 L21,22 L3,22 Z", Brush("#E2A36F"), GrayDark, 1.1);
    }

    private static void Recolor(Canvas c)
    {
        AddPath(c, "M4,8 C7,3 14,3 18,7 L20,5 L20,11 L14,11 L16.5,8.5 C14,6 9,6 7,9", null, Blue, 2);
        AddPath(c, "M20,16 C17,21 10,21 6,17 L4,19 L4,13 L10,13 L7.5,15.5 C10,18 15,18 17,15", null, Red, 2);
    }

    private static void ColorRemover(Canvas c)
    {
        AddPath(c, "M4,15 L13,5 L21,12 L12,22 Z", Brush("#D69CE6"), PurpleDark, 1.2);
        AddPath(c, "M7,12 L15,19", null, White, 1);
        AddEllipse(c, 2, 2, 9, 9, Brush("#EFF8FA"), BlueDark, 1.1);
        AddPath(c, "M4,6.5 L9,6.5", null, Red, 1.8);
    }

    private static void Text(Canvas c)
    {
        c.Children.Add(new TextBlock
        {
            Text = "T", FontFamily = new FontFamily("Georgia"), FontSize = 25,
            Foreground = GrayDark, FontWeight = FontWeights.Normal,
            Width = 24, Height = 26, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, -4, 0, 0),
        });
    }

    private static void Line(Canvas c)
    {
        AddPath(c, "M3,19 C7,19 7,5 20,5", null, BlueDark, 1.55);
        AddEllipse(c, 1.5, 17.5, 4, 4, White, BlueDark, 1.1);
        AddEllipse(c, 18.5, 3.5, 4, 4, White, BlueDark, 1.1);
    }

    private static void Curve(Canvas c)
    {
        AddPath(c, "M3,19 C8,3 15,22 21,5", null, BlueDark, 1.5);
        AddEllipse(c, 1.5, 17.5, 4, 4, White, PurpleDark, 1);
        AddEllipse(c, 10, 10, 4, 4, White, PurpleDark, 1);
        AddEllipse(c, 19, 3, 4, 4, White, PurpleDark, 1);
    }

    private static void RectangleShape(Canvas c)
    {
        AddRect(c, 3, 4, 14, 14, Brush("#AEDBF2"), BlueDark, 1.2);
        AddRect(c, 8, 8, 13, 13, Brush("#D5B5E8"), PurpleDark, 1.2);
    }

    private static void RoundedRectangle(Canvas c)
    {
        var rect = AddRect(c, 3, 4, 18, 16, Brush("#E5C6EF"), PurpleDark, 1.35);
        rect.RadiusX = rect.RadiusY = 4;
    }

    private static void EllipseShape(Canvas c)
    {
        AddEllipse(c, 2, 5, 15, 15, Brush("#AEDBF2"), BlueDark, 1.2);
        AddEllipse(c, 8, 3, 14, 14, Brush("#C7E4C9"), Brush("#4D9067"), 1.2);
    }

    private static void Freeform(Canvas c)
    {
        AddPath(c, "M4,16 C1,11 5,4 10,7 C14,2 21,5 20,11 C23,16 18,21 13,18 C9,22 3,21 4,16 Z", Brush("#B7E0D1"), Brush("#3F8970"), 1.3);
    }

    private static void SpritePreview(Canvas c)
    {
        AddRect(c, 2, 4, 20, 16, Brush("#26313A"), BlueDark, 1.2);
        AddRect(c, 4, 6, 4, 4, GoldPale, Gold, 0.8);
        AddRect(c, 10, 6, 4, 4, Brush("#B7E0D1"), Green, 0.8);
        AddRect(c, 16, 6, 4, 4, Brush("#D5B5E8"), PurpleDark, 0.8);
        AddPath(c, "M9,12 L9,18 L15,15 Z", Red, White, 0.9);
    }

    private static void ResourceGlyph(Canvas c, string key)
    {
        var path = new Path
        {
            Data = (Geometry)Application.Current.FindResource(key),
            Width = 20, Height = 20, Stretch = Stretch.Uniform,
            StrokeThickness = 1.4, Margin = new Thickness(2),
        };
        path.SetResourceReference(Shape.StrokeProperty, "IconBrush");
        c.Children.Add(path);
    }

    private static Path AddPath(Canvas c, string data, Brush? fill, Brush? stroke, double thickness)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data), Fill = fill, StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        if (stroke == null) path.SetResourceReference(Shape.StrokeProperty, "IconBrush");
        else path.Stroke = stroke;
        c.Children.Add(path);
        return path;
    }

    private static Rectangle AddRect(Canvas c, double x, double y, double width, double height,
        Brush? fill, Brush stroke, double thickness, bool dash = false)
    {
        var rect = new Rectangle { Width = width, Height = height, Fill = fill, Stroke = stroke, StrokeThickness = thickness };
        if (dash) rect.StrokeDashArray = new DoubleCollection { 2, 1.5 };
        Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y); c.Children.Add(rect);
        return rect;
    }

    private static Ellipse AddEllipse(Canvas c, double x, double y, double width, double height,
        Brush? fill, Brush stroke, double thickness, bool dash = false)
    {
        var ellipse = new Ellipse { Width = width, Height = height, Fill = fill, Stroke = stroke, StrokeThickness = thickness };
        if (dash) ellipse.StrokeDashArray = new DoubleCollection { 2, 1.5 };
        Canvas.SetLeft(ellipse, x); Canvas.SetTop(ellipse, y); c.Children.Add(ellipse);
        return ellipse;
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
