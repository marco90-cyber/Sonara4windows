using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Yt;
using static Sonara.Core.Yt.Generator;

namespace Sonara.App.Views;

/// <summary>Generatore di playlist: scegli una modalità, le sue opzioni (diverse per ognuna) e genera. Poi riproduci o salva come playlist.</summary>
public sealed class GeneratorView : UserControl
{
    private const int SuggestionsCount = 35;

    private readonly AppServices _a = AppServices.I;
    private readonly CancellationTokenSource _cts = new();
    private readonly ScrollViewer _sv = new();

    private Mode? _mode;
    private Period _period = Period.All;
    private int _count = 26;
    private Track? _seed;
    private string _seedQuery = "";
    private List<Track> _seedHits = new();
    private string _artist = "";
    private string _tag = "";
    private FavGenres.Genre? _genre;
    private bool _busy;
    private string _progress = "";
    private string? _error;
    private (Mode Mode, List<Track> Tracks)? _result;

    private static (string Label, string Sub, string Glyph) Info(Mode m) => m switch
    {
        Mode.Top => ("Brani top", "I brani che hai ascoltato di più", K.ThumbUp),
        Mode.Recent => ("Brani recenti", "Quello che hai ascoltato di recente", K.History),
        Mode.Radio => ("Radio brano", "Radio di YouTube Music a partire da un brano", K.Music),
        Mode.SimilarArtists => ("Artisti simili", "Scopri artisti, prima YouTube", K.Person),
        Mode.Genre => ("Per tag / genere", "Scelte di genere, prima YouTube", K.Bookmark),
        Mode.MyMix => ("Il mio mix", "I tuoi gusti, i mix e i preferiti", K.Shuffle),
        Mode.Suggestions => ("I miei consigli", $"{SuggestionsCount} scoperte, prima YouTube", K.Radio),
        Mode.NeverHeard => ("Mai sentiti", "Scoperte nuove, brani che non hai mai ascoltato", K.Compass),
        _ => ("La mia libreria", "Riscopri i suoni del tuo passato", K.Library),
    };

    public GeneratorView()
    {
        Content = _sv;
        Unloaded += (_, _) => _cts.Cancel();
        Render();
    }

    private void Render()
    {
        if (_result != null) { RenderResult(); return; }
        Content = _sv;
        var root = new StackPanel { Padding = new Thickness(40, 24, 40, 80), Spacing = 8, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(K.T("Generator", 28, Pal.Text, true));
        root.Children.Add(K.T("Scegli una modalità per generare una playlist", 14, Pal.Sub));
        root.Children.Add(new Border { Height = 8 });
        foreach (var m in Enum.GetValues<Mode>())
        {
            var mm = m;
            var (label, sub, glyph) = Info(m);
            var on = _mode == m;
            var ic = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Background = on ? Pal.Accent : Pal.Hover, Child = K.Icon(glyph, 18, on ? Pal.OnAccent : Pal.Text) };
            var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(K.T(label, 15, Pal.Text, true));
            texts.Children.Add(K.T(sub, 12, Pal.Sub));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            row.Children.Add(ic); row.Children.Add(texts);
            root.Children.Add(K.Clickable(row, () =>
            {
                if (_busy) return;
                _mode = mm; _error = null;
                if (mm == Mode.Radio && _seed == null) _seed = _a.Player.Current;
                Render();
            }, on ? Pal.Card : null, Pal.Hover, 14, new Thickness(10)));
        }
        if (_mode is Mode m2) root.Children.Add(Options(m2));
        _sv.Content = root;
    }

