using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Data;
using Sonara.Core.Yt;

namespace Sonara.App.Views;

/// <summary>Home: mix quotidiano, i tuoi mix, novità, scaffali personali, generi preferiti, In evidenza e suggerimenti di YouTube Music.</summary>
public sealed class HomeView : UserControl
{
    /// <summary>Lo stato della Home resta uguale cambiando pagina: i suggerimenti si rifanno al massimo ogni 6 ore.</summary>
    private sealed class Cache
    {
        public List<Shelf> Personal = new(), GenreShelves = new(), ArtistShelves = new(), Explore = new(), Remote = new();
        public List<List<Track>> Radios = new();
        public List<Track> Mix = new();
        public List<ForYouMix> Mixes = new();
        public ForYouMix? Weekly;
        public List<Card> Releases = new();
        public List<Entry> Featured = new();
        public string MixKey = "", GenreKey = "", ArtistKey = "", WeeklyKey = "";
        public long PersonalAt, ReleasesAt, GenreAt, RemoteAt;
        public int Version = -1;
        public bool MixLoading;
    }

    private static readonly Cache C = new();
    private static readonly long SixHours = 6L * 3600_000;
    private static long Now => Environment.TickCount64;

    private readonly AppServices _a = AppServices.I;
    private readonly StackPanel _body = new() { Padding = new Thickness(28, 8, 28, 40) };
    private readonly ScrollViewer _scroll;
    private readonly CancellationTokenSource _cts = new();
    private FavGenres.Genre? _chip;
    private List<Shelf>? _chipShelves;
    private int _queued;

    private AppSettings S => _a.Settings;

    public HomeView()
    {
        _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = _scroll;
        if (!S.FavArtistsSynced)
        {
            // gli artisti già scelti (Benvenuto o Impostazioni) devono essere anche in Raccolta › Salvati
            foreach (var e in S.FavArtists) _a.Store.AddSaved(e);
            S.FavArtistsSynced = true; S.Save();
        }
        Render();
        Unloaded += (_, _) => _cts.Cancel();
        _ = LoadAsync(false);
    }

    // ------------------------------------------------------------------ disegno

    private void Refresh()
    {
        if (Interlocked.Exchange(ref _queued, 1) == 1) return;
        _a.UI.TryEnqueue(() => { Interlocked.Exchange(ref _queued, 0); if (!_cts.IsCancellationRequested) Render(); });
    }

    private void Render()
    {
        var offset = _scroll.VerticalOffset;
        _body.Children.Clear();
        BuildRecentSearches();
        BuildChips();
        if (_chip != null)
        {
            if (_chipShelves == null) _body.Children.Add(Center(K.Spinner()));
            else if (_chipShelves.Count == 0) _body.Children.Add(K.T("Nessun suggerimento per questo genere", 13, Pal.Sub));
            else foreach (var sh in _chipShelves) _body.Children.Add(Shelves.Make(sh));
            return;
        }
        BuildBanner();
        var forYou = C.Mixes.Concat(C.Weekly != null ? new[] { C.Weekly } : Array.Empty<ForYouMix>()).ToList();
        if (forYou.Count > 0) _body.Children.Add(MixRow(forYou));
        if (C.Releases.Count > 0) _body.Children.Add(Shelves.Make("Novità per te", C.Releases));
        var h = _a.Store.History.Take(14).ToList();
        if (h.Count > 0) _body.Children.Add(Shelves.ForTracks("Ascoltati di recente", h.Select(_a.Resolve)));
        var wide = C.GenreShelves.Select(sh => sh.Cards.FirstOrDefault(c => c.Entry?.Kind == Kind.Playlists)?.Entry).Where(e => e != null).Cast<Entry>()
            .Concat(C.Featured.Take(3)).GroupBy(e => e.BrowseId).Select(g => g.First()).Take(4).ToList();
        if (wide.Count > 0) _body.Children.Add(Recommended(wide));
        foreach (var sh in C.Personal) _body.Children.Add(Shelves.Make(sh));
        foreach (var sh in C.ArtistShelves) _body.Children.Add(Shelves.Make(sh));
        foreach (var sh in C.GenreShelves) _body.Children.Add(Shelves.Make(sh));
        var fav = Taste.ForgottenFavorites(_a.Store.Favorites, _a.Store.Plays, 14);
        if (fav.Count > 0) _body.Children.Add(Shelves.ForTracks("Preferiti da riascoltare", fav.Select(_a.Resolve)));
        if (C.Featured.Count > 0) _body.Children.Add(Shelves.ForEntries("In evidenza", C.Featured));
        var imps = _a.Imports.All;
        if (imps.Count > 0) _body.Children.Add(Shelves.ForEntries("Importate", imps.Select(i => i.Entry), "Importa…", () => ImportDialog.Show(XamlRoot)));
        else _body.Children.Add(ImportHint());
        foreach (var sh in C.Explore) _body.Children.Add(Shelves.Make(sh));
        foreach (var sh in C.Remote) _body.Children.Add(Shelves.Make(sh));
        var refresh = K.Pill("Aggiorna suggerimenti", () => { C.Remote = new(); C.RemoteAt = 0; _ = LoadAsync(true); }, false, K.Refresh);
        refresh.HorizontalAlignment = HorizontalAlignment.Left; refresh.Margin = new Thickness(0, 24, 0, 0);
        _body.Children.Add(refresh);
        if (offset > 0) _scroll.DispatcherQueue.TryEnqueue(() => _scroll.ChangeView(null, offset, null, true));
    }

