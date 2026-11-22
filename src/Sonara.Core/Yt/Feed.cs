namespace Sonara.Core.Yt;

/// <summary>Scaffali personali della Home, costruiti dai gusti locali. Ogni sezione è indipendente. Porting di YtFeed.kt.</summary>
public static class Feed
{
    private static readonly Dictionary<string, Entry?> ArtistCache = new();

    public sealed record Result(List<Shelf> Shelves, List<List<Track>> Radios);

    public static async Task<Result> BuildAsync(Dictionary<string, (Track Item, int Weight)> w, Func<Track, bool> bad, Action<Shelf> onShelf, CancellationToken ct = default)
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

    /// <summary>I brani di un genere scelto (scaffale «Per te: genere»).</summary>
    public static async Task<Shelf?> GenreShelfAsync(FavGenres.Genre g, Func<Track, bool> bad, CancellationToken ct = default)
    {
        try
        {
            var r = (await YtApi.SearchSongsAsync(g.Query, 1, null, ct).ConfigureAwait(false)).Where(t => !bad(t)).Take(20).ToList();
            return r.Count == 0 ? null : new Shelf(g.Label, r.Select(t => new Card(t, null)).ToList());
        }
        catch (Exception) { return null; }
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
