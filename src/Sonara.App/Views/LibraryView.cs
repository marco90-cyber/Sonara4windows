using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Data;
using Sonara.Core.Yt;

namespace Sonara.App.Views;

/// <summary>Raccolta: preferiti, playlist tue, salvati, scaricati, cronologia e riepilogo degli ascolti.</summary>
public sealed class LibraryView : UserControl
{
    public static readonly string[] Tabs = { "Preferiti", "Playlist", "Salvati", "Scaricati", "Cronologia", "Riepilogo" };

    // filtri e ordine restano uguali cambiando pagina
    private static int _favSort, _savedKind, _savedSort;
    private static string _favQuery = "", _savedQuery = "";

    private readonly AppServices _a = AppServices.I;
    private readonly Grid _root = new();
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(28, 16, 28, 8) };
    private readonly Border _area = new();
    private string _tab;
    private string? _group;
    private string? _openPlaylist;
    private Entry? _undo;

    public LibraryView(string tab = "Preferiti", string? openPlaylist = null)
    {
        _tab = Tabs.Contains(tab) ? tab : Tabs[0];
        _openPlaylist = openPlaylist;
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition());
        _root.Children.Add(new ScrollViewer { Content = _chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled });
        Grid.SetRow(_area, 1);
        _root.Children.Add(_area);
        Content = _root;
        Build();
    }

    private string Label(string t) => t switch
    {
        "Preferiti" => $"Preferiti · {_a.Store.Favorites.Count}",
        "Playlist" => $"Playlist · {_a.Store.MyPlaylists.Count}",
        "Salvati" => $"Salvati · {SavedAll().Count}",
        "Scaricati" => $"Scaricati · {_a.Downloads.Items.Select(i => i.VideoId).Distinct().Count()}",
        _ => t,
    };

    private void Build()
    {
        _chips.Children.Clear();
        foreach (var t in Tabs) { var tt = t; _chips.Children.Add(K.Chip(Label(t), _tab == t, () => { _tab = tt; _group = null; _openPlaylist = null; _undo = null; Build(); })); }
        _area.Child = _tab switch
        {
            "Preferiti" => Favorites(),
            "Playlist" => Playlists(),
            "Salvati" => Saved(),
            "Cronologia" => Songs(_a.Store.History.Select(_a.Resolve).ToList(), "La cronologia è vuota.", clear: true),
            "Riepilogo" => Summary(),
            _ => Downloads(),
        };
    }

    private static string N(string s) => Taste.Norm(s);

    private static UIElement SearchBox(string placeholder, string text, Action<string> changed)
    {
        var box = new TextBox { PlaceholderText = placeholder, Text = text, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        box.TextChanged += (_, _) => changed(box.Text);
        return box;
    }

    // ------------------------------------------------------------------ preferiti

    private UIElement Favorites()
    {
        var all = _a.Store.Favorites.Select(_a.Resolve).ToList();
        if (all.Count == 0) return Msg("Nessun brano preferito. Tocca il cuore su un brano per salvarlo qui.");
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var host = new Border();
        Grid.SetRow(host, 1);
        var head = new StackPanel { Margin = new Thickness(28, 4, 28, 6), Spacing = 8 };
        var sorts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        void Fill()
        {
            var q = N(_favQuery);
            var l = q.Length == 0 ? all : all.Where(t => N(t.Title).Contains(q) || N(t.Artist).Contains(q)).ToList();
            l = _favSort switch
            {
                1 => l.OrderBy(t => N(t.Title)).ToList(),
                2 => l.OrderBy(t => N(t.Artist)).ThenBy(t => N(t.Title)).ToList(),
                _ => l,
            };
            host.Child = Songs(l, "Nessun risultato.");
        }
        void Sorts()
        {
            sorts.Children.Clear();
            var names = new[] { "Recenti", "A-Z", "Artista" };
            for (var i = 0; i < names.Length; i++) { var ii = i; sorts.Children.Add(K.Chip(names[i], _favSort == i, () => { _favSort = ii; Sorts(); Fill(); })); }
        }
        head.Children.Add(SearchBox("Cerca nei preferiti", _favQuery, v => { _favQuery = v; Fill(); }));
        head.Children.Add(sorts);
        grid.Children.Add(head); grid.Children.Add(host);
        Sorts(); Fill();
        return grid;
    }

    // ------------------------------------------------------------------ playlist tue

    private UIElement Playlists()
    {
        var st = _a.Store;
        var open = _openPlaylist == null ? null : st.MyPlaylists.FirstOrDefault(p => p.Id == _openPlaylist);
        if (open != null) return OpenPlaylist(open);
        var sp = new StackPanel { Margin = new Thickness(28, 4, 28, 30), Spacing = 6 };
        var nw = K.Pill("Nuova playlist", async () =>
        {
            var name = await MyPlaylists.PromptName(XamlRoot, "Nuova playlist", "", "Crea");
            if (name == null) return;
            _openPlaylist = st.CreatePlaylist(name).Id; Build();
        }, true, K.Add);
        nw.HorizontalAlignment = HorizontalAlignment.Left; nw.Margin = new Thickness(0, 0, 0, 8);
        sp.Children.Add(nw);
        if (st.MyPlaylists.Count == 0) sp.Children.Add(K.T("Nessuna playlist tua: creane una e aggiungi i brani dal menu ⋯ di un brano.", 14, Pal.Sub, false, 0));
        foreach (var p in st.MyPlaylists)
        {
            var pp = p;
            var g = new Grid { ColumnSpacing = 12, Height = 64 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(K.Cover(pp.Items.FirstOrDefault()?.Cover, 56, 10));
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            tx.Children.Add(K.T(pp.Name, 15, Pal.Text, true));
            tx.Children.Add(K.T($"Playlist tua · {pp.Items.Count} brani", 12, Pal.Sub));
            Grid.SetColumn(tx, 1); g.Children.Add(tx);
            sp.Children.Add(K.Clickable(g, () => { _openPlaylist = pp.Id; Build(); }, radius: 12, pad: new Thickness(6)));
        }
        return new ScrollViewer { Content = sp };
    }

    private UIElement OpenPlaylist(MyPlaylist p)
    {
        var st = _a.Store;
        var tracks = p.Items.Select(_a.Resolve).ToList();
        var head = new StackPanel { Margin = new Thickness(28, 4, 28, 10), Spacing = 10 };
        head.Children.Add(K.Clickable(K.T("‹  Tutte le playlist", 13, Pal.Sub), () => { _openPlaylist = null; Build(); }, pad: new Thickness(4)));
        head.Children.Add(K.T(p.Name, 30, Pal.Text, true, 2));
        head.Children.Add(K.T($"{tracks.Count} brani", 13, Pal.Sub));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (tracks.Count > 0)
        {
            row.Children.Add(K.Pill("Riproduci", () => _a.Player.PlayList(tracks, 0), true, K.Play));
            row.Children.Add(K.Pill("Casuale", () => _a.Player.PlayList(tracks, 0, true), false, K.Shuffle));
        }
        row.Children.Add(K.Pill("Rinomina", async () =>
        {
            var name = await MyPlaylists.PromptName(XamlRoot, "Rinomina la playlist", p.Name);
            if (name != null) { st.RenamePlaylist(p.Id, name); Build(); }
        }, false));
        row.Children.Add(K.Pill("Elimina", async () =>
        {
            if (await K.Confirm(XamlRoot, "Eliminare la playlist?", $"«{p.Name}» e il suo elenco di brani verranno cancellati. I brani scaricati restano.", "Elimina")) { st.DeletePlaylist(p.Id); _openPlaylist = null; Build(); }
        }, false, K.Delete));
        head.Children.Add(row);
        if (tracks.Count == 0)
        {
            head.Children.Add(K.T("Playlist vuota: usa «Aggiungi a una playlist» nel menu ⋯ di un brano.", 13, Pal.Sub, false, 0));
            return head;
        }
        return new TrackList(tracks, head, true, t => { st.RemoveFromPlaylist(p.Id, t.VideoId); Build(); }, "Togli dalla playlist");
    }

    // ------------------------------------------------------------------ salvati

    private List<Entry> SavedAll() =>
        _a.Store.Saved.Concat(_a.Imports.All.Select(i => i.Entry)).GroupBy(e => e.BrowseId).Select(g => g.First()).ToList();

    /// <summary>Playlist, album e artisti salvati (e le playlist importate): filtri per tipo, sezioni, ricerca, ordine e rimozione rapida con Annulla.</summary>
    private UIElement Saved()
    {
        var st = _a.Store;
        var all = SavedAll();
        if (all.Count == 0) return Msg("Niente di salvato: usa il segnalibro nella pagina di una playlist, un album o un artista.");
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var host = new Border();
        Grid.SetRow(host, 1);
        var head = new StackPanel { Margin = new Thickness(28, 4, 28, 6), Spacing = 8 };
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var kinds = new (Kind? Kd, string Name)[] { (null, "Tutti"), (Kind.Artists, "Artisti"), (Kind.Playlists, "Playlist"), (Kind.Albums, "Album") };
        void Fill()
        {
            var kind = kinds[Math.Clamp(_savedKind, 0, 3)].Kd;
            var q = N(_savedQuery);
            var shown = all.Where(e => (kind == null || e.Kind == kind) && (q.Length == 0 || N(e.Title).Contains(q) || N(e.Subtitle).Contains(q))).ToList();
            if (_savedSort == 1) shown = shown.OrderBy(e => N(e.Title)).ToList();
            var sp = new StackPanel { Margin = new Thickness(28, 4, 28, 30), Spacing = 2 };
            if (shown.Count == 0) sp.Children.Add(K.T("Nessun risultato", 14, Pal.Sub));
            else if (kind == null)
                foreach (var k in new[] { Kind.Artists, Kind.Playlists, Kind.Albums })
                {
                    var part = shown.Where(e => e.Kind == k).ToList();
                    if (part.Count == 0) continue;
                    var h = K.T($"{k.Label()} · {part.Count}", 14, Pal.Accent, true); h.Margin = new Thickness(0, 12, 0, 4);
                    sp.Children.Add(h);
                    foreach (var e in part) sp.Children.Add(SavedRow(e));
                }
            else foreach (var e in shown) sp.Children.Add(SavedRow(e));
            host.Child = new ScrollViewer { Content = sp };
        }
        void Chips()
        {
            chips.Children.Clear();
            for (var i = 0; i < kinds.Length; i++)
            {
                var ii = i; var k = kinds[i];
                var n = k.Kd == null ? all.Count : all.Count(e => e.Kind == k.Kd);
                chips.Children.Add(K.Chip($"{k.Name} · {n}", _savedKind == i, () => { _savedKind = ii; Chips(); Fill(); }));
            }
            chips.Children.Add(K.Chip(_savedSort == 0 ? "Recenti" : "A-Z", false, () => { _savedSort = 1 - _savedSort; Chips(); Fill(); }));
        }
        if (_undo is Entry u)
        {
            var bar = new Grid { ColumnSpacing = 12, Background = Pal.Card, Padding = new Thickness(14, 8, 8, 8) };
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Children.Add(K.T($"«{u.Title}» tolto dai Salvati", 13, Pal.Text));
            var undo = K.Clickable(K.T("Annulla", 13, Pal.Accent, true), () => { st.AddSaved(u); _undo = null; Build(); }, pad: new Thickness(10, 4, 10, 4));
            Grid.SetColumn(undo, 1); bar.Children.Add(undo);
            head.Children.Add(bar);
        }
        head.Children.Add(SearchBox("Cerca nei salvati", _savedQuery, v => { _savedQuery = v; Fill(); }));
        head.Children.Add(chips);
        grid.Children.Add(head); grid.Children.Add(host);
        Chips(); Fill();
        return grid;
    }

    private UIElement SavedRow(Entry e)
    {
        var st = _a.Store;
        var g = new Grid { ColumnSpacing = 12, Height = 60 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(K.Cover(e.Thumb, 48, 8, e.Kind == Kind.Artists));
        var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        tx.Children.Add(K.T(e.Title, 14, Pal.Text, true));
        tx.Children.Add(K.T(string.Join(" · ", new[] { e.Kind.Label(), e.Subtitle }.Where(x => x.Length > 0).Distinct()), 12, Pal.Sub));
        Grid.SetColumn(tx, 1); g.Children.Add(tx);
        if (st.IsSaved(e.BrowseId))
        {
            // segnalibro pieno: un tocco toglie dai Salvati (con «Annulla»)
            var bm = K.IconBtn(K.BookmarkFill, () => { st.ToggleSaved(e); _undo = e; Build(); }, 16, "Togli dai Salvati", Pal.Accent, 40);
            Grid.SetColumn(bm, 2); g.Children.Add(bm);
        }
        return K.Clickable(g, () => Actions.OpenEntry(e), radius: 10, pad: new Thickness(6, 0, 6, 0));
    }

    // ------------------------------------------------------------------ riepilogo

    /// <summary>Artisti, brani e generi più ascoltati.</summary>
    private UIElement Summary()
    {
        var st = _a.Store;
        var plays = st.Plays.Values.ToList();
        var total = plays.Sum(p => p.Count);
        if (total == 0) return Msg("Ascolta qualche brano (almeno 30 secondi): qui vedrai artisti, brani e generi più ascoltati.");
        var byArtist = new Dictionary<string, (string Name, int N)>();
        foreach (var p in plays)
        {
            var a = Taste.ArtistNames(p.Item.Artist).FirstOrDefault();
            if (a == null) continue;
            var k = Taste.Norm(a);
            byArtist[k] = (a, (byArtist.TryGetValue(k, out var c) ? c.N : 0) + p.Count);
        }
        var topArtists = byArtist.Values.OrderByDescending(x => x.N).Take(5).ToList();
        var topTracks = plays.OrderByDescending(p => p.Count).Take(5).Select(p => _a.Resolve(p.Item)).ToList();
        var prof = Taste.BuildProfile(st.Plays, st.History, st.Favorites, st.Skips, st.Dislikes);
        var scores = Taste.GenreScores(prof, t => Genres.GenreOf(t, st));
        var topGenres = scores.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(4)
            .Select(kv => FavGenres.All.FirstOrDefault(g => g.Id == kv.Key)?.Label).Where(l => l != null).ToList();

        var sp = new StackPanel { Margin = new Thickness(28, 8, 28, 30), Spacing = 6 };
        var stats = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 8) };
        var items = new[] { (total, "ascolti"), (plays.Count, "brani"), (byArtist.Count, "artisti") };
        for (var i = 0; i < items.Length; i++)
        {
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            var box = new Border { Background = Pal.Card, CornerRadius = new CornerRadius(14), Padding = new Thickness(12) };
            var v = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            v.Children.Add(K.T(items[i].Item1.ToString(), 26, Pal.Accent, true));
            v.Children.Add(K.T(items[i].Item2, 12, Pal.Sub));
            box.Child = v;
            Grid.SetColumn(box, i); stats.Children.Add(box);
        }
        sp.Children.Add(stats);
        sp.Children.Add(K.T("Artisti più ascoltati", 14, Pal.Accent, true));
        for (var i = 0; i < topArtists.Count; i++)
        {
            var a = topArtists[i];
            var line = new Grid { ColumnSpacing = 10 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(K.T((i + 1).ToString(), 15, Pal.Sub, true));
            var nm = K.T(a.Name, 14, Pal.Text, true); Grid.SetColumn(nm, 1); line.Children.Add(nm);
            var ct = K.T($"{a.N} ascolti", 12, Pal.Sub); Grid.SetColumn(ct, 2); line.Children.Add(ct);
            sp.Children.Add(line);
            sp.Children.Add(new ProgressBar { Minimum = 0, Maximum = topArtists[0].N, Value = a.N, Foreground = Pal.Accent, Margin = new Thickness(38, 0, 0, 6) });
        }
        sp.Children.Add(new Border { Height = 6 });
        sp.Children.Add(K.T("Brani più ascoltati", 14, Pal.Accent, true));
        var list = new TrackList(topTracks, null, true);
        list.Height = topTracks.Count * 58 + 24;
        sp.Children.Add(list);
        if (topGenres.Count > 0)
        {
            sp.Children.Add(K.T("Generi che ascolti di più", 14, Pal.Accent, true));
            sp.Children.Add(K.T(string.Join(" · ", topGenres), 15, Pal.Text));
        }
        return new ScrollViewer { Content = sp };
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
