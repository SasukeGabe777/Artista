using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Artista.App;

public enum AppTheme
{
    Dark,
    Light,
    System,
}

/// <summary>
/// Swaps the theme resource dictionary at runtime and applies dark title bars
/// via DWM. All control styles reference palette brushes with DynamicResource,
/// so switching the dictionary retheming the entire app including open
/// dialogs, menus and popups.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _current;

    public static AppTheme Theme { get; private set; } = AppTheme.Dark;

    /// <summary>The effective theme after resolving System.</summary>
    public static bool IsDarkEffective =>
        Theme == AppTheme.Dark || (Theme == AppTheme.System && IsSystemDark());

    public static event EventHandler? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        Theme = theme;
        bool dark = IsDarkEffective;
        var uri = new Uri($"Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        var app = Application.Current;
        if (_current != null)
            app.Resources.MergedDictionaries.Remove(_current);
        // Insert palette before Controls.xaml so lookups resolve.
        app.Resources.MergedDictionaries.Insert(0, dict);
        _current = dict;

        foreach (Window window in app.Windows)
            ApplyTitleBar(window);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Applies a dark/light title bar to the window (call after SourceInitialized).</summary>
    public static void ApplyTitleBar(Window window)
    {
        var helper = new WindowInteropHelper(window);
        if (helper.Handle == IntPtr.Zero)
        {
            window.SourceInitialized += (_, _) => ApplyTitleBar(window);
            return;
        }
        int dark = IsDarkEffective ? 1 : 0;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        _ = DwmSetWindowAttribute(helper.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }
}
