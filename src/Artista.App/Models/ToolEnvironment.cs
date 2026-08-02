using System.ComponentModel;
using System.Runtime.CompilerServices;
using Artista.Core.Drawing;
using Artista.Core.Selections;

namespace Artista.App.Models;

public enum FillStyle
{
    Outline,
    Fill,
    FillAndOutline,
}

/// <summary>
/// Shared tool settings (Paint.NET's "AppEnvironment"): colors, brush width,
/// tolerance, antialiasing, etc. Tools read from here; the tool settings bar
/// and the Colors panel write to it.
/// </summary>
public sealed class ToolEnvironment : INotifyPropertyChanged
{
    private uint _primaryColor = 0xFF000000;
    private uint _secondaryColor = 0xFFFFFFFF;
    private double _brushWidth = 8;
    private double _hardness = 0.75;
    private double _opacity = 1.0;
    private double _tolerance = 25;
    private double _softness = 20;
    private bool _antialias = true;
    private FillStyle _fillStyle = FillStyle.Outline;
    private double _cornerRadius = 10;
    private SelectionCombineMode _combineMode = SelectionCombineMode.Replace;
    private bool _wandGlobal;
    private GradientShape _gradientShape = GradientShape.Linear;
    private bool _gradientToTransparent;
    private string _fontFamily = "Segoe UI";
    private double _fontSize = 24;
    private bool _fontBold;
    private bool _fontItalic;
    private bool _sampleFromClick = true;
    private bool _sampleContinuously;
    private double _feather;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public uint PrimaryColor { get => _primaryColor; set => Set(ref _primaryColor, value); }
    public uint SecondaryColor { get => _secondaryColor; set => Set(ref _secondaryColor, value); }
    public double BrushWidth { get => _brushWidth; set => Set(ref _brushWidth, Math.Clamp(value, 1, 500)); }
    public double Hardness { get => _hardness; set => Set(ref _hardness, Math.Clamp(value, 0, 1)); }
    public double Opacity { get => _opacity; set => Set(ref _opacity, Math.Clamp(value, 0, 1)); }
    public double Tolerance { get => _tolerance; set => Set(ref _tolerance, Math.Clamp(value, 0, 100)); }
    public double Softness { get => _softness; set => Set(ref _softness, Math.Clamp(value, 0, 100)); }
    public bool Antialias { get => _antialias; set => Set(ref _antialias, value); }
    public FillStyle FillStyle { get => _fillStyle; set => Set(ref _fillStyle, value); }
    public double CornerRadius { get => _cornerRadius; set => Set(ref _cornerRadius, Math.Clamp(value, 0, 200)); }
    public SelectionCombineMode CombineMode { get => _combineMode; set => Set(ref _combineMode, value); }
    public bool WandGlobal { get => _wandGlobal; set => Set(ref _wandGlobal, value); }
    public GradientShape GradientShape { get => _gradientShape; set => Set(ref _gradientShape, value); }
    public bool GradientToTransparent { get => _gradientToTransparent; set => Set(ref _gradientToTransparent, value); }
    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, value); }
    public double FontSize { get => _fontSize; set => Set(ref _fontSize, Math.Clamp(value, 4, 400)); }
    public bool FontBold { get => _fontBold; set => Set(ref _fontBold, value); }
    public bool FontItalic { get => _fontItalic; set => Set(ref _fontItalic, value); }

    /// <summary>Color Remover: sample the target from the first clicked pixel.</summary>
    public bool SampleFromClick { get => _sampleFromClick; set => Set(ref _sampleFromClick, value); }

    /// <summary>Color Remover: re-sample the target continuously while dragging.</summary>
    public bool SampleContinuously { get => _sampleContinuously; set => Set(ref _sampleContinuously, value); }

    /// <summary>Selection feather radius applied when a new selection is made.</summary>
    public double Feather { get => _feather; set => Set(ref _feather, Math.Clamp(value, 0, 100)); }

    public void SwapColors() => (PrimaryColor, SecondaryColor) = (SecondaryColor, PrimaryColor);

    public void ResetColors()
    {
        PrimaryColor = 0xFF000000;
        SecondaryColor = 0xFFFFFFFF;
    }
}
