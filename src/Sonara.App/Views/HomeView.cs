using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Data;
using Sonara.Core.Yt;

namespace Sonara.App.Views;

public sealed class HomeView : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private readonly StackPanel _body = new() { Padding = new Thickness(28, 8, 28, 40) };
    private readonly StackPanel _personal = new();
    private readonly StackPanel _featured = new();
    private readonly StackPanel _imports = new();
    private readonly StackPanel _remote = new();
    private readonly StackPanel _chipRow = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
    private readonly CancellationTokenSource _cts = new();
    private List<Track> _mix = new();
    private string? _chip;

    public HomeView()
    {
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        BuildBanner();
        BuildRecentSearches();
        _body.Children.Add(_chipRow);
        BuildRecent();
        _body.Children.Add(_personal);
        _body.Children.Add(_imports);
        _body.Children.Add(_featured);
        _body.Children.Add(_remote);
        BuildImports();
        Unloaded += (_, _) => _cts.Cancel();
        _ = LoadAsync();
    }

    private void BuildBanner()
    {
        var grid = new Grid { Height = 210, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 0, 8) };
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
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36, 0, 0, 0), Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        left.Children.Add(new TextBlock { Text = "Musica per\nogni tuo momento", FontSize = 34, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = K.Brush("#FFFFFF") });
        left.Children.Add(K.T("Scopri nuove emozioni, ogni giorno.", 14, K.Brush("#D4D4DC")));
        var play = K.Pill("Riproduci", PlayMix, true, K.Play);
        play.HorizontalAlignment = HorizontalAlignment.Left; play.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(play);
        grid.Children.Add(left);
        var logo = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/sonara.ico")), Width = 0 };
        _body.Children.Add(grid);
    }

    private async void PlayMix()
    {
        try
        {
            if (_mix.Count == 0)
            {
                var w = Taste.Weights(_a.Store.Plays, _a.Store.History, _a.Store.Favorites);
                var seed = Taste.Seeds(w, 1).FirstOrDefault() ?? _a.Store.Favorites.FirstOrDefault();
                if (seed != null) _mix = await YtApi.RadioAsync(seed.VideoId);
                if (_mix.Count == 0) _mix = await YtApi.SearchSongsAsync("top hits", 1);
            }
            var bad = Taste.Excluder(_a.Store.Dislikes);
            var l = _mix.Where(t => !bad(t)).Select(_a.Resolve).ToList();
            if (l.Count > 0) _a.Player.PlayList(l, 0);
        }
        catch (Exception) { }
    }

    private void BuildRecentSearches()
    {
        var s = _a.Store.Searches.Take(4).ToList();
        if (s.Count == 0) return;
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        sp.Children.Add(K.T("Ricerche recenti", 13, Pal.Sub, true));
        foreach (var q in s)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(K.Icon("", 13, Pal.Sub));
            row.Children.Add(K.T(q, 14, Pal.Text));
            sp.Children.Add(K.Clickable(row, () => Nav.Go("search:" + q, () => new SearchView(q)), pad: new Thickness(6, 6, 12, 6)));
        }
        _body.Children.Add(sp);
    }

    private void BuildRecent()
    {
        var h = _a.Store.History.Take(14).ToList();
        if (h.Count > 0) _body.Children.Add(Shelves.ForTracks("Ascoltati di recente", h.Select(_a.Resolve)));
    }

    private void BuildImports()
    {
        _imports.Children.Clear();
        var imps = _a.Imports.All;
        if (imps.Count > 0)
            _imports.Children.Add(Shelves.ForEntries("Importate", imps.Select(i => i.Entry), "Importa…", () => ImportDialog.Show(XamlRoot)));
        else
            _imports.Children.Add(ImportHint());
    }

    private UIElement ImportHint()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 24, 0, 0), Spacing = 6 };
        sp.Children.Add(K.T("Importa le tue playlist", 18, Pal.Text, true));
        sp.Children.Add(K.T("Incolla il link di una playlist pubblica di Spotify, Deezer o YouTube nella barra di ricerca.", 13, Pal.Sub, 0));
        var b = K.Pill("Importa una playlist", () => ImportDialog.Show(XamlRoot), false, K.Add);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(b);
        return sp;
    }

    private async Task LoadAsync()
    {
        var ct = _cts.Token;
        var w = Taste.Weights(_a.Store.Plays, _a.Store.History, _a.Store.Favorites);
        var bad = Taste.Excluder(_a.Store.Dislikes);
        var tasks = new List<Task> { LoadPersonal(w, bad, ct), LoadFeatured(ct), LoadRemote(ct) };
        try { await Task.WhenAll(tasks); } catch (Exception) { }
    }

    private async Task LoadPersonal(Dictionary<string, (Track Item, int Weight)> w, Func<Track, bool> bad, CancellationToken ct)
    {
        try
        {
            var fav = Taste.ForgottenFavorites(_a.Store.Favorites, _a.Store.Plays, 14);
            if (fav.Count > 0) _personal.Children.Add(Shelves.ForTracks("Preferiti da riascoltare", fav.Select(_a.Resolve)));
            var r = await Feed.BuildAsync(w, bad, sh => _a.RunUi(() => { if (!ct.IsCancellationRequested) _personal.Children.Add(Shelves.Make(sh)); }), ct);
            _mix = r.Radios.FirstOrDefault() ?? new();
            foreach (var g in FavGenres.Parse(_a.Settings.FavGenres).Take(3))
            {
                var sh = await Feed.GenreShelfAsync(g, bad, ct);
                if (sh != null && !ct.IsCancellationRequested) _personal.Children.Add(Shelves.Make(sh));
            }
        }
        catch (Exception) { }
    }

    private async Task LoadFeatured(CancellationToken ct)
    {
        try
        {
            var country = _a.Settings.EffectiveCountry();
            void Show(List<Entry> l)
            {
                if (ct.IsCancellationRequested) return;
                _featured.Children.Clear();
                if (l.Count > 0) _featured.Children.Add(Shelves.ForEntries("In evidenza", l));
            }
            Show(Featured.SpotifyNow(country));
            var all = await Featured.LoadAsync(country, l => _a.RunUi(() => Show(l)), ct);
            Show(all);
        }
        catch (Exception) { }
    }

    private async Task LoadRemote(CancellationToken ct)
    {
        try
        {
            var (chips, shelves) = await YtApi.HomeFeedAsync(null, ct);
            ShowChips(chips);
            ShowShelves(shelves);
            var news = await YtApi.NewReleasesAsync(ct);
            if (news != null && !ct.IsCancellationRequested) _remote.Children.Add(Shelves.Make(news));
            var charts = await YtApi.ChartsAsync(ct);
            foreach (var c in charts) if (!ct.IsCancellationRequested) _remote.Children.Add(Shelves.Make(c));
        }
        catch (Exception) { }
    }

    private void ShowShelves(List<Shelf> shelves)
    {
        _remote.Children.Clear();
        foreach (var s in shelves) _remote.Children.Add(Shelves.Make(s));
    }

    private void ShowChips(List<Chip> chips)
    {
        _chipRow.Children.Clear();
        if (chips.Count == 0) return;
        void Rebuild()
        {
            _chipRow.Children.Clear();
            _chipRow.Children.Add(K.Chip("Tutto", _chip == null, () => Pick(null, chips)));
            foreach (var c in chips) _chipRow.Children.Add(K.Chip(c.Title, _chip == c.Params, () => Pick(c.Params, chips)));
        }
        void Pick(string? p, List<Chip> cs)
        {
            _chip = p; Rebuild();
            _ = Reload(p);
        }
        Rebuild();
    }

    private async Task Reload(string? p)
    {
        try
        {
            var (_, shelves) = await YtApi.HomeFeedAsync(p, _cts.Token);
            ShowShelves(shelves);
        }
        catch (Exception) { }
    }
}
