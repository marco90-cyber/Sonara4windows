using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core.Data;

namespace Sonara.App.Views;

/// <summary>Schermata di benvenuto: introduzione, cartella download, correzione brani, paese, generi.</summary>
public sealed class WelcomeView : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private readonly Action _done;
    private int _page;
    private readonly Grid _host = new();

    public WelcomeView(Action done)
    {
        _done = done;
        var outer = new Grid { Background = Pal.Bg };
        var card = new Border { Width = 560, Background = Pal.Card, CornerRadius = new CornerRadius(16), Padding = new Thickness(32), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = _host };
        outer.Children.Add(card);
        Content = outer;
        Show();
    }

    private static readonly string[] Genres = { "Pop", "Rock", "Hip hop", "Elettronica", "Jazz", "Classica", "Indie", "R&B", "Metal", "Latino", "Dance", "Cantautori" };

    private void Show()
    {
        var s = new StackPanel { Spacing = 14 };
        void Title(string t, string sub) { s.Children.Add(K.T(t, 26, Pal.Text, true, 2)); s.Children.Add(K.T(sub, 13, Pal.Sub, false, 6)); }
        switch (_page)
        {
            case 0:
                Title("Benvenuto in Sonara", "Musica da YouTube, solo audio, senza account. Cerca, ascolta, salva le playlist e scarica per ascoltare offline.");
                s.Children.Add(K.T("♥ preferiti   ⬇ scarica   ⇄ cambia sorgente   ⟳ playlist importate", 12, Pal.Sub, false, 3));
                break;
            case 1:
                Title("Dove salvare i download?", "Scegli una cartella per i brani scaricati. Puoi cambiarla in seguito dalle Impostazioni.");
                var f = K.T(string.IsNullOrEmpty(_a.Settings.DownloadFolder) ? "Nessuna cartella scelta" : _a.Settings.DownloadFolder, 12, Pal.Sub, false, 2);
                s.Children.Add(f);
                s.Children.Add(K.Pill("Scegli cartella", async () => { if (await FolderPick.PickDownloadFolderAsync()) f.Text = _a.Settings.DownloadFolder; }, false, K.Folder));
                break;
            case 2:
                Title("Il tuo paese", "Serve per le classifiche e i consigli.");
                var c = new ComboBox { ItemsSource = Countries.Codes.Select(Countries.Name).ToList(), Width = 280 };
                c.SelectedIndex = Math.Max(0, Array.IndexOf(Countries.Codes, _a.Settings.EffectiveCountry()));
                c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) { _a.Settings.Region = Countries.Codes[c.SelectedIndex]; } };
                s.Children.Add(c);
                break;
            case 3:
                Title("Che musica ti piace?", "Scegli i generi: Sonara parte da qui per i consigli.");
                var chosen = new HashSet<string>((_a.Settings.FavGenres ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries));
                var wrap = new ItemsControl();
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var rows = new StackPanel { Spacing = 8 };
                for (var i = 0; i < Genres.Length; i += 4)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    foreach (var g in Genres.Skip(i).Take(4))
                    {
                        var gg = g;
                        Border? chip = null;
                        chip = K.Chip(gg, chosen.Contains(gg), () =>
                        {
                            if (!chosen.Add(gg)) chosen.Remove(gg);
                            _a.Settings.FavGenres = string.Join('|', chosen);
                            Show();
                        });
                        row.Children.Add(chip);
                    }
                    rows.Children.Add(row);
                }
                s.Children.Add(rows);
                break;
        }
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (_page > 0) nav.Children.Add(K.Pill("Indietro", () => { _page--; Show(); }, false));
        var last = _page == 3;
        nav.Children.Add(K.Pill(last ? "Inizia" : "Avanti", () =>
        {
            if (last) { _a.Settings.WelcomeDone = true; _a.Settings.Save(); _done(); }
            else { _page++; Show(); }
        }));
        s.Children.Add(nav);
        _host.Children.Clear();
        _host.Children.Add(s);
    }
}
