using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Data;
using Sonara.Core.Yt;

namespace Sonara.App.Views;

/// <summary>Importa una playlist pubblica da Spotify, Deezer o YouTube incollando il link.</summary>
public static class ImportDialog
{
    public static async void Show(XamlRoot root, string? text = null)
    {
        var box = new TextBox { PlaceholderText = "Link della playlist (Spotify, Deezer, YouTube)", Text = text ?? "", MinWidth = 420 };
        var info = K.T("Solo playlist pubbliche. Spotify dà circa i primi 100 brani.", 12, Pal.Sub, 0);
        var sp = new StackPanel { Spacing = 10 };
        sp.Children.Add(box); sp.Children.Add(info);
        var d = new ContentDialog { XamlRoot = root, Title = "Importa una playlist", Content = sp, PrimaryButtonText = "Importa", CloseButtonText = "Annulla", DefaultButton = ContentDialogButton.Primary };
        if (await d.ShowAsync() != ContentDialogResult.Primary) return;
        var r = await PlaylistImport.ParseResolvingAsync(box.Text);
        if (r == null) { await K.Info(root, "Link non riconosciuto", "Incolla il link di una playlist di Spotify, Deezer o YouTube."); return; }
        Start(r, root);
    }

    public static async void Start(PlaylistImport.Ref r, XamlRoot? root)
    {
        var a = AppServices.I;
        try
        {
            var imp = await a.Runner.ImportPlaylistAsync(r);
            Nav.Go("entry:" + imp.Entry.BrowseId, () => new DetailView(imp.Entry));
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (root != null) await K.Info(root, "Importazione non riuscita", e.Message);
        }
    }
}

/// <summary>«Cambia sorgente»: sostituisce l'audio di un brano con un altro video (con anteprima).</summary>
public static class SourceDialog
{
    public static async Task ShowAsync(Track t, XamlRoot root)
    {
        var a = AppServices.I;
        var box = new TextBox { Text = $"{t.Artist} {t.Title}".Trim(), MinWidth = 460 };
        var list = new StackPanel { Spacing = 4 };
        var scroll = new ScrollViewer { Content = list, MaxHeight = 360 };
        var keep = new CheckBox { Content = "Tieni titolo, artista e copertina originali", IsChecked = true };
        var status = K.T("", 12, Pal.Sub);
        var sp = new StackPanel { Spacing = 10 };
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(box);
        var go = new Button { Content = "Cerca" };
        Grid.SetColumn(go, 1); row.Children.Add(go);
        sp.Children.Add(row); sp.Children.Add(status); sp.Children.Add(scroll); sp.Children.Add(keep);
        var d = new ContentDialog { XamlRoot = root, Title = "Cambia sorgente", Content = sp, CloseButtonText = "Chiudi" };

        async Task Search()
        {
            status.Text = "Cerco…"; list.Children.Clear();
            try
            {
                var songs = await YtApi.SearchSongsAsync(box.Text, 1);
                var vids = await YtApi.SearchVideosAsync(box.Text, 1);
                var ids = songs.Select(s => s.VideoId).ToHashSet();
                var all = songs.Select(s => (s, false)).Concat(vids.Where(v => !ids.Contains(v.VideoId)).Select(v => (v, true))).ToList();
                status.Text = all.Count == 0 ? "Nessun risultato" : $"{all.Count} risultati";
                foreach (var (c, isVideo) in all)
                {
                    var r = new Grid { ColumnSpacing = 10, Padding = new Thickness(4) };
                    r.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    r.ColumnDefinitions.Add(new ColumnDefinition());
                    r.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    r.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    r.Children.Add(K.Cover(c.Cover, 44, 6));
                    var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    txt.Children.Add(K.T(c.Title, 13, Pal.Text, true));
                    txt.Children.Add(K.T($"{c.Artist} · {c.DurationText}{(isVideo ? " · video" : "")}", 12, Pal.Sub));
                    Grid.SetColumn(txt, 1); r.Children.Add(txt);
                    var pv = new Button { Content = "Ascolta" }; pv.Click += (_, _) => a.Player.PlayOne(c);
                    Grid.SetColumn(pv, 2); r.Children.Add(pv);
                    var use = new Button { Content = "Usa" };
                    use.Click += (_, _) => { a.Overrides.Set(t.VideoId, c, keep.IsChecked == true); d.Hide(); };
                    Grid.SetColumn(use, 3); r.Children.Add(use);
                    list.Children.Add(r);
                }
            }
            catch (Exception e) { status.Text = "Ricerca non riuscita: " + e.Message; }
        }
        go.Click += async (_, _) => await Search();
        d.Opened += async (_, _) => await Search();
        await d.ShowAsync();
    }
}
