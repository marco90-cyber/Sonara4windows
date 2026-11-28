using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.Core;
using Sonara.Core.Data;
using Sonara.Core.Yt;

namespace Sonara.App.Ui;

/// <summary>Scelta di generi e artisti preferiti, uguale in Benvenuto e Impostazioni.</summary>
public static class Pickers
{
    /// <summary>Le «pastiglie» dei generi (a righe); ogni tocco cambia la scelta e la salva.</summary>
    public static UIElement Genres(AppSettings s, bool save, int perRow = 4)
    {
        var wrap = new StackPanel { Spacing = 8 };
        void Build()
        {
            wrap.Children.Clear();
            var all = FavGenres.All;
            for (var i = 0; i < all.Count; i += perRow)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var g in all.Skip(i).Take(perRow))
                {
                    var id = g.Id;
                    row.Children.Add(K.Chip(g.Label, FavGenres.Parse(s.FavGenres).Any(x => x.Id == id), () =>
                    {
                        s.FavGenres = FavGenres.Toggle(s.FavGenres, id);
                        if (save) s.Save();
                        Build();
                    }));
                }
                wrap.Children.Add(row);
            }
        }
        Build();
        return wrap;
    }

    /// <summary>Le pastiglie dei generi che vanno a capo da sole (per il Generator).</summary>
    public static UIElement GenreChips(IEnumerable<FavGenres.Genre> all, Func<FavGenres.Genre, bool> isOn, Action<FavGenres.Genre> click) =>
        K.Wrap(all.Select(g => (UIElement)K.Chip(g.Label, isOn(g), () => click(g))));

    /// <summary>
    /// Scelta degli artisti preferiti: quelli proposti seguono i generi scelti; man mano che ne scegli compaiono i loro simili;
    /// in alternativa si cerca per nome. La scelta finisce anche tra i salvati della Raccolta.
    /// </summary>
    public static UIElement Artists(AppSettings s, bool save)
    {
        var root = new StackPanel { Spacing = 10, MinWidth = 420 };
        var body = new StackPanel { Spacing = 10 };
        var suggested = new List<Entry>();
        var similar = new Dictionary<string, List<Entry>>();
        List<Entry>? found = null;
        var loading = false;
        var searching = false;

        void Toggle(Entry e)
        {
            var had = s.FavArtists.Any(x => x.BrowseId == e.BrowseId);
            if (had) s.FavArtists.RemoveAll(x => x.BrowseId == e.BrowseId);
            else { s.FavArtists.Add(e); AppServices.I.Store.AddSaved(e); }
            if (save) s.Save();
            Render();
            if (!had) _ = LoadSimilar();
        }

        UIElement Tile(Entry e)
        {
            var on = s.FavArtists.Any(x => x.BrowseId == e.BrowseId);
            var pic = new Grid { Width = 76, Height = 76, HorizontalAlignment = HorizontalAlignment.Center };
            pic.Children.Add(K.Cover(e.Thumb, 76, 38, true));
            if (on)
            {
                var ov = new Border { CornerRadius = new CornerRadius(38), Background = Pal.Accent, Opacity = 0.7 };
                pic.Children.Add(ov);
                pic.Children.Add(K.Icon(K.Check, 26, Pal.OnAccent));
            }
            var sp = new StackPanel { Width = 92, Spacing = 4 };
            sp.Children.Add(pic);
            var t = K.T(e.Title, 12, Pal.Text, false, 2); t.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center; t.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(t);
            return K.Clickable(sp, () => Toggle(e), radius: 12, pad: new Thickness(2, 4, 2, 4));
        }

        UIElement Section(string title, IEnumerable<Entry> list)
        {
            var sp = new StackPanel { Spacing = 6 };
            sp.Children.Add(K.T(title, 13, Pal.Sub, true));
            sp.Children.Add(K.Wrap(list.Select(Tile), 6, 6));
            return sp;
        }

        void Render()
        {
            body.Children.Clear();
            var chosen = s.FavArtists.ToList();
            if (chosen.Count > 0) body.Children.Add(Section("Scelti", chosen));
            // prima i simili agli ultimi scelti (a turno), poi quelli dei generi
            var lists = chosen.AsEnumerable().Reverse().Where(c => similar.ContainsKey(c.BrowseId)).Select(c => similar[c.BrowseId]).ToList();
            var fromChosen = Interleave(lists);
            var shown = (found ?? fromChosen.Concat(suggested).ToList()).Where(e => chosen.All(c => c.BrowseId != e.BrowseId))
                .GroupBy(e => e.BrowseId).Select(g => g.First()).Take(30).ToList();
            if (searching || (found == null && loading && shown.Count == 0)) body.Children.Add(K.Spinner());
            else if (shown.Count > 0)
                body.Children.Add(Section(found != null ? "Risultati" : fromChosen.Count > 0 ? "Simili ai tuoi artisti e proposti per te" : "Proposti per te", shown));
            else if (found != null) body.Children.Add(K.T("Nessun artista trovato", 13, Pal.Sub));
            else if (chosen.Count == 0)
                body.Children.Add(K.T(string.IsNullOrWhiteSpace(s.FavGenres)
                    ? "Scrivi il nome di un artista per aggiungerlo. Se scegli dei generi, qui compaiono artisti da cui partire."
                    : "Non riesco a proporre artisti adesso: cerca per nome.", 13, Pal.Sub, false, 0));
        }

        async Task LoadSuggested()
        {
            var genres = FavGenres.Parse(s.FavGenres).Take(6).ToList();
            if (genres.Count == 0) { suggested = new(); Render(); return; }
            loading = true; Render();
            var tasks = genres.Select(async g =>
            {
                try { return (await YtApi.SearchEntriesAsync(g.Label, Kind.Artists, 1)).Take(20).ToList(); } catch (Exception) { return new List<Entry>(); }
            }).ToList();
            var lists = (await Task.WhenAll(tasks)).ToList();
            suggested = Interleave(lists).GroupBy(e => e.BrowseId).Select(g => g.First()).Take(60).ToList();
            loading = false; Render();
        }

        async Task LoadSimilar()
        {
            var todo = s.FavArtists.Select(a => a.BrowseId).Where(id => !similar.ContainsKey(id)).ToList();
            if (todo.Count == 0) return;
            foreach (var id in todo) similar[id] = new();   // segnato subito: niente richieste doppie
            foreach (var id in todo)
            {
                try { similar[id] = await YtApi.SimilarArtistsAsync(id); } catch (Exception) { }
            }
            Render();
        }

        var box = new AutoSuggestBox { PlaceholderText = "Cerca un artista (Invio)", MinWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
        box.TextChanged += (b, e) => { if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput && string.IsNullOrWhiteSpace(b.Text)) { found = null; Render(); } };
        box.QuerySubmitted += async (b, _) =>
        {
            var q = b.Text.Trim();
            if (q.Length == 0) { found = null; Render(); return; }
            searching = true; Render();
            try { found = (await YtApi.SearchEntriesAsync(q, Kind.Artists, 1)).Take(12).ToList(); } catch (Exception) { found = new(); }
            searching = false; Render();
        };
        root.Children.Add(box);
        root.Children.Add(body);
        Render();
        _ = LoadSuggested();
        _ = LoadSimilar();
        return root;
    }

    private static List<T> Interleave<T>(IReadOnlyList<List<T>> lists)
    {
        var o = new List<T>();
        for (var i = 0; lists.Any(l => i < l.Count); i++)
            foreach (var l in lists) if (i < l.Count) o.Add(l[i]);
        return o;
    }
}
