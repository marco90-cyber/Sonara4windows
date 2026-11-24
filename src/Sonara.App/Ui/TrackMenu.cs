using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Views;
using Sonara.Core;
using Sonara.Core.Yt;

namespace Sonara.App.Ui;

/// <summary>Il menu delle azioni su un brano (tre puntini o tasto destro).</summary>
public static class TrackMenu
{
    private static MenuFlyoutItem Item(string text, string glyph, Action a)
    {
        var i = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        i.Click += (_, _) => a();
        return i;
    }

    public static void Show(Track t, FrameworkElement anchor, IList<Track>? context = null, Action? onRemove = null, string removeLabel = "Rimuovi dalla coda")
    {
        var a = AppServices.I;
        var f = new MenuFlyout();
        var idx = context?.ToList().FindIndex(x => x.VideoId == t.VideoId) ?? -1;
        f.Items.Add(Item("Riproduci", K.Play, () => { if (context != null && idx >= 0) a.Player.PlayList(context, idx); else a.Player.PlayOne(t); }));
        f.Items.Add(Item("Riproduci dopo", K.Next, () => a.Player.PlayNext(t)));
        f.Items.Add(Item("Aggiungi alla coda", K.Queue, () => a.Player.AddToQueue(t)));
        f.Items.Add(Item("Radio da questo brano", K.Radio, async () =>
        {
            try { var r = await YtApi.RadioAsync(a.Overrides.Target(t.VideoId)); if (r.Count > 0) a.Player.PlayList(r, 0); } catch (Exception) { }
        }));
        var add = new MenuFlyoutSubItem { Text = "Aggiungi a una playlist", Icon = new FontIcon { Glyph = K.Add } };
        add.Items.Add(Item("Nuova playlist…", K.Add, () => MyPlaylists.AddToNew(t, anchor.XamlRoot)));
        foreach (var pl in a.Store.MyPlaylists.Take(15)) { var id = pl.Id; add.Items.Add(Item(pl.Name, K.Queue, () => MyPlaylists.AddTo(id, t, anchor.XamlRoot))); }
        f.Items.Add(add);
        f.Items.Add(new MenuFlyoutSeparator());
        var fav = a.Store.IsFavorite(t.VideoId);
        f.Items.Add(Item(fav ? "Togli dai preferiti" : "Aggiungi ai preferiti", fav ? K.HeartFill : K.Heart, () => a.Store.ToggleFavorite(t)));
        var dis = a.Store.IsDisliked(t.VideoId);
        f.Items.Add(Item(dis ? "Annulla «non mi piace»" : "Non mi piace", K.ThumbDown, () => a.Store.ToggleDislike(t)));
        if (a.Downloads.IsDownloaded(t.VideoId))
            f.Items.Add(Item("Elimina il download", K.Delete, () => a.Downloads.Delete(i => i.VideoId == t.VideoId)));
        else
            f.Items.Add(Item("Scarica", K.Download, () => Actions.Download("Brani", new[] { t }, anchor.XamlRoot)));
        f.Items.Add(new MenuFlyoutSeparator());
        f.Items.Add(Item("Vai all'artista", K.Person, () => Actions.OpenArtistByName(t.Artist)));
        f.Items.Add(Item("Cambia sorgente…", K.Swap, () => _ = SourceDialog.ShowAsync(t, anchor.XamlRoot)));
        if (a.Overrides.Has(t.VideoId)) f.Items.Add(Item("Ripristina la sorgente originale", K.Refresh, () => a.Overrides.Remove(t.VideoId)));
        if (onRemove != null) { f.Items.Add(new MenuFlyoutSeparator()); f.Items.Add(Item(removeLabel, K.Close, onRemove)); }
        f.ShowAt(anchor);
    }
}
