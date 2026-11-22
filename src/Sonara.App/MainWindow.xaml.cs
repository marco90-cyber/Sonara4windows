using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.App.Views;
using WinRT.Interop;

namespace Sonara.App;

public sealed partial class MainWindow : Window
{
    private MediaKeys? _keys;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Sonara";
        FolderPick.Hwnd = WindowNative.GetWindowHandle(this);
        try { AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "sonara.ico")); } catch { }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1480, 920));
        if (AppWindow.TitleBar is { } tb)
        {
            tb.BackgroundColor = Pal.Bg.Color; tb.InactiveBackgroundColor = Pal.Bg.Color;
            tb.ButtonBackgroundColor = Pal.Bg.Color; tb.ButtonInactiveBackgroundColor = Pal.Bg.Color;
        }
        RootGrid.Background = Pal.Bg;
        try { _keys = new MediaKeys(FolderPick.Hwnd); } catch { }
        var a = AppServices.I;
        if (a.Settings.WelcomeDone) RootGrid.Children.Add(new Shell());
        else RootGrid.Children.Add(new WelcomeView(() => { RootGrid.Children.Clear(); RootGrid.Children.Add(new Shell()); }));
        Closed += (_, _) => { try { a.Player.Stop(); } catch { } };
    }
}
