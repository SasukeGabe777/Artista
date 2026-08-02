using System.Windows;
using System.Windows.Threading;

namespace Artista.App;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Settings = AppSettings.Load();
        if (Enum.TryParse<AppTheme>(Settings.Theme, out var theme))
            ThemeManager.Apply(theme);
        else
            ThemeManager.Apply(AppTheme.System);

        DispatcherUnhandledException += OnUnhandledException;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (e.Args.Contains("--uitest"))
        {
            string outputDir = e.Args.SkipWhile(a => a != "--uitest").Skip(1).FirstOrDefault()
                ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "artista-uitest");
            _ = window.Dispatcher.InvokeAsync(async () =>
            {
                int exitCode = await window.RunSelfTestAsync(outputDir);
                Shutdown(exitCode);
            });
            return;
        }

        if (e.Args.Length > 0)
            window.OpenFilesOnStartup(e.Args.Where(System.IO.File.Exists).ToArray());
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nYour documents are still open — save your work.",
            "Artista", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Settings.Save();
        base.OnExit(e);
    }
}