    private static UIElement Center(UIElement e) { var g = new Grid { Padding = new Thickness(0, 32, 0, 0) }; g.Children.Add(e); return g; }

    private void BuildRecentSearches()
    {
        var s = _a.Store.Searches.Take(4).ToList();
        if (s.Count == 0) return;
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        sp.Children.Add(K.T("Ricerche recenti", 13, Pal.Sub, true));
        foreach (var q in s)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(K.Icon("", 13, Pal.Sub));
            row.Children.Add(K.T(q, 14, Pal.Text));
            sp.Children.Add(K.Clickable(row, () => Nav.Go("search:" + q, () => new SearchView(q)), pad: new Thickness(6, 6, 12, 6)));
        }
        _body.Children.Add(sp);
    }

    /// <summary>«Home» e i generi preferiti: un tocco mostra brani, playlist e artisti di quel genere.</summary>
    private void BuildChips()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(K.Chip("Home", _chip == null, () => PickGenre(null)));
        var picked = FavGenres.Parse(S.FavGenres);
        foreach (var g in picked) { var gg = g; row.Children.Add(K.Chip(gg.Label, _chip?.Id == gg.Id, () => PickGenre(_chip?.Id == gg.Id ? null : gg))); }
        if (picked.Count == 0) row.Children.Add(K.Chip("＋ Scegli i generi", false, () => Nav.Go("settings", () => new SettingsView())));
        _body.Children.Add(new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled });
    }

    private async void PickGenre(FavGenres.Genre? g)
    {
        _chip = g; _chipShelves = null;
        Render();
        if (g == null) return;
        try
        {
            var l = await Feed.GenreChipShelvesAsync(g, _cts.Token);
            if (_chip?.Id == g.Id) { _chipShelves = l; Render(); }
        }
        catch (Exception) { }
    }

    private void BuildBanner()
    {
        var mix = C.Mix;
        var grid = new Grid { Height = 210, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 12, 0, 8) };
        grid.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0x12, 0x3B, 0x27), Offset = 0 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0x1B, 0x1B, 0x3A), Offset = 0.6 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0x3A, 0x16, 0x4A), Offset = 1 },
            },
        };
        // le copertine del mix, sfumate, sul lato destro
        if (mix.Count > 0)
        {
            var covers = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.55 };
            foreach (var t in mix.Take(3)) covers.Children.Add(K.Cover(t.Cover, 210, 0));
            grid.Children.Add(covers);
            grid.Children.Add(new Border
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 0),
                    GradientStops =
                    {
                        new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0x14, 0x24, 0x30), Offset = 0 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(230, 0x14, 0x24, 0x30), Offset = 0.45 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(40, 0x14, 0x24, 0x30), Offset = 1 },
                    },
                },
            });
        }
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36, 0, 0, 0), Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        left.Children.Add(K.T("IL TUO MIX QUOTIDIANO", 11, K.Brush("#D4D4DC"), true));
        left.Children.Add(new TextBlock { Text = "La tua musica, oggi.", FontSize = 34, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = K.Brush("#FFFFFF") });
        left.Children.Add(K.T(
            mix.Count > 0 ? "Una selezione creata per accompagnare la tua giornata."
            : C.MixLoading ? "Sto preparando il tuo mix…"
            : "Scegli i tuoi generi o artisti preferiti: il mix nasce da lì.", 14, K.Brush("#D4D4DC")));
        var play = K.Pill(mix.Count > 0 ? "Ascolta ora" : C.MixLoading ? "Un momento…" : "Scegli i generi",
            () => { if (mix.Count > 0 || !C.MixLoading) PlayMix(); }, true, mix.Count > 0 ? K.Play : null);
        play.HorizontalAlignment = HorizontalAlignment.Left; play.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(play);
        grid.Children.Add(left);
        _body.Children.Add(grid);
    }

    private void PlayMix()
    {
        if (C.Mix.Count == 0) { Nav.Go("settings", () => new SettingsView()); return; }
        var bad = Taste.Excluder(_a.Store.Dislikes);
        var l = C.Mix.Where(t => !bad(t)).Select(_a.Resolve).ToList();
        if (l.Count > 0) _a.Player.PlayList(l, 0);
    }

    /// <summary>«I tuoi mix»: schede con la copertina del primo brano; il tocco avvia il mix.</summary>
    private UIElement MixRow(List<ForYouMix> mixes)
    {
        var sp = new StackPanel();
        sp.Children.Add(K.Header("I tuoi mix"));
        sp.Children.Add(K.HRow(mixes.Select(m =>
        {
            var tile = new StackPanel { Width = 160, Spacing = 2 };
            var cover = K.Cover(m.Tracks.FirstOrDefault()?.Cover, 160, 14);
            var badge = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = Pal.Panel, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8), Child = K.Icon(K.Play, 14, Pal.Accent) };
            var holder = new Grid { Width = 160, Height = 160, Margin = new Thickness(0, 0, 0, 8) };
            holder.Children.Add(cover); holder.Children.Add(badge);
            tile.Children.Add(holder);
            tile.Children.Add(K.T(m.Title, 13, Pal.Text, true));
            tile.Children.Add(K.T(m.Subtitle, 12, Pal.Sub, false, 2));
            return (UIElement)K.Clickable(tile, () => _a.Player.PlayList(m.Tracks.Select(_a.Resolve).ToList(), 0), radius: 12, pad: new Thickness(8));
        })));
        return sp;
    }

    /// <summary>«Consigliati per te»: playlist a schede larghe, con il tasto per ascoltarle subito.</summary>
    private UIElement Recommended(List<Entry> entries)
    {
        var sp = new StackPanel { Spacing = 8 };
        sp.Children.Add(K.Header("Consigliati per te"));
        foreach (var e in entries)
        {
            var ee = e;
            var g = new Grid { Height = 92, ColumnSpacing = 14 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cover = K.Cover(YtApi.BigThumb(ee.Thumb), 112, 0, false, false); cover.Height = 92;
            g.Children.Add(cover);
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            tx.Children.Add(K.T(ee.Title, 15, Pal.Text, true));
            tx.Children.Add(K.T(ee.Subtitle.Length > 0 ? ee.Subtitle : "Brani che potrebbero piacerti", 12, Pal.Sub));
            var hint = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
            hint.Children.Add(K.Icon(K.Play, 11, Pal.Accent)); hint.Children.Add(K.T("Ascolta la playlist", 12, Pal.Text));
            tx.Children.Add(hint);
            Grid.SetColumn(tx, 1); g.Children.Add(tx);
            var play = K.IconBtn(K.Play, () => PlayPlaylistNow(ee), 16, "Ascolta " + ee.Title, Pal.Accent, 46);
            play.Margin = new Thickness(0, 0, 14, 0); play.Background = Pal.Panel;
            Grid.SetColumn(play, 2); g.Children.Add(play);
            var card = K.Clickable(g, () => Actions.OpenEntry(ee), Pal.Card, Pal.Hover, 18);
            sp.Children.Add(card);
        }
        return sp;
    }

    /// <summary>Fa partire subito una playlist di YouTube; quelle di Spotify e Deezer si aprono (e si importano).</summary>
    private async void PlayPlaylistNow(Entry e)
    {
        try
        {
            if (PlaylistImport.RefOf(e.BrowseId) != null) { Actions.OpenEntry(e); return; }
            var l = await YtApi.PlaylistTracksAsync(e.BrowseId);
            if (l.Count > 0) _a.Player.PlayList(l.Select(t => _a.Resolve(t.Thumb.Length == 0 && e.Thumb.Length > 0 ? t with { Thumb = e.Thumb } : t)).ToList(), 0);
        }
        catch (Exception) { }
    }

    private UIElement ImportHint()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 24, 0, 0), Spacing = 6 };
        sp.Children.Add(K.T("Importa le tue playlist", 18, Pal.Text, true));
        sp.Children.Add(K.T("Incolla il link di una playlist pubblica di Spotify, Deezer o YouTube nella barra di ricerca.", 13, Pal.Sub, false, 0));
        var b = K.Pill("Importa una playlist", () => ImportDialog.Show(XamlRoot), false, K.Add);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(b);
        return sp;
    }

    // ------------------------------------------------------------------ caricamento

    private async Task LoadAsync(bool force)
    {
        var ct = _cts.Token;
        var tasks = new List<Task> { LoadFeatured(ct), LoadRemote(ct), LoadFavArtists(ct), LoadPersonal(force, ct) };
        try { await Task.WhenAll(tasks); } catch (Exception) { }
    }

    private async Task LoadPersonal(bool force, CancellationToken ct)
    {
        try
        {
            var st = _a.Store;
            var prof = Taste.BuildProfile(st.Plays, st.History, st.Favorites, st.Skips, st.Dislikes);
            var fresh = C.PersonalAt != 0 && Now - C.PersonalAt < SixHours && st.TasteVersion == C.Version;
            if (force || !fresh)
            {
                var w = prof.Weights.Where(kv => !prof.Bad(kv.Value.Item)).ToDictionary(kv => kv.Key, kv => kv.Value);
                C.Version = st.TasteVersion;
                if (force) { C.Personal = new(); C.GenreKey = ""; C.WeeklyKey = ""; C.MixKey = ""; C.ReleasesAt = 0; }
                if (w.Count > 0)
                {
                    // il genere degli artisti che ascolti di più (da Deezer, ricordato): serve ai punteggi dei generi
                    await Genres.FetchMissingAsync(st, Taste.TopArtists(w, 15).Select(a => a.Name), 12, ct);
                    await LoadGenreShelves(prof, ct);
                    var r = await Feed.BuildAsync(w, prof.Bad, sh =>
                    {
                        C.Personal = C.Personal.Where(x => x.Title != sh.Title).Append(sh).ToList();
                        Refresh();
                    }, ct);
                    C.Radios = r.Radios;
                    C.PersonalAt = Now;
                }
                else await LoadGenreShelves(prof, ct);
            }
            await LoadMixes(prof, ct);
            await LoadWeekly(prof, ct);
            await LoadReleases(prof, ct);
        }
        catch (Exception) { }
    }

    private async Task LoadGenreShelves(Taste.Profile prof, CancellationToken ct)
    {
        var st = _a.Store;
        var scores = Taste.GenreScores(prof, t => Genres.GenreOf(t, st));
        var day = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400;
        var use = Feed.PickGenres(S.FavGenres, scores, st.Plays.Count, day);
        if (use.Count == 0) { C.GenreShelves = new(); C.GenreKey = ""; Refresh(); return; }
        var key = string.Join(",", use.Select(g => g.Id));
        if (key == C.GenreKey && Now - C.GenreAt < SixHours) return;
        C.GenreKey = key; C.GenreAt = Now; C.GenreShelves = new();
        foreach (var g in use)
        {
            var sh = await Feed.GenreShelfAsync(g, ct);
            if (sh != null && key == C.GenreKey) { C.GenreShelves = C.GenreShelves.Append(sh).ToList(); Refresh(); }
        }
        if (C.GenreShelves.Count == 0 && key == C.GenreKey) C.GenreKey = "";   // riprovo alla prossima apertura
    }

    private async Task LoadFavArtists(CancellationToken ct)
    {
        try
        {
            var mine = S.FavArtists.ToList();
            if (mine.Count == 0) { C.ArtistShelves = new(); C.ArtistKey = ""; return; }
            var key = string.Join(",", mine.Select(a => a.BrowseId));
            if (key == C.ArtistKey) return;
            C.ArtistKey = key; C.ArtistShelves = new();
            await Feed.FavArtistShelvesAsync(mine, sh =>
            {
                if (key != C.ArtistKey) return;
                C.ArtistShelves = C.ArtistShelves.Where(x => x.Title != sh.Title).Append(sh).ToList();
                Refresh();
            }, ct);
        }
        catch (Exception) { }
    }

    /// <summary>Il mix: radio dai tuoi ascolti, brani degli artisti preferiti e dei generi scelti; uguale per tutta la giornata.</summary>
    private async Task LoadMixes(Taste.Profile prof, CancellationToken ct)
    {
        var st = _a.Store;
        var day = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400;
        var key = $"{day}|{S.FavGenres}|{string.Join(",", S.FavArtists.Select(a => a.BrowseId))}|{C.Radios.Sum(r => r.Count)}|{st.TasteVersion}";
        if (key == C.MixKey || C.MixLoading) return;
        C.MixLoading = true; Refresh();
        try
        {
            var m = await ForYou.BuildMixesAsync(prof, S.FavGenres, S.FavArtists.ToList(), C.Radios, day, ct);
            C.Mix = m.Daily; C.Mixes = m.Numbered;
            if (m.Daily.Count > 0) C.MixKey = key;   // senza nulla in mano riprovo alla prossima apertura
        }
        finally { C.MixLoading = false; Refresh(); }
    }

    /// <summary>«Scopri settimanale»: brani nuovi per te che non hai ancora ascoltato; cambia ogni settimana.</summary>
    private async Task LoadWeekly(Taste.Profile prof, CancellationToken ct)
    {
        if (C.Radios.Count == 0) return;
        var week = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (7 * 86400);
        var key = $"{week}|{C.Radios.Count}";
        if (key == C.WeeklyKey) return;
        var similar = C.Personal.FirstOrDefault(s => s.Title == "Artisti simili")?.Cards.Select(c => c.Entry).Where(e => e != null).Cast<Entry>().Take(4).ToList() ?? new();
        C.Weekly = await ForYou.BuildWeeklyAsync(prof, _a.Store, C.Radios, similar, week, ct);
        if (C.Weekly != null) C.WeeklyKey = key;
        Refresh();
    }

    /// <summary>«Novità per te»: tra le nuove uscite, quelle dei tuoi artisti e degli artisti simili.</summary>
    private async Task LoadReleases(Taste.Profile prof, CancellationToken ct)
    {
        if (Now - C.ReleasesAt < SixHours && C.ReleasesAt != 0) return;
        var names = Taste.TopArtists(prof.Weights, 15).Select(a => a.Name)
            .Concat(S.FavArtists.Select(a => a.Title))
            .Concat(C.Personal.Where(s => s.Title == "Artisti simili").SelectMany(s => s.Cards.Select(c => c.Entry?.Title ?? "")))
            .ToList();
        var l = await ForYou.BuildReleasesAsync(names, ct);
        if (l == null) return;   // rete assente: riprovo alla prossima apertura
        C.Releases = l; C.ReleasesAt = Now;
        Refresh();
    }

    private async Task LoadFeatured(CancellationToken ct)
    {
        try
        {
            var country = S.EffectiveCountry();
            C.Featured = Featured.SpotifyNow(country); Refresh();
            C.Featured = await Featured.LoadAsync(country, l => { C.Featured = l; Refresh(); }, ct);
            Refresh();
        }
        catch (Exception) { }
    }

    /// <summary>Suggerimenti, nuove uscite e classifiche di YouTube Music (ogni parte è indipendente).</summary>
    private async Task LoadRemote(CancellationToken ct)
    {
        if (C.RemoteAt != 0 && Now - C.RemoteAt < SixHours && (C.Remote.Count > 0 || C.Explore.Count > 0)) return;
        C.RemoteAt = Now;
        try { var (_, shelves) = await YtApi.HomeFeedAsync(null, ct); C.Remote = shelves; Refresh(); } catch (Exception) { }
        var extra = new List<Shelf>();
        try { var news = await YtApi.NewReleasesAsync(ct); if (news != null) extra.Add(news); C.Explore = extra.ToList(); Refresh(); } catch (Exception) { }
        try { extra.AddRange(await YtApi.ChartsAsync(ct)); C.Explore = extra.GroupBy(s => s.Title).Select(g => g.First()).ToList(); Refresh(); } catch (Exception) { }
        if (C.Remote.Count == 0 && C.Explore.Count == 0) C.RemoteAt = 0;   // riprovo alla prossima apertura
    }
}
