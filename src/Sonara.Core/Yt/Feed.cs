namespace Sonara.Core.Yt;

/// <summary>Scaffali personali della Home, costruiti dai gusti locali. Ogni sezione è indipendente. Porting di YtFeed.kt.</summary>
public static class Feed
{
    private static readonly Dictionary<string, Entry?> ArtistCache = new();

    public sealed record Result(List<Shelf> Shelves, List<List<Track>> Radios);

    public static async Task<Result> BuildAsync(Dictionary<string, Taste.Weighted> w, Func<Track, bool> bad, Action<Shelf> onShelf, CancellationToken ct = default)
    {
        var shelves = new List<Shelf>();
        var radios = new List<List<Track>>();
        void Add(Shelf s) { shelves.Add(s); onShelf(s); }
        if (w.Count == 0) return new Result(shelves, radios);
        var known = w.Keys.ToHashSet();

        // 1) «Perché hai ascoltato X»: la radio dei brani di partenza (uno per artista)
        foreach (var seed in Taste.Seeds(w, 3))
        {
            try
            {
                var r = (await YtApi.RadioAsync(seed.VideoId, ct).ConfigureAwait(false)).Where(t => !bad(t)).ToList();
                radios.Add(r);
                var cards = r.Where(t => t.VideoId != seed.VideoId && !known.Contains(t.VideoId)).Take(20).Select(t => new Card(t, null)).ToList();
                if (cards.Count > 0) Add(new Shelf($"Perché hai ascoltato «{seed.Title}»", cards));
            }
            catch (Exception) { /* questa sezione salta, le altre restano */ }
        }

        // 2) «Artisti per te» e «Artisti simili»
        try
        {
            var cards = new List<Card>();
            var mine = new List<Entry>();
            foreach (var (name, _) in Taste.TopArtists(w, 8))
            {
                var hit = await ArtistEntryAsync(name, ct).ConfigureAwait(false);
                if (hit == null) continue;
                mine.Add(hit);
                cards.Add(new Card(null, hit));
            }
            if (cards.Count > 0) Add(new Shelf("Artisti per te", cards.GroupBy(c => c.Id).Select(g => g.First()).ToList()));
            var have = mine.Select(m => m.BrowseId).ToHashSet();
            var sim = new Dictionary<string, Entry>();
            foreach (var a in mine.Take(3))
            {
                try
                {
                    foreach (var e in await YtApi.SimilarArtistsAsync(a.BrowseId, ct).ConfigureAwait(false))
                        if (!have.Contains(e.BrowseId)) sim.TryAdd(e.BrowseId, e);
                }
                catch (Exception) { }
            }
            if (sim.Count > 0) Add(new Shelf("Artisti simili", sim.Values.Take(20).Select(e => new Card(null, e)).ToList()));
        }
        catch (Exception) { }
        return new Result(shelves, radios);
    }

    /// <summary>
    /// I generi da mostrare nella Home: quelli scelti (tranne col punteggio negativo) più i 2 con più punti non scelti (almeno 3).
    /// Con pochi ascolti guidano la Home (fino a 6), con più ascolti ne bastano 2; a pari punti a rotazione ogni giorno.
    /// </summary>
    public static List<FavGenres.Genre> PickGenres(string favGenres, Dictionary<string, double> scores, int playCount, long day)
    {
        var picked = FavGenres.Parse(favGenres).Where(g => scores.GetValueOrDefault(g.Id) >= 0.0).ToList();
        var extra = FavGenres.All.Where(g => picked.All(p => p.Id != g.Id) && scores.GetValueOrDefault(g.Id) >= 3.0)
            .OrderByDescending(g => scores[g.Id]).Take(2).ToList();
        var all = picked.Concat(extra).ToList();
        var rnd = new Random((int)(day % int.MaxValue));
        return all.OrderBy(_ => rnd.Next()).OrderByDescending(g => scores.GetValueOrDefault(g.Id)).Take(playCount < 15 ? 6 : 2).ToList();
    }

