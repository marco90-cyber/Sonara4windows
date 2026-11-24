using Sonara.Core.Data;

namespace Sonara.Core.Yt;

/// <summary>Un mix pronto da ascoltare (Mix 1, 2, 3, Scopri settimanale).</summary>
public sealed record ForYouMix(string Title, string Subtitle, List<Track> Tracks);

/// <summary>Mix quotidiani, «Scopri settimanale» e «Novità per te», costruiti dai gusti locali. Porting della Home di Android.</summary>
public static class ForYou
{
    private sealed record Pool(string Name, List<Track> Tracks);

    public sealed record Mixes(List<Track> Daily, List<ForYouMix> Numbered);

    /// <summary>
    /// Il mix quotidiano: radio dai tuoi ascolti, brani degli artisti preferiti e dei generi scelti, a turno e senza doppioni;
    /// uguale per tutta la giornata. Se i gruppi sono abbastanza, nascono anche Mix 1, 2, 3 con artisti e generi diversi.
    /// </summary>
    public static async Task<Mixes> BuildMixesAsync(Taste.Profile prof, string favGenres, IReadOnlyList<Entry> favArtists, IReadOnlyList<List<Track>> radios, long day, CancellationToken ct = default)
    {
        var rnd = new Random((int)(day % int.MaxValue));
        List<T> Shuffle<T>(IEnumerable<T> l) => l.OrderBy(_ => rnd.Next()).ToList();
        var genres = Shuffle(FavGenres.Parse(favGenres)).Take(3).ToList();
        var artists = Shuffle(favArtists).Take(4).ToList();
        var pools = new List<Pool>();
        foreach (var r in radios)
        {
            var label = r.FirstOrDefault()?.Artist is { } a ? Taste.ArtistNames(a).FirstOrDefault() ?? a : "";
            pools.Add(new Pool(label, Shuffle(r).Take(20).ToList()));
        }
        foreach (var a in artists)
        {
            try { pools.Add(new Pool(a.Title, (await YtApi.ArtistAsync(a.BrowseId, ct).ConfigureAwait(false)).Tracks.Take(15).ToList())); }
            catch (Exception) { }
        }
        foreach (var g in genres)
        {
            try
            {
                var l = (await YtApi.SearchSongsAsync(g.Query, 1, null, ct).ConfigureAwait(false)).Take(15).ToList();
                Genres.NoteOrigin(l, g.Id);
                pools.Add(new Pool(g.Label, l));
            }
            catch (Exception) { }
        }
        pools = pools.Where(p => p.Tracks.Count > 0).ToList();
        var daily = Blend(pools, prof.Bad, 40);
        var k = pools.Count >= 6 ? 3 : pools.Count >= 4 ? 2 : 1;
        var numbered = new List<ForYouMix>();
        if (k >= 2)
            for (var j = 0; j < k; j++)
            {
                var ps = pools.Where((_, idx) => idx % k == j).ToList();
                var tracks = Blend(ps, prof.Bad, 30);
                if (tracks.Count == 0) continue;
                var names = ps.Select(p => p.Name).Where(n => n.Trim().Length > 0).Distinct().ToList();
                numbered.Add(new ForYouMix($"Mix {j + 1}", string.Join(", ", names.Take(3)) + (names.Count > 3 ? " e altri" : ""), tracks));
            }
        return new Mixes(daily, numbered);
    }

    private static List<Track> Blend(List<Pool> ps, Func<Track, bool> bad, int max)
    {
        var seen = new HashSet<string>();
        var o = new List<Track>();
        var n = ps.Count == 0 ? 0 : ps.Max(p => p.Tracks.Count);
        for (var i = 0; i < n; i++)
            foreach (var p in ps)
                if (i < p.Tracks.Count && !bad(p.Tracks[i]) && seen.Add(p.Tracks[i].VideoId)) o.Add(p.Tracks[i]);
        return o.Take(max).ToList();
    }

    /// <summary>«Scopri settimanale»: brani nuovi per te (radio e artisti simili) che non hai ancora ascoltato; cambia ogni settimana.</summary>
    public static async Task<ForYouMix?> BuildWeeklyAsync(Taste.Profile prof, YtStore st, IReadOnlyList<List<Track>> radios, IReadOnlyList<Entry> similarArtists, long week, CancellationToken ct = default)
    {
        var known = prof.Weights.Keys.Concat(st.History.Select(h => h.VideoId)).Concat(st.Dislikes.Select(d => d.VideoId)).ToHashSet();
        var pools = radios.Select(r => r.Where(t => !known.Contains(t.VideoId)).ToList()).ToList();
        foreach (var a in similarArtists.Take(4))
        {
            try { pools.Add((await YtApi.ArtistAsync(a.BrowseId, ct).ConfigureAwait(false)).Tracks.Take(8).Where(t => !known.Contains(t.VideoId)).ToList()); }
            catch (Exception) { }
        }
        var rnd = new Random((int)(week % int.MaxValue));
        var shuffled = pools.Select(p => p.OrderBy(_ => rnd.Next()).ToList()).ToList();
        var seen = new HashSet<string>();
        var o = new List<Track>();
        var n = shuffled.Count == 0 ? 0 : shuffled.Max(p => p.Count);
        for (var i = 0; i < n; i++)
            foreach (var p in shuffled)
                if (i < p.Count && !prof.Bad(p[i]) && seen.Add(p[i].VideoId)) o.Add(p[i]);
        var tracks = o.Take(30).ToList();
        return tracks.Count == 0 ? null : new ForYouMix("Scopri settimanale", $"{tracks.Count} brani nuovi, cambia ogni settimana", tracks);
    }

    /// <summary>«Novità per te»: tra le nuove uscite di YouTube Music, quelle dei tuoi artisti e degli artisti simili. Null se la rete non risponde.</summary>
    public static async Task<List<Card>?> BuildReleasesAsync(IEnumerable<string> artistNames, CancellationToken ct = default)
    {
        var names = artistNames.Select(Taste.Norm).Where(n => n.Length > 1).ToHashSet();
        if (names.Count == 0) return new();
        Shelf? shelf;
        try { shelf = await YtApi.NewReleasesAsync(ct).ConfigureAwait(false); }
        catch (Exception) { return null; }
        if (shelf == null) return null;
        return shelf.Cards.Where(c => { var sub = Taste.Norm(c.Subtitle); return names.Any(n => sub.Contains(n)); }).ToList();
    }
}
