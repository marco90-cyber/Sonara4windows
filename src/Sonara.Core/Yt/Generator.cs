using Sonara.Core.Data;

namespace Sonara.Core.Yt;

/// <summary>
/// Generatore di playlist (dall'idea di LastWave, GPL-3.0): crea un elenco di brani da una modalità,
/// partendo dai gusti locali di Sonara (ascolti, cronologia, preferiti, salti, «non mi piace») e da YouTube Music. Porting di YtGenerator.kt.
/// </summary>
public static class Generator
{
    public enum Mode { Top, Recent, Radio, SimilarArtists, Genre, MyMix, Suggestions, NeverHeard, Library }

    public sealed record PeriodInfo(string Label, int Days);

    public enum Period { All, Y1, M6, M3, M1, W1 }

    public static PeriodInfo Info(Period p) => p switch
    {
        Period.Y1 => new("12 mesi", 365), Period.M6 => new("6 mesi", 182), Period.M3 => new("3 mesi", 91),
        Period.M1 => new("1 mese", 30), Period.W1 => new("7 giorni", 7), _ => new("Sempre", 0),
    };

    /// <summary>Tutto quello che serve a generare: i gusti e le scelte fatte nella pagina.</summary>
    public sealed record Input(
        IReadOnlyDictionary<string, Play> Plays, IReadOnlyList<Track> History, IReadOnlyList<Track> Favorites,
        IReadOnlyDictionary<string, int> Skips, IReadOnlyList<Track> Dislikes,
        Track? Seed = null, string? Artist = null, FavGenres.Genre? Genre = null, Period Period = Period.All, int Count = 26);

    public static async Task<List<Track>> GenerateAsync(Mode mode, Input input, Action<string>? onProgress = null, CancellationToken ct = default)
    {
        onProgress ??= _ => { };
        var profile = Taste.BuildProfile(input.Plays, input.History, input.Favorites, input.Skips, input.Dislikes);
        var w = profile.Weights;
        var bad = profile.Bad;
        var n = Math.Clamp(input.Count, 5, 100);
        var days = Info(input.Period).Days;
        var cutoff = days > 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - days * 86_400_000L : 0L;
        // tutto quello che hai già ascoltato o salvato: per le scoperte
        var known = new HashSet<string>(w.Keys);
        foreach (var k in input.Plays.Keys) known.Add(k);
        foreach (var t in input.History.Concat(input.Favorites).Concat(input.Dislikes)) known.Add(t.VideoId);

        List<Track> Clean(IEnumerable<Track> l) => l.Where(t => !bad(t)).GroupBy(t => t.VideoId).Select(g => g.First()).ToList();
        long LastAt(string id, long dflt) => input.Plays.TryGetValue(id, out var p) ? p.LastAt : dflt;

        List<Track> res;
        switch (mode)
        {
            case Mode.Top:
            {
                var pool = cutoff > 0 ? w.Where(kv => LastAt(kv.Key, 0) >= cutoff).ToDictionary(kv => kv.Key, kv => kv.Value) : w;
                res = Clean(Taste.TopTracks(pool, n * 2));
                break;
            }
            case Mode.Recent:
                res = Clean(input.History.Where(t => cutoff == 0 || LastAt(t.VideoId, long.MaxValue) >= cutoff));
                break;
            case Mode.Radio:
            {
                var seed = input.Seed; if (seed == null) return new();
                onProgress($"Radio da «{seed.Title}»…");
                var r = await YtApi.RadioAsync(seed.VideoId, ct).ConfigureAwait(false);
                res = new List<Track> { seed };
                res.AddRange(Clean(r.Where(t => t.VideoId != seed.VideoId)));
                break;
            }
            case Mode.SimilarArtists:
                if (string.IsNullOrWhiteSpace(input.Artist)) return new();
                res = await SimilarArtistsTracksAsync(input.Artist, n, bad, onProgress, ct).ConfigureAwait(false);
                break;
            case Mode.Genre:
            {
                var g = input.Genre; if (g == null) return new();
                onProgress($"Cerco «{g.Label}»…");
                res = Clean(await YtApi.SearchSongsAsync(g.Query, 2, null, ct).ConfigureAwait(false)).OrderBy(_ => Random.Shared.Next()).ToList();
                break;
            }
            case Mode.MyMix:
            {
                onProgress("Mescolo i tuoi gusti…");
                var lists = new List<List<Track>> { Taste.TopTracks(w, n), Taste.ForgottenFavorites(input.Favorites, input.Plays, n) };
                foreach (var s in Taste.Seeds(w, 4))
                {
                    try { lists.Add(await YtApi.RadioAsync(s.VideoId, ct).ConfigureAwait(false)); } catch (Exception) { }
                }
                res = Clean(Interleave(lists.Select(Shuffle).ToList()));
                break;
            }
            case Mode.Suggestions:
                onProgress("Cerco scoperte per te…");
                res = Clean((await RadiosFromAsync(w, 5, ct).ConfigureAwait(false)).SelectMany(x => x).Where(t => !known.Contains(t.VideoId)));
                break;
            case Mode.NeverHeard:
            {
                onProgress("Cerco brani nuovi…");
                var fresh = Clean(Interleave(await RadiosFromAsync(w, 6, ct).ConfigureAwait(false)).Where(t => !known.Contains(t.VideoId)));
                // al massimo due brani per artista, per non ripetersi
                var per = new Dictionary<string, int>();
                res = fresh.Where(t => { var k = Taste.Norm(t.Artist); per[k] = per.GetValueOrDefault(k) + 1; return per[k] <= 2; }).ToList();
                break;
            }
            default: // Library: «Riscopri il tuo passato»: brani amati (più ascolti) che non senti da un po', nel periodo scelto
            {
                var loved = input.Plays.Values.Where(p => p.Count >= 2 && p.LastAt >= cutoff).OrderBy(p => p.LastAt).Select(p => p.Item);
                res = Clean(loved.Concat(Taste.ForgottenFavorites(input.Favorites, input.Plays, n)).Concat(input.History.Reverse()));
                break;
            }
        }
        return res.Take(n).ToList();
    }

