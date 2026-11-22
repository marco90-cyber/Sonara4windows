using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core.Data;

namespace Sonara.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        var s = AppSettings.Load();
        Pal.Init(s.LightTheme);
        RequestedTheme = s.LightTheme ? ApplicationTheme.Light : ApplicationTheme.Dark;
        InitializeComponent();
        UnhandledException += (_, e) => { try { System.IO.File.AppendAllText(System.IO.Path.Combine(Persist.Root, "crash.log"), e.Exception + "\n"); } catch { } e.Handled = true; };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Init(DispatcherQueue.GetForCurrentThread());
        _window = new MainWindow();
        _window.Activate();
    }
}