    private UIElement Options(Mode m)
    {
        var box = new StackPanel { Spacing = 10, Padding = new Thickness(20) };
        box.Children.Add(K.T("Opzioni", 18, Pal.Text, true));
        var st = _a.Store;
        var w = Taste.Weights(st.Plays, st.History, st.Favorites, st.Skips);
        switch (m)
        {
            case Mode.Top:
            case Mode.Library:
                box.Children.Add(K.T("Periodo", 13, Pal.Sub, true));
                var pr = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var p in Enum.GetValues<Period>()) { var pp = p; pr.Children.Add(K.Chip(Generator.Info(p).Label, _period == p, () => { _period = pp; Render(); })); }
                box.Children.Add(new ScrollViewer { Content = pr, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
                break;
            case Mode.Recent: box.Children.Add(K.T("Gli ultimi brani che hai ascoltato, dal più recente.", 13, Pal.Sub, false, 0)); break;
            case Mode.Radio: box.Children.Add(SeedPicker(w)); break;
            case Mode.SimilarArtists: box.Children.Add(ArtistPicker(w)); break;
            case Mode.Genre: box.Children.Add(GenrePicker()); break;
            case Mode.MyMix: box.Children.Add(K.T("Un mix dei tuoi brani più ascoltati, dei preferiti che non senti da tempo e delle radio dei tuoi artisti.", 13, Pal.Sub, false, 0)); break;
            case Mode.Suggestions: box.Children.Add(K.T($"{SuggestionsCount} scoperte dalle radio dei tuoi brani, senza quelli che conosci già.", 13, Pal.Sub, false, 0)); break;
            default: box.Children.Add(K.T("Solo brani che non hai mai ascoltato, al massimo due per artista.", 13, Pal.Sub, false, 0)); break;
        }
        if (m == Mode.Suggestions) box.Children.Add(K.T($"Numero di brani: {SuggestionsCount} (fisso)", 13, Pal.Text, true));
        else
        {
            var lab = K.T($"Numero di brani: {_count}", 13, Pal.Text, true);
            var sl = K.Sld(10, 60, _count);
            sl.ValueChanged += (_, e) => { _count = (int)e.NewValue; lab.Text = $"Numero di brani: {_count}"; };
            box.Children.Add(lab); box.Children.Add(sl);
        }
        var can = !_busy && m switch
        {
            Mode.Radio => _seed != null,
            Mode.SimilarArtists => _artist.Trim().Length > 0,
            Mode.Genre => _genre != null || _tag.Trim().Length > 0,
            _ => true,
        };
        var go = K.Pill(_busy ? (_progress.Length > 0 ? _progress : "Genero…") : m == Mode.Radio ? "Crea radio" : "Genera playlist", () => { if (can) Generate(m); }, true, _busy ? null : K.Refresh, 46);
        go.Opacity = can || _busy ? 1 : 0.4;
        box.Children.Add(go);
        if (_error != null) box.Children.Add(K.T(_error, 13, K.Brush("#FF6B6B"), false, 0));
        return new Border { Background = Pal.Card, CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 10, 0, 0), Child = box };
    }

