using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Sonara.App.Ui;

/// <summary>Colori dell'app (scuro come nel riferimento grafico, oppure chiaro).</summary>
public static class Pal
{
    public static bool Light { get; private set; }
    public static SolidColorBrush Bg = null!, Side = null!, Panel = null!, Card = null!, Hover = null!, Line = null!;
    public static SolidColorBrush Text = null!, Sub = null!, Accent = null!, OnAccent = null!, Red = null!, Transparent = null!;
    public static Color AccentColor;

    private static SolidColorBrush B(string hex)
    {
        var h = hex.TrimStart('#');
        byte a = 255; var i = 0;
        if (h.Length == 8) { a = Convert.ToByte(h[..2], 16); i = 2; }
        return new SolidColorBrush(Color.FromArgb(a, Convert.ToByte(h.Substring(i, 2), 16), Convert.ToByte(h.Substring(i + 2, 2), 16), Convert.ToByte(h.Substring(i + 4, 2), 16)));
    }

    public static void Init(bool light)
    {
        Light = light;
        if (!light)
        {
            Bg = B("#0B0B0D"); Side = B("#08080A"); Panel = B("#111114"); Card = B("#18181C"); Hover = B("#26262C"); Line = B("#26262B");
            Text = B("#FFFFFF"); Sub = B("#A0A0AB");
        }
        else
        {
            Bg = B("#F4F4F6"); Side = B("#EBEBEF"); Panel = B("#FFFFFF"); Card = B("#FFFFFF"); Hover = B("#E3E3E8"); Line = B("#D8D8DE");
            Text = B("#111114"); Sub = B("#5E5E69");
        }
        Accent = B("#1ED760"); OnAccent = B("#06130A"); Red = B("#E5484D"); Transparent = new SolidColorBrush(Colors.Transparent);
        AccentColor = Color.FromArgb(255, 0x1E, 0xD7, 0x60);
    }
}
