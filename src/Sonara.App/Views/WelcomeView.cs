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
                break;
            case 1:
                Title("Legenda dei simboli", "Cosa significano le icone che trovi nell'app.");
                foreach (var (g, t) in new[] { (K.Heart, "Mi piace: salva il brano tra i preferiti"), (K.ThumbDown, "Non mi piace: non lo proponiamo più (non salta il brano)"), (K.Download, "Scarica per ascoltare offline"), (K.DownDone, "Brano già scaricato"), (K.Swap, "Cambia sorgente: scegli un'altra versione del brano"), (K.Bookmark, "Salva playlist, album o artista nella libreria"), (K.Refresh, "Aggiorna una playlist importata") })
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                    row.Children.Add(K.Icon(g, 16, Pal.Accent)); row.Children.Add(K.T(t, 13, Pal.Text, false, 0));
                    s.Children.Add(row);
                }
                break;
            case 2:
                Title("Dove salvare i download?", "Scegli una cartella per i brani scaricati. Puoi cambiarla in seguito dalle Impostazioni.");
                var f = K.T(string.IsNullOrEmpty(_a.Settings.DownloadFolder) ? "Nessuna cartella scelta" : _a.Settings.DownloadFolder, 12, Pal.Sub, false, 2);
                s.Children.Add(f);
                s.Children.Add(K.Pill("Scegli cartella", async () => { if (await FolderPick.PickDownloadFolderAsync()) f.Text = _a.Settings.DownloadFolder; }, false, K.Folder));
                break;
            case 3:
                Title("Correzione brani", "Sistema in automatico titolo e artista dei brani (esclude bootleg, cover, remix e simili). Di base è spenta.");
                s.Children.Add(K.Tgl(_a.Settings.AutoFix, v => _a.Settings.AutoFix = v, "Attiva la correzione brani"));
                break;
            case 4:
                Title("Il tuo paese", "Serve per le classifiche e i consigli.");
                var c = new ComboBox { ItemsSource = Countries.Codes.Select(Countries.Name).ToList(), Width = 280 };
                c.SelectedIndex = Math.Max(0, Array.IndexOf(Countries.Codes, _a.Settings.EffectiveCountry()));
                c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) { _a.Settings.Region = Countries.Codes[c.SelectedIndex]; } };
                s.Children.Add(c);
                break;
            case 5:
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
        if (_page == 6)
        {
            Title("I tuoi artisti", "Cerca e scegli gli artisti che ascolti di più (facoltativo).");
            var sel = _a.Settings.FavArtists;
            var chips = new StackPanel { Spacing = 6 };
            void Rebuild() { chips.Children.Clear(); foreach (var e in sel.ToList()) { var ee = e; chips.Children.Add(K.Chip("✓ " + ee.Title, true, () => { sel.RemoveAll(x => x.BrowseId == ee.BrowseId); Rebuild(); })); } }
            var res = new StackPanel { Spacing = 4 };
            var box = new AutoSuggestBox { PlaceholderText = "Cerca un artista", Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
            box.QuerySubmitted += async (b, e) =>
            {
                res.Children.Clear();
                try
                {
                    var l = await Sonara.Core.Yt.YtApi.SearchEntriesAsync(b.Text, Sonara.Core.Yt.Kind.Artists, 1);
                    foreach (var a in l.Take(6))
                    {
                        var aa = a;
                        res.Children.Add(K.Clickable(K.T(aa.Title, 13, Pal.Text), () => { if (!sel.Any(x => x.BrowseId == aa.BrowseId)) sel.Add(aa); Rebuild(); }, null, null, 8, new Thickness(8)));
                    }
                }
                catch { res.Children.Add(K.T("Ricerca non riuscita", 12, Pal.Sub)); }
            };
            s.Children.Add(box); s.Children.Add(res); s.Children.Add(chips); Rebuild();
        }
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (_page > 0) nav.Children.Add(K.Pill("Indietro", () => { _page--; Show(); }, false));
        var last = _page == 6;
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