    private UIElement SeedPicker(Dictionary<string, Taste.Weighted> w)
    {
        var s = new StackPanel { Spacing = 8 };
        s.Children.Add(K.T("Brano di partenza", 13, Pal.Sub, true));
        var tb = new TextBox { Text = _seedQuery, PlaceholderText = "Titolo e artista (Invio per cercare)", MinWidth = 320 };
        tb.TextChanged += (_, _) => _seedQuery = tb.Text;
        tb.KeyDown += async (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter || _seedQuery.Trim().Length == 0) return;
            try { _seedHits = (await YtApi.SearchSongsAsync(_seedQuery, 1)).Take(6).ToList(); } catch (Exception) { _seedHits = new(); }
            Render();
        };
        s.Children.Add(tb);
        var mine = new List<Track>();
        if (_a.Player.Current is Track cur) mine.Add(cur);
        mine.AddRange(Taste.TopTracks(w, 8));
        var choices = _seedHits.Concat(mine).GroupBy(t => t.VideoId).Select(g => g.First()).ToList();
        var wrap = new StackPanel { Spacing = 6 };
        foreach (var t in choices)
        {
            var tt = t;
            wrap.Children.Add(K.Chip($"{t.Title} – {t.Artist}", _seed?.VideoId == t.VideoId, () => { _seed = tt; Render(); }));
        }
        s.Children.Add(wrap);
        return s;
    }

    private UIElement ArtistPicker(Dictionary<string, Taste.Weighted> w)
    {
        var s = new StackPanel { Spacing = 8 };
        s.Children.Add(K.T("Artista di partenza", 13, Pal.Sub, true));
        var tb = new TextBox { Text = _artist, PlaceholderText = "Nome dell'artista", MinWidth = 320 };
        tb.TextChanged += (_, _) => _artist = tb.Text;
        s.Children.Add(tb);
        var top = Taste.TopArtists(w, 10).Select(a => a.Name).ToList();
        if (top.Count > 0)
        {
            s.Children.Add(K.T("I tuoi artisti più ascoltati", 12, Pal.Sub));
            var wrap = new StackPanel { Spacing = 6 };
            foreach (var a in top) { var aa = a; wrap.Children.Add(K.Chip(a, _artist == a, () => { _artist = aa; Render(); })); }
            s.Children.Add(wrap);
        }
        return s;
    }

    private UIElement GenrePicker()
    {
        var s = new StackPanel { Spacing = 8 };
        s.Children.Add(K.T("Tag o genere", 13, Pal.Sub, true));
        var tb = new TextBox { Text = _tag, PlaceholderText = "es. indie, lofi, anni 90", MinWidth = 320 };
        tb.TextChanged += (_, _) => { if (_genre != null && tb.Text != _genre.Label) _genre = null; _tag = tb.Text; };
        s.Children.Add(tb);
        s.Children.Add(Pickers.GenreChips(FavGenres.All, g => _genre?.Id == g.Id, g => { _genre = g; _tag = g.Label; Render(); }));
        return s;
    }

    private async void Generate(Mode m)
    {
        _busy = true; _error = null; _progress = "";
        Render();
        var st = _a.Store;
        var genre = _genre ?? (_tag.Trim().Length > 0 ? new FavGenres.Genre("custom", _tag.Trim(), _tag.Trim()) : null);
        var input = new Input(st.Plays, st.History.ToList(), st.Favorites.ToList(), st.Skips, st.Dislikes.ToList(),
            _seed, _artist.Trim().Length > 0 ? _artist.Trim() : null, genre, _period, m == Mode.Suggestions ? SuggestionsCount : _count);
        List<Track>? res = null;
        try { res = await Task.Run(() => GenerateAsync(m, input, p => _a.RunUi(() => { _progress = p; if (!_cts.IsCancellationRequested) Render(); }), _cts.Token)); }
        catch (Exception) { }
        if (_cts.IsCancellationRequested) return;
        _busy = false;
        if (res == null || res.Count == 0) _error = "Non ho trovato brani per questa modalità. Riprova o cambia opzioni.";
        else _result = (m, res);
        Render();
    }

    private void RenderResult()
    {
        var (m, tracks) = _result!.Value;
        var head = new StackPanel { Spacing = 8, Padding = new Thickness(40, 24, 40, 8) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        top.Children.Add(K.IconBtn(K.Back, () => { _result = null; Render(); }, 16, "Indietro"));
        var t = new StackPanel();
        t.Children.Add(K.T(Info(m).Label, 24, Pal.Text, true));
        t.Children.Add(K.T($"{tracks.Count} brani", 13, Pal.Sub));
        top.Children.Add(t);
        head.Children.Add(top);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        btns.Children.Add(K.Pill("Riproduci", () => _a.Player.PlayList(tracks, 0), true, K.Play));
        btns.Children.Add(K.Pill("Casuale", () => _a.Player.PlayList(tracks, 0, true), false, K.Shuffle));
        btns.Children.Add(K.Pill("Salva come playlist", async () =>
        {
            var name = await MyPlaylists.PromptName(XamlRoot, "Salva come playlist", $"{Info(m).Label} · {DateTime.Now:d MMM}");
            if (name == null) return;
            var p = _a.Store.CreatePlaylist(string.IsNullOrWhiteSpace(name) ? Info(m).Label : name);
            foreach (var tr in tracks) _a.Store.AddToPlaylist(p.Id, tr);
            await K.Info(XamlRoot, "Playlist creata", $"«{p.Name}»: {tracks.Count} brani.");
        }, false, K.Add));
        head.Children.Add(btns);
        Content = new TrackList(tracks, head);
    }

}
