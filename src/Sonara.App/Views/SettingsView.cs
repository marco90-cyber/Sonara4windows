using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core.Data;

namespace Sonara.App.Views;

/// <summary>Impostazioni.</summary>
public sealed class SettingsView : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private AppSettings S => _a.Settings;

    public SettingsView()
    {
        var r = new StackPanel { Margin = new Thickness(28, 16, 28, 28), Spacing = 12 };
        r.Children.Add(K.T("Impostazioni", 30, Pal.Text, true));

        r.Children.Add(Sec("Riproduzione"));
        var q = new ComboBox { ItemsSource = new[] { "Migliore", "Alta", "Risparmio dati" }, SelectedIndex = Math.Clamp(S.Quality, 0, 2), Width = 220 };
        q.SelectionChanged += (_, _) => { S.Quality = q.SelectedIndex; _a.Resolver.Quality = q.SelectedIndex; S.Save(); };
        r.Children.Add(Row("Qualità audio", q));
        r.Children.Add(K.Tgl(S.ExclusiveOutput, v => { S.ExclusiveOutput = v; S.Save(); }, "Uscita esclusiva WASAPI (bit-perfect, se supportata)"));

        r.Children.Add(K.Tgl(S.AutoFix, v => { S.AutoFix = v; S.Save(); }, "Correzione brani (sostituisce in automatico l'audio con durata sbagliata)"));

        r.Children.Add(Sec("Aspetto"));
        r.Children.Add(K.Tgl(S.LightTheme, v => { S.LightTheme = v; S.Save(); _ = K.Info(XamlRoot, "Tema", "Il tema cambia al prossimo avvio di Sonara."); }, "Tema chiaro"));

        r.Children.Add(K.Tgl(S.CheckUpdates, v => { S.CheckUpdates = v; S.Save(); }, "Controlla gli aggiornamenti all'avvio"));

        r.Children.Add(Sec("I tuoi gusti"));
        r.Children.Add(K.T("Generi preferiti", 13, Pal.Text, true));
        var chosen = new HashSet<string>((S.FavGenres ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries));
        var gWrap = new StackPanel { Spacing = 8 };
        void BuildGenres()
        {
            gWrap.Children.Clear();
            for (var i = 0; i < WelcomeView.Genres.Length; i += 4)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var g in WelcomeView.Genres.Skip(i).Take(4))
                {
                    var gg = g;
                    row.Children.Add(K.Chip(gg, chosen.Contains(gg), () =>
                    {
                        if (!chosen.Add(gg)) chosen.Remove(gg);
                        S.FavGenres = string.Join('|', chosen); S.Save(); BuildGenres();
                    }));
                }
                gWrap.Children.Add(row);
            }
        }
        BuildGenres();
        r.Children.Add(gWrap);
        r.Children.Add(K.T("Artisti preferiti", 13, Pal.Text, true));
        var chips = new StackPanel { Spacing = 6 };
        void BuildArtists()
        {
            chips.Children.Clear();
            foreach (var e in S.FavArtists.ToList())
            {
                var ee = e;
                chips.Children.Add(K.Chip("✓ " + ee.Title, true, () => { S.FavArtists.RemoveAll(x => x.BrowseId == ee.BrowseId); S.Save(); BuildArtists(); }));
            }
        }
        var res = new StackPanel { Spacing = 4 };
        var box = new AutoSuggestBox { PlaceholderText = "Cerca un artista da aggiungere", Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        box.QuerySubmitted += async (b, _) =>
        {
            res.Children.Clear();
            try
            {
                var l = await Sonara.Core.Yt.YtApi.SearchEntriesAsync(b.Text, Sonara.Core.Yt.Kind.Artists, 1);
                foreach (var a in l.Take(6))
                {
                    var aa = a;
                    res.Children.Add(K.Clickable(K.T(aa.Title, 13, Pal.Text), () =>
                    {
                        if (!S.FavArtists.Any(x => x.BrowseId == aa.BrowseId)) { S.FavArtists.Add(aa); S.Save(); }
                        res.Children.Clear(); BuildArtists();
                    }, null, null, 8, new Thickness(8)));
                }
            }
            catch { res.Children.Add(K.T("Ricerca non riuscita", 12, Pal.Sub)); }
        };
        BuildArtists();
        r.Children.Add(box); r.Children.Add(res); r.Children.Add(chips);

        r.Children.Add(Sec("Paese"));
        var c = new ComboBox { ItemsSource = Countries.Codes.Select(Countries.Name).ToList(), Width = 260 };
        c.SelectedIndex = Math.Max(0, Array.IndexOf(Countries.Codes, S.EffectiveCountry()));
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) { S.Region = Countries.Codes[c.SelectedIndex]; S.Save(); } };
        r.Children.Add(Row("Classifiche e In evidenza", c));

        r.Children.Add(Sec("Download"));
        var folder = K.T(string.IsNullOrEmpty(S.DownloadFolder) ? "Nessuna cartella scelta" : S.DownloadFolder, 12, Pal.Sub);
        r.Children.Add(folder);
        r.Children.Add(K.Pill("Scegli cartella", async () => { if (await FolderPick.PickDownloadFolderAsync()) folder.Text = S.DownloadFolder; }, false, K.Folder));

        r.Children.Add(Sec("Playlist importate"));
        r.Children.Add(K.Tgl(S.ImportAutoRefresh, v => { S.ImportAutoRefresh = v; S.Save(); }, "Aggiorna automaticamente"));
        var hrs = new ComboBox { ItemsSource = new[] { "Ogni 6 ore", "Ogni 12 ore", "Ogni giorno", "Ogni 3 giorni", "Ogni settimana" }, Width = 200 };
        var hv = new[] { 6, 12, 24, 72, 168 };
        hrs.SelectedIndex = Math.Max(0, Array.IndexOf(hv, S.ImportRefreshHours));
        hrs.SelectionChanged += (_, _) => { if (hrs.SelectedIndex >= 0) { S.ImportRefreshHours = hv[hrs.SelectedIndex]; S.Save(); } };
        r.Children.Add(Row("Frequenza", hrs));
        r.Children.Add(K.Tgl(S.ImportNotify, v => { S.ImportNotify = v; S.Save(); }, "Avvisami delle novità"));

        r.Children.Add(Sec("Cache"));
        var cache = new ComboBox { ItemsSource = new[] { "256 MB", "512 MB", "1 GB", "2 GB", "4 GB" }, Width = 160 };
        var mbs = new[] { 256, 512, 1024, 2048, 4096 };
        cache.SelectedIndex = Math.Max(0, Array.IndexOf(mbs, S.CacheMaxMb));
        cache.SelectionChanged += (_, _) => { if (cache.SelectedIndex >= 0) { S.CacheMaxMb = mbs[cache.SelectedIndex]; S.Save(); } };
        r.Children.Add(Row("Dimensione massima", cache));

        r.Children.Add(Sec("Ripristino"));
        r.Children.Add(K.Pill("Cancella cronologia", async () =>
        {
            if (await K.Confirm(XamlRoot, "Cancella cronologia", "Vuoi cancellare cronologia e ascolti?", "Cancella")) _a.Store.ClearHistory();
        }, false, K.Delete));
        r.Children.Add(K.Pill("Ripristina Sonara", async () =>
        {
            if (!await K.Confirm(XamlRoot, "Ripristina Sonara", "Cancella preferiti, playlist importate e impostazioni. Sonara si riavvia.", "Ripristina")) return;
            _a.Store.ClearAll(); _a.Imports.ClearAll(); _a.Dsp = new Sonara.Core.Audio.DspSettings(); _a.Dsp.Save();
            var s = new AppSettings(); s.Save();
            var exe = Environment.ProcessPath;
            if (exe != null) System.Diagnostics.Process.Start(exe);
            Environment.Exit(0);
        }, false, K.Refresh));

        r.Children.Add(K.T("Sonara per Windows — derivato da Sonara per Android (GPL-3.0)", 11, Pal.Sub));
        Content = new ScrollViewer { Content = r };
    }

    private static TextBlock Sec(string t) { var x = K.T(t, 18, Pal.Text, true); x.Margin = new Thickness(0, 10, 0, 0); return x; }

    private static UIElement Row(string label, UIElement e)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(K.T(label, 13, Pal.Text));
        Grid.SetColumn((FrameworkElement)e, 1); g.Children.Add(e);
        return g;
    }
}
