using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Data;
using Sonara.Core.Yt;

namespace Sonara.App.Views;

public sealed class SearchView : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private readonly string _q;
    private Kind _kind = Kind.Songs;
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(28, 12, 28, 8) };
    private readonly Border _area = new();
    private readonly Grid _root = new();
    private CancellationTokenSource? _cts;

    public SearchView(string query)
    {
        _q = query.Trim();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition());
        _root.Children.Add(_chips);
        Grid.SetRow(_area, 1);
        _root.Children.Add(_area);
        Content = _root;
        Unloaded += (_, _) => _cts?.Cancel();
        if (PlaylistImport.MaybeLink(_q)) { _ = ShowLink(); return; }
        _a.Store.AddSearch(_q);
        BuildChips();
        _ = LoadAsync();
    }

    private void BuildChips()
    {
        _chips.Children.Clear();
        foreach (var k in new[] { Kind.Songs, Kind.Artists, Kind.Albums, Kind.Playlists })
        {
            var kk = k;
            _chips.Children.Add(K.Chip(k.Label(), _kind == k, () => { if (_kind != kk) { _kind = kk; BuildChips(); _ = LoadAsync(); } }));
        }
    }

    private async Task ShowLink()
    {
        _area.Child = K.Spinner();
        var r = await PlaylistImport.ParseResolvingAsync(_q);
        var sp = new StackPanel { Margin = new Thickness(28), Spacing = 12 };
        if (r == null) sp.Children.Add(K.T("Link non riconosciuto. Incolla il link di una playlist di Spotify, Deezer o YouTube.", 14, Pal.Sub, false, 0));
        else
        {
            sp.Children.Add(K.T($"Playlist di {r.Source.Label()}", 22, Pal.Text, true));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var saved = AppServices.I.Imports.Find(r.ImportId);
            if (saved != null)
            {
                sp.Children.Add(K.T("Già importata e abbinata.", 13, Pal.Sub));
                row.Children.Add(K.Pill("Apri", () => Actions.OpenEntry(saved.Entry), true, K.Play));
            }
            else
            {
                row.Children.Add(K.Pill("Apri", () => Actions.OpenEntry(r.Placeholder()), false, K.Play));
                row.Children.Add(K.Pill("Importa nella Home", () => ImportDialog.Start(r, XamlRoot), true, K.Download));
            }
            sp.Children.Add(row);
        }
        _area.Child = sp;
    }

    private async Task LoadAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _area.Child = new Grid { Children = { K.Spinner() }, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 40, 0, 0) };
        try
        {
            if (_kind == Kind.Songs)
            {
                var bad = Taste.Excluder(_a.Store.Dislikes);
                var list = await YtApi.SearchSongsAsync(_q, 5, null, ct);
                if (ct.IsCancellationRequested) return;
                var res = list.Select(_a.Resolve).ToList();
                var head = new StackPanel { Margin = new Thickness(28, 4, 28, 8) };
                head.Children.Add(K.T($"{res.Count} brani per «{_q}»", 13, Pal.Sub));
                _area.Child = res.Count == 0 ? Empty("Nessun brano trovato") : new TrackList(res, head);
            }
            else
            {
                var list = await YtApi.SearchEntriesAsync(_q, _kind, 3, null, ct);
                var extra = new List<Entry>();
                if (_kind == Kind.Playlists)
                    extra = await PlaylistImport.SearchDeezerAsync(_q, ct);
                if (ct.IsCancellationRequested) return;
                var all = list.Concat(extra).ToList();
                _area.Child = all.Count == 0 ? Empty("Nessun risultato") : EntryList(all);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!ct.IsCancellationRequested) _area.Child = Empty("Ricerca non riuscita: " + e.Message); }
    }

    private static UIElement Empty(string text) => new Grid { Children = { K.T(text, 14, Pal.Sub) }, Margin = new Thickness(28), VerticalAlignment = VerticalAlignment.Top };

    private static UIElement EntryList(List<Entry> list)
    {
        var sp = new StackPanel { Padding = new Thickness(20, 0, 20, 30) };
        foreach (var e in list)
        {
            var row = new Grid { ColumnSpacing = 14, Height = 68, Padding = new Thickness(8, 0, 8, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(K.Cover(e.Thumb, 52, 8, e.Kind == Kind.Artists));
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(K.T(e.Title, 14, Pal.Text, true));
            t.Children.Add(K.T(e.Subtitle, 12, Pal.Sub));
            Grid.SetColumn(t, 1); row.Children.Add(t);
            sp.Children.Add(K.Clickable(row, () => Actions.OpenEntry(e), radius: 10));
        }
        return new ScrollViewer { Content = sp };
    }
}
