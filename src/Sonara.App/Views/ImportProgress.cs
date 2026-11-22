using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Ui;

namespace Sonara.App.Views;

/// <summary>Titolo, barra di avanzamento e conteggio di un'importazione in corso.</summary>
public sealed class ImportProgress : StackPanel
{
    private readonly TextBlock _name = K.T("Leggo la playlist…", 16, Pal.Text, true);
    private readonly TextBlock _count = K.T("", 12, Pal.Sub);
    private readonly ProgressBar _bar = new() { IsIndeterminate = true, MinWidth = 320, Foreground = Pal.Accent };

    public ImportProgress()
    {
        Spacing = 10; MinWidth = 340;
        Children.Add(_name); Children.Add(_bar); Children.Add(_count);
    }

    /// <summary>Da chiamare dal thread dell'interfaccia.</summary>
    public void Set(string name, int done, int total)
    {
        if (!string.IsNullOrEmpty(name)) _name.Text = name;
        if (total <= 0) { _bar.IsIndeterminate = true; _count.Text = "Cerco i brani…"; return; }
        _bar.IsIndeterminate = false; _bar.Maximum = total; _bar.Value = Math.Min(done, total);
        _count.Text = $"{done} di {total} brani abbinati";
    }
}
