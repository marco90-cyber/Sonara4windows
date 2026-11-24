using System.Collections.Concurrent;
using Sonara.Core.Data;

namespace Sonara.Core.Yt;

/// <summary>
/// Il genere di un brano, in id di <see cref="FavGenres"/>. Due strade: l'origine (il brano è arrivato da uno scaffale di un genere)
/// oppure il genere dell'artista chiesto a Deezer e ricordato nello store. Porting di YtGenres.kt.
/// </summary>
public static class Genres
{
    private static readonly ConcurrentDictionary<string, string> Origin = new();

    /// <summary>Da chiamare quando si mostrano brani presi da uno scaffale di genere: se poi li ascolti, il genere è quello.</summary>
    public static void NoteOrigin(IEnumerable<Track> items, string genreId) { foreach (var t in items) Origin.TryAdd(t.VideoId, genreId); }
    public static string? OriginOf(string videoId) => Origin.TryGetValue(videoId, out var g) ? g : null;

    private static readonly Dictionary<string, string> Deezer = new()
    {
        ["pop"] = "pop", ["rap/hip hop"] = "hiphop", ["rock"] = "rock", ["dance"] = "dance", ["r&b"] = "rnb",
        ["alternative"] = "indie", ["electro"] = "electronic", ["folk"] = "folk", ["reggae"] = "reggae", ["jazz"] = "jazz",
        ["classical"] = "classica", ["films/games"] = "colonne", ["metal"] = "metal", ["soul & funk"] = "soul",
        ["latin music"] = "reggaeton", ["blues"] = "blues", ["country"] = "country", ["asian music"] = "kpop",
    };

    /// <summary>Il genere di un brano: prima quello d'origine ricordato, poi quello dell'artista; null se non si sa.</summary>
    public static string? GenreOf(Track t, YtStore st)
    {
        if (st.TrackGenres.TryGetValue(t.VideoId, out var g)) return g;
        var a = Taste.ArtistNames(t.Artist).FirstOrDefault();
        return a != null && st.ArtistGenres.TryGetValue(Taste.Norm(a), out var ag) && ag.Length > 0 ? ag : null;
    }

    /// <summary>Ricava da Deezer il genere degli artisti non ancora noti (al massimo max per volta). Un artista non trovato resta «sconosciuto» e non si richiede più.</summary>
    public static async Task FetchMissingAsync(YtStore st, IEnumerable<string> artists, int max = 12, CancellationToken ct = default)
    {
        var todo = artists.Where(a => !st.ArtistGenres.ContainsKey(Taste.Norm(a))).GroupBy(Taste.Norm).Select(g => g.First()).Take(max).ToList();
        if (todo.Count == 0) return;
        var found = new Dictionary<string, string>();
        foreach (var name in todo)
        {
            try { found[Taste.Norm(name)] = await ArtistGenreAsync(name, ct).ConfigureAwait(false) ?? ""; }
            catch (Exception) { /* rete assente: si riprova la prossima volta */ }
        }
        st.AddArtistGenres(found);
    }

    private static async Task<string?> ArtistGenreAsync(string name, CancellationToken ct)
    {
        var hit = await PlaylistImport.DeezerArtistGenresAsync(name, ct).ConfigureAwait(false);
        var best = hit?.GroupBy(x => x).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
        return best != null && Deezer.TryGetValue(best.ToLowerInvariant(), out var id) ? id : null;
    }
}