    /// <summary>Le radio dei brani di partenza (un brano per artista).</summary>
    private static async Task<List<List<Track>>> RadiosFromAsync(Dictionary<string, Taste.Weighted> w, int seeds, CancellationToken ct)
    {
        var o = new List<List<Track>>();
        foreach (var s in Taste.Seeds(w, seeds))
        {
            try { o.Add(await YtApi.RadioAsync(s.VideoId, ct).ConfigureAwait(false)); } catch (Exception) { }
        }
        return o;
    }

    private static async Task<List<Track>> SimilarArtistsTracksAsync(string name, int n, Func<Track, bool> bad, Action<string> onProgress, CancellationToken ct)
    {
        onProgress($"Cerco artisti simili a {name}…");
        var me = await Feed.ArtistEntryAsync(name, ct).ConfigureAwait(false);
        if (me == null) return new();
        List<Entry> sims;
        try { sims = (await YtApi.SimilarArtistsAsync(me.BrowseId, ct).ConfigureAwait(false)).Take(6).ToList(); } catch (Exception) { sims = new(); }
        var lists = new List<List<Track>>();
        foreach (var a in sims)
        {
            onProgress($"Brani di {a.Title}…");
            try { lists.Add((await YtApi.ArtistAsync(a.BrowseId, ct).ConfigureAwait(false)).Tracks.Take(n / 3 + 3).ToList()); } catch (Exception) { }
        }
        return Interleave(lists).Where(t => !bad(t)).GroupBy(t => t.VideoId).Select(g => g.First()).ToList();
    }

    private static List<T> Shuffle<T>(List<T> l) => l.OrderBy(_ => Random.Shared.Next()).ToList();

    /// <summary>Un brano per volta da ogni elenco, a turno.</summary>
    public static List<T> Interleave<T>(IReadOnlyList<List<T>> lists)
    {
        var o = new List<T>();
        for (var i = 0; lists.Any(l => i < l.Count); i++)
            foreach (var l in lists) if (i < l.Count) o.Add(l[i]);
        return o;
    }
}
