using Microsoft.UI.Xaml;

namespace Sonara.App.Services;

/// <summary>Navigazione tra le pagine con indietro e avanti.</summary>
public static class Nav
{
    private sealed record Page(string Key, Func<UIElement> Make);

    private static readonly List<Page> History = new();
    private static int _i = -1;

    public static event Action<UIElement, string>? Navigated;

    public static bool CanBack => _i > 0;
    public static bool CanForward => _i < History.Count - 1;
    public static string CurrentKey => _i >= 0 ? History[_i].Key : "";

    public static void Go(string key, Func<UIElement> make)
    {
        if (_i >= 0 && History[_i].Key == key && key != "") { Show(); return; }
        if (_i < History.Count - 1) History.RemoveRange(_i + 1, History.Count - _i - 1);
        History.Add(new Page(key, make));
        _i = History.Count - 1;
        Show();
    }

    public static void Back() { if (CanBack) { _i--; Show(); } }
    public static void Forward() { if (CanForward) { _i++; Show(); } }

    /// <summary>Ricrea la pagina in corso (per esempio dopo un cambio di impostazioni).</summary>
    public static void Refresh() { if (_i >= 0) Show(); }

    private static void Show()
    {
        var p = History[_i];
        Navigated?.Invoke(p.Make(), p.Key);
    }
}
