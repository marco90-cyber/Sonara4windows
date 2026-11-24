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

        r.Children.Add(K.Tgl(S.InfiniteRadio, v => { S.InfiniteRadio = v; S.Save(); }, "Radio infinita (a fine coda accoda brani simili)"));
        r.Children.Add(K.Tgl(S.AutoFix, v => { S.AutoFix = v; S.Save(); }, "Correzione brani (sostituisce in automatico l'audio con durata sbagliata)"));

        r.Children.Add(Sec("Aspetto"));
        r.Children.Add(K.Tgl(S.LightTheme, v => { S.LightTheme = v; S.Save(); _ = K.Info(XamlRoot, "Tema", "Il tema cambia al prossimo avvio di Sonara."); }, "Tema chiaro"));

        r.Children.Add(K.Tgl(S.CoverActions, v => { S.CoverActions = v; S.Save(); }, "«Mi piace» e «Non mi piace» sulla copertina del lettore"));
        r.Children.Add(K.Tgl(S.DoubleTapLike, v => { S.DoubleTapLike = v; S.Save(); }, "Doppio tocco sulla copertina = mi piace"));
        r.Children.Add(K.Tgl(S.CheckUpdates, v => { S.CheckUpdates = v; S.Save(); }, "Controlla gli aggiornamenti all'avvio"));

        r.Children.Add(Sec("I tuoi gusti"));
        r.Children.Add(K.T("Generi preferiti", 13, Pal.Text, true));
        r.Children.Add(Pickers.Genres(S, true));
        r.Children.Add(K.T("Artisti preferiti", 13, Pal.Text, true));
        r.Children.Add(Pickers.Artists(S, true));

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

        r.Children.Add(Sec("Cambio computer"));
        r.Children.Add(K.T("Un solo file con libreria, preferiti, gusti, playlist tue e importate, sorgenti cambiate e impostazioni. I brani scaricati non ci sono: si ritrovano scegliendo la stessa cartella dei download.", 12, Pal.Sub, false, 0));
        var xr = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        xr.Children.Add(K.Pill("Esporta…", ExportAsync, false, K.Download));
        xr.Children.Add(K.Pill("Importa…", ImportAsync, false, K.Folder));
        r.Children.Add(xr);

        r.Children.Add(Sec("Dati e ripristino"));
        r.Children.Add(K.T("Ogni voce chiede conferma. Dopo la cancellazione Sonara si riavvia da sola.", 12, Pal.Sub, false, 0));
        foreach (var (title, sub, warn, act) in Resets())
        {
            var tt = title; var ww = warn; var aa = act;
            var row = new StackPanel { Spacing = 0, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(K.T(title, 13, Pal.Text, true)); row.Children.Add(K.T(sub, 11, Pal.Sub));
            r.Children.Add(K.Clickable(row, async () =>
            {
                if (!await K.Confirm(XamlRoot, tt + "?", ww + "\n\nNon si può annullare.", "Conferma")) return;
                aa();
                Restart();
            }, Pal.Card, Pal.Hover, 10, new Thickness(14, 10, 14, 10)));
        }

        r.Children.Add(K.T("Sonara per Windows — derivato da Sonara per Android (GPL-3.0)", 11, Pal.Sub));
        Content = new ScrollViewer { Content = r };
    }

    private (string Title, string Sub, string Warn, Action Do)[] Resets() => new (string, string, string, Action)[]
    {
        ("Reimposta le impostazioni", "Interruttori e scelte tornano ai valori di partenza",
            "Tutti gli interruttori e le scelte tornano ai valori di partenza.\n\nRestano: cartella dei download, paese, equalizzatore, generi e artisti scelti, e i tuoi dati.", () => S.ResetPreferences()),
        ("Preferiti, salvati e cronologia", "Preferiti, playlist tue, salvati, cronologia, ascolti e «non mi piace»",
            "Cancella preferiti, playlist tue, playlist/album/artisti salvati, cronologia, ricerche recenti, ascolti e «non mi piace». La Home riparte da zero.", () => _a.Store.ClearAll()),
        ("Playlist importate", "Da Spotify, Deezer e YouTube",
            "Dimentica tutte le playlist importate. Si possono importare di nuovo.", () => _a.Imports.ClearAll()),
        ("Sorgenti cambiate", "Le sostituzioni fatte con «Cambia sorgente»",
            "I brani tornano alla sorgente originale. Con «Correzione brani» attiva potrebbero essere corretti di nuovo.", () => _a.Overrides.ClearAll()),
        ("Elenco dei download", "I file audio restano nella cartella",
            "L'app dimentica i brani scaricati e i loro gruppi. I file restano nella cartella, ma non si vedono più in Raccolta.", () => _a.Downloads.ForgetAll()),
        ("Resetta tutto", "Impostazioni e tutti i dati qui sopra",
            "Reimposta le impostazioni e cancella preferiti, salvati, cronologia, playlist tue e importate, sorgenti cambiate ed elenco dei download.\n\nI file audio scaricati non vengono toccati.",
            () => { _a.Store.ClearAll(); _a.Imports.ClearAll(); _a.Overrides.ClearAll(); _a.Downloads.ForgetAll(); _a.Dsp = new Sonara.Core.Audio.DspSettings(); _a.Dsp.Save(); var d = new AppSettings(); d.Save(); }),
    };

    private static void Restart()
    {
        var exe = Environment.ProcessPath;
        if (exe != null) System.Diagnostics.Process.Start(exe);
        Environment.Exit(0);
    }

    private async void ExportAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(DataTransfer.FileName), SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
            picker.FileTypeChoices.Add("Backup di Sonara", new List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, FolderPick.Hwnd);
            var f = await picker.PickSaveFileAsync();
            if (f == null) return;
            await File.WriteAllTextAsync(f.Path, DataTransfer.Export(_a.Store, _a.Imports, _a.Overrides, S));
            await K.Info(XamlRoot, "Esportato", "Il file è pronto: portalo sull'altro computer e usa «Importa…».");
        }
        catch (Exception e) { await K.Info(XamlRoot, "Esportazione non riuscita", e.Message); }
    }

    private async void ImportAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, FolderPick.Hwnd);
            var f = await picker.PickSingleFileAsync();
            if (f == null) return;
            var msg = DataTransfer.Import(_a.Store, _a.Imports, _a.Overrides, S, await File.ReadAllTextAsync(f.Path));
            if (msg == null) { await K.Info(XamlRoot, "File non valido", "Questo non è un backup di Sonara."); return; }
            await K.Info(XamlRoot, "Importato", msg + ". Alcune scelte si applicano al riavvio.");
            Nav.Refresh();
        }
        catch (Exception e) { await K.Info(XamlRoot, "Importazione non riuscita", e.Message); }
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