    /// <summary>Le playlist di un genere scelto (scaffale «Per te · genere»).</summary>
    public static async Task<Shelf?> GenreShelfAsync(FavGenres.Genre g, CancellationToken ct = default)
    {
        try
        {
            var l = (await YtApi.SearchEntriesAsync(g.Query, Kind.Playlists, 1, null, ct).ConfigureAwait(false)).Take(12).ToList();
            return l.Count == 0 ? null : new Shelf($"Per te · {g.Label}", l.Select(e => new Card(null, e)).ToList());
        }
        catch (Exception) { return null; }
    }

    /// <summary>Brani, playlist e artisti di un genere (le chip sotto la ricerca). I brani ricordano il genere d'origine.</summary>
    public static async Task<List<Shelf>> GenreChipShelvesAsync(FavGenres.Genre g, CancellationToken ct = default)
    {
        var l = new List<Shelf>();
        try
        {
            var songs = (await YtApi.SearchSongsAsync(g.Query, 1, null, ct).ConfigureAwait(false)).Take(15).ToList();
            Genres.NoteOrigin(songs, g.Id);
            if (songs.Count > 0) l.Add(new Shelf($"Brani · {g.Label}", songs.Select(t => new Card(t, null)).ToList()));
        }
        catch (Exception) { }
        try
        {
            var pl = (await YtApi.SearchEntriesAsync(g.Query, Kind.Playlists, 1, null, ct).ConfigureAwait(false)).Take(12).ToList();
            if (pl.Count > 0) l.Add(new Shelf($"Playlist · {g.Label}", pl.Select(e => new Card(null, e)).ToList()));
        }
        catch (Exception) { }
        try
        {
            var ar = (await YtApi.SearchEntriesAsync(g.Label, Kind.Artists, 1, null, ct).ConfigureAwait(false)).Take(12).ToList();
            if (ar.Count > 0) l.Add(new Shelf($"Artisti · {g.Label}", ar.Select(e => new Card(null, e)).ToList()));
        }
        catch (Exception) { }
        return l;
    }

    /// <summary>«I tuoi artisti» (scelti) e «Simili ai tuoi artisti» (dalle pagine di 4 di loro, senza i tuoi).</summary>
    public static async Task<List<Shelf>> FavArtistShelvesAsync(IReadOnlyList<Entry> mine, Action<Shelf>? onShelf = null, CancellationToken ct = default)
    {
        var res = new List<Shelf>();
        if (mine.Count == 0) return res;
        var first = new Shelf("I tuoi artisti", mine.Select(e => new Card(null, e)).ToList());
        res.Add(first); onShelf?.Invoke(first);
        var have = mine.Select(m => m.BrowseId).ToHashSet();
        var sim = new Dictionary<string, Entry>();
        var rnd = new Random();
        foreach (var a in mine.OrderBy(_ => rnd.Next()).Take(4))
        {
            try { foreach (var e in await YtApi.SimilarArtistsAsync(a.BrowseId, ct).ConfigureAwait(false)) if (!have.Contains(e.BrowseId)) sim.TryAdd(e.BrowseId, e); }
            catch (Exception) { }
        }
        if (sim.Count > 0)
        {
            var s2 = new Shelf("Simili ai tuoi artisti", sim.Values.Take(20).Select(e => new Card(null, e)).ToList());
            res.Add(s2); onShelf?.Invoke(s2);
        }
        return res;
    }

    private static async Task<Entry?> ArtistEntryAsync(string name, CancellationToken ct)
    {
        var key = Taste.Norm(name);
        lock (ArtistCache) { if (ArtistCache.TryGetValue(key, out var c)) return c; }
        Entry? hit = null;
        try
        {
            var r = await YtApi.SearchEntriesAsync(name, Kind.Artists, 1, null, ct).ConfigureAwait(false);
            hit = r.FirstOrDefault(e => Taste.Norm(e.Title) == key);
        }
        catch (Exception) { return null; }
        lock (ArtistCache) ArtistCache[key] = hit;
        return hit;
    }
}
