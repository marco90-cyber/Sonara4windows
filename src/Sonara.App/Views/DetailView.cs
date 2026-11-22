using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Data;
using Sonara.Core.Yt;

namespace Sonara.App.Views;

/// <summary>Pagina di una playlist, un album, un artista, una categoria o una playlist importata.</summary>
public sealed class DetailView : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private Entry _e;
    private readonly Grid _root = new();
    private List<Track> _tracks = new();
    private CancellationTokenSource _cts = new();

    private readonly Grid _wait = new() { Children = { K.Spinner(40) }, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 80, 0, 0) };

    public DetailView(Entry e)
    {
        _e = e;
        Content = _root;
        _root.Children.Add(_wait);
        Unloaded += (_, _) => _cts.Cancel();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var ct = _cts.Token;
        try
        {
            var imp = _a.Imports.Find(_e.BrowseId);
            Detail d;
            if (imp != null) d = new Detail(imp.Tracks, new());
            else if (_e.BrowseId.StartsWith("cat:")) d = await YtApi.CategoryAsync(_e.BrowseId, ct);
            else if (_e.Kind == Kind.Artists) d = await YtApi.ArtistAsync(_e.BrowseId, ct);
            else if (PlaylistImport.RefOf(_e.BrowseId) is { } r && !_e.BrowseId.StartsWith("imp:"))
            {
                var saved = _a.Imports.Find(r.ImportId);
                if (saved == null)
                {
                    var prog = new ImportProgress { HorizontalAlignment = HorizontalAlignment.Center };
                    _wait.Children.Clear(); _wait.Children.Add(prog);
                    // aprire una playlist di un altro servizio la abbina e la salva: la volta dopo è già pronta
                    saved = await _a.Runner.ImportPlaylistAsync(r, (meta, found, dn, tt) => _a.RunUi(() => prog.Set(meta.Name, dn, tt)));
                }
                _e = saved.Entry;
                d = new Detail(saved.Tracks, new());
            }
            else
            {
                var l = await YtApi.PlaylistTracksAsync(_e.BrowseId, ct);
                d = new Detail(l.Select(t => t.Thumb.Length == 0 && _e.Thumb.Length > 0 ? t with { Thumb = _e.Thumb } : t).ToList(), new());
            }
            if (ct.IsCancellationRequested) return;
            Show(d);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _root.Children.Clear();
            _root.Children.Add(new TextBlock { Text = "Non riesco ad aprire questa pagina: " + ex.Message, Foreground = Pal.Sub, Margin = new Thickness(28), TextWrapping = TextWrapping.Wrap });
        }
    }

    private void Show(Detail d)
    {
        _tracks = d.Tracks.Select(_a.Resolve).ToList();
        _root.Children.Clear();
        var header = BuildHeader();
        var footer = new StackPanel { Padding = new Thickness(28, 0, 28, 40) };
        foreach (var sh in d.Shelves) footer.Children.Add(Shelves.Make(sh));
        if (_tracks.Count == 0)
        {
            var sp = new StackPanel();
            sp.Children.Add(header);
            sp.Children.Add(footer);
            _root.Children.Add(new ScrollViewer { Content = sp });
            return;
        }
        var list = new TrackList(_tracks, header) { Footer = footer };
        _root.Children.Add(list);
    }

    private UIElement BuildHeader()
    {
        var round = _e.Kind == Kind.Artists;
        var g = new Grid { Margin = new Thickness(28, 20, 28, 12), ColumnSpacing = 24 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(K.Cover(_e.Thumb, 200, 14, round));
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 8 };
        var kindLabel = _a.Imports.Find(_e.BrowseId) != null ? "PLAYLIST IMPORTATA" : _e.Kind switch { Kind.Artists => "ARTISTA", Kind.Albums => "ALBUM", _ => "PLAYLIST" };
        right.Children.Add(K.T(kindLabel, 12, Pal.Sub, true));
        right.Children.Add(K.T(_e.Title, 36, Pal.Text, true, 2));
        var sub = _e.Subtitle;
        if (_tracks.Count > 0) sub = (sub.Length > 0 ? sub + " · " : "") + $"{_tracks.Count} brani";
        if (sub.Length > 0) right.Children.Add(K.T(sub, 13, Pal.Sub, false, 0));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };
        if (_tracks.Count > 0)
        {
            row.Children.Add(K.Pill("Riproduci", () => _a.Player.PlayList(_tracks, 0), true, K.Play));
            row.Children.Add(K.Pill("Controlla durate", () => DurationDialog.ShowAsync(_tracks, XamlRoot), false, K.Timer));
            row.Children.Add(K.Pill("Casuale", () => _a.Player.PlayList(_tracks, 0, true), false, K.Shuffle));
        }
        var saved = _a.Store.IsSaved(_e.BrowseId);
        if (!_e.BrowseId.StartsWith("cat:"))
        {
            var bm = K.IconBtn(saved ? K.BookmarkFill : K.Bookmark, () => { }, 18, "Salva nella libreria", saved ? Pal.Accent : Pal.Text, 40);
            bm.Tapped += (_, _) => { var now = _a.Store.ToggleSaved(_e); ((FontIcon)bm.Child).Glyph = now ? K.BookmarkFill : K.Bookmark; ((FontIcon)bm.Child).Foreground = now ? Pal.Accent : Pal.Text; };
            row.Children.Add(bm);
        }
        if (_tracks.Count > 0)
            row.Children.Add(K.IconBtn(K.Download, () => Actions.Download(_e.Title, _tracks, XamlRoot), 18, "Scarica", null, 40));
        var imp = _a.Imports.Find(_e.BrowseId);
        if (imp != null)
        {
            row.Children.Add(K.IconBtn(K.Refresh, async () =>
            {
                try
                {
                    var ch = await _a.Runner.RefreshPlaylistAsync(imp);
                    await K.Info(XamlRoot, "Playlist aggiornata", ch.Any ? $"{ch.Added} brani nuovi, {ch.Removed} tolti{(ch.Reordered ? ", ordine cambiato" : "")}." : "Nessun cambiamento.");
                    Nav.Refresh();
                }
                catch (Exception ex) { await K.Info(XamlRoot, "Aggiornamento non riuscito", ex.Message); }
            }, 18, "Aggiorna dall'originale", null, 40));
            row.Children.Add(K.IconBtn(K.Delete, async () =>
            {
                if (await K.Confirm(XamlRoot, "Rimuovere la playlist?", "La playlist importata sparisce dalla Home e dalla libreria.", "Rimuovi")) { _a.Imports.Remove(_e.BrowseId); Nav.Back(); }
            }, 18, "Rimuovi", null, 40));
        }
        else if (PlaylistImport.RefOf(_e.BrowseId) is { } rf && !_e.BrowseId.StartsWith("imp:"))
            row.Children.Add(K.Pill("Importa nella Home", () => ImportDialog.Start(rf, XamlRoot), false, K.Add));
        if (_e.Kind == Kind.Artists)
            row.Children.Add(K.IconBtn(K.Radio, async () =>
            {
                try { var r = await YtApi.RadioAsync(_tracks.FirstOrDefault()?.VideoId ?? ""); if (r.Count > 0) _a.Player.PlayList(r, 0); } catch (Exception) { }
            }, 18, "Radio", null, 40));
        right.Children.Add(row);
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }
}
