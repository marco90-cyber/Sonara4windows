using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Data;
using Sonara.Core.Yt;

namespace Sonara.App.Views;

/// <summary>Libreria: preferiti, playlist, album, artisti, cronologia, download.</summary>
public sealed class LibraryView : UserControl
{
    public static readonly string[] Tabs = { "Brani preferiti", "Playlist", "Album", "Artisti", "Cronologia", "Download" };

    private readonly AppServices _a = AppServices.I;
    private readonly Grid _root = new();
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(28, 16, 28, 8) };
    private readonly Border _area = new();
    private string _tab;
    private string? _group;

    public LibraryView(string tab = "Brani preferiti")
    {
        _tab = Tabs.Contains(tab) ? tab : Tabs[0];
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition());
        _root.Children.Add(_chips);
        Grid.SetRow(_area, 1);
        _root.Children.Add(_area);
        Content = _root;
        Build();
    }

    private void Build()
    {
        _chips.Children.Clear();
        foreach (var t in Tabs) { var tt = t; _chips.Children.Add(K.Chip(t, _tab == t, () => { _tab = tt; _group = null; Build(); })); }
        _area.Child = _tab switch
        {
            "Brani preferiti" => Songs(_a.Store.Favorites.Select(_a.Resolve).ToList(), "Nessun brano preferito. Tocca il cuore su un brano per salvarlo qui."),
            "Playlist" => Entries(_a.Imports.All.Select(i => i.Entry).Concat(_a.Store.Saved.Where(e => e.Kind == Kind.Playlists)).GroupBy(e => e.BrowseId).Select(g => g.First()).ToList(), "Nessuna playlist. Importane una o salva quelle che ti piacciono con il segnalibro."),
            "Album" => Entries(_a.Store.Saved.Where(e => e.Kind == Kind.Albums).ToList(), "Nessun album salvato. Usa il segnalibro nella pagina di un album."),
            "Artisti" => Entries(_a.Store.Saved.Where(e => e.Kind == Kind.Artists).ToList(), "Nessun artista salvato. Usa il segnalibro nella pagina di un artista."),
            "Cronologia" => Songs(_a.Store.History.Select(_a.Resolve).ToList(), "La cronologia è vuota.", clear: true),
            _ => Downloads(),
        };
    }

    private UIElement Songs(List<Track> list, string empty, bool clear = false)
    {
        if (list.Count == 0) return Msg(empty);
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(28, 4, 28, 10) };
        head.Children.Add(K.Pill("Riproduci", () => _a.Player.PlayList(list, 0), true, K.Play));
        head.Children.Add(K.Pill("Casuale", () => _a.Player.PlayList(list, 0, true), false, K.Shuffle));
        head.Children.Add(K.T($"{list.Count} brani", 13, Pal.Sub));
        if (clear) head.Children.Add(K.Pill("Cancella", async () =>
        {
            if (await K.Confirm(XamlRoot, "Cancellare la cronologia?", "Si azzerano anche gli ascolti contati per i consigliati.", "Cancella")) { _a.Store.ClearHistory(); Build(); }
        }, false, K.Delete));
        return new TrackList(list, head);
    }

    private UIElement Entries(List<Entry> list, string empty)
    {
        if (list.Count == 0) return Msg(empty);
        var wrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 176, ItemHeight = 224, Margin = new Thickness(20, 8, 20, 30) };
        foreach (var e in list) wrap.Children.Add(K.CardTile(e.Thumb, e.Title, e.Subtitle, () => Actions.OpenEntry(e), 150, e.Kind == Kind.Artists));
        return new ScrollViewer { Content = wrap };
    }

    private UIElement Downloads()
    {
        var d = _a.Downloads;
        var sp = new StackPanel();
        var head = new StackPanel { Margin = new Thickness(28, 4, 28, 8), Spacing = 8 };
        if (!d.HasFolder)
        {
            head.Children.Add(K.T("Scegli la cartella dove salvare i brani scaricati: si ascoltano anche senza internet. Ritrovi tutto anche dopo una reinstallazione scegliendo la stessa cartella.", 13, Pal.Sub, false, 0));
            head.Children.Add(K.Pill("Scegli la cartella", async () => { if (await FolderPick.PickDownloadFolderAsync()) Build(); }, true, K.Folder));
            return head;
        }
        head.Children.Add(K.T($"Cartella: {d.Folder}", 12, Pal.Sub));
        var groups = d.Groups().ToList();
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        chips.Children.Add(K.Chip("Tutti", _group == null, () => { _group = null; Build(); }));
        foreach (var g in groups) { var name = g.Key; chips.Children.Add(K.Chip($"{g.Key} ({g.Count()})", _group == name, () => { _group = name; Build(); })); }
        head.Children.Add(new ScrollViewer { Content = chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled });
        var items = d.Items.Where(i => _group == null || i.Group == _group).ToList();
        if (d.Active)
            head.Children.Add(K.T($"Sto scaricando: {d.Current}", 12, Pal.Accent));
        var tracks = items.Select(i => _a.Resolve(new Track(i.VideoId, i.Title, i.Artist, i.DurationSec, i.Thumb))).ToList();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (tracks.Count > 0)
        {
            row.Children.Add(K.Pill("Riproduci", () => _a.Player.PlayList(tracks, 0), true, K.Play));
            row.Children.Add(K.Pill("Elimina" + (_group != null ? " il gruppo" : " tutto"), async () =>
            {
                if (await K.Confirm(XamlRoot, "Eliminare i download?", "I file vengono cancellati dalla cartella.", "Elimina")) { var g = _group; d.Delete(i => g == null || i.Group == g); _group = null; Build(); }
            }, false, K.Delete));
        }
        row.Children.Add(K.Pill("Cambia cartella", async () => { if (await FolderPick.PickDownloadFolderAsync()) Build(); }, false, K.Folder));
        head.Children.Add(row);
        if (tracks.Count == 0) { head.Children.Add(K.T("Nessun brano scaricato. Usa «Scarica» nel menu di un brano o in una playlist.", 13, Pal.Sub, false, 0)); return head; }
        return new TrackList(tracks, head);
    }

    private static UIElement Msg(string t) => new Grid { Children = { K.T(t, 14, Pal.Sub, false, 0) }, Margin = new Thickness(28, 24, 28, 0), VerticalAlignment = VerticalAlignment.Top };
}
