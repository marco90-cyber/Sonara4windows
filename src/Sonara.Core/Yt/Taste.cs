using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Sonara.Core.Data;

namespace Sonara.Core.Yt;

/// <summary>Gusti ricavati in locale da ascolti, cronologia e preferiti (nessun account). Funzioni pure. Porting di YtTaste.kt.</summary>
public static class Taste
{
    private static readonly Regex Marks = new(@"\p{M}+", RegexOptions.Compiled);
    private static readonly Regex NotAlnum = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>Punti di ogni gesto: «mi piace» +2, ascolto completo +1 (al massimo 5 per brano), salto entro 30 s -0,5 (al massimo -1 per brano), «non mi piace» -1.</summary>
    public const double Like = 2.0, Complete = 1.0, Skip = -0.5, MaxSkip = -1.0, Dislike = -1.0;
    public const int MaxComplete = 5;

    public sealed record Weighted(Track Item, double Weight);

    /// <summary>
    /// Peso di un brano. Ascolti contati (tetto 5); un brano solo in cronologia vale 1, ma 0 se l'hai saltato;
    /// il preferito +2; i salti tolgono 0,5 l'uno fino a -1. Il «non mi piace» esclude il brano e pesa sull'artista e sul genere.
    /// </summary>
    public static Dictionary<string, Weighted> Weights(IReadOnlyDictionary<string, Play> plays, IEnumerable<Track> history, IEnumerable<Track> favorites, IReadOnlyDictionary<string, int>? skips = null)
    {
        skips ??= new Dictionary<string, int>();
        var items = new Dictionary<string, Track>();
        var b = new Dictionary<string, double>();
        foreach (var p in plays.Values) { items[p.Item.VideoId] = p.Item; b[p.Item.VideoId] = Math.Min(p.Count, MaxComplete) * Complete; }
        foreach (var h in history)
            if (!items.ContainsKey(h.VideoId)) { items[h.VideoId] = h; b[h.VideoId] = skips.GetValueOrDefault(h.VideoId) > 0 ? 0.0 : Complete; }
        foreach (var f in favorites)
        {
            items.TryAdd(f.VideoId, f);
            b[f.VideoId] = b.GetValueOrDefault(f.VideoId) + Like;
        }
        foreach (var (id, n) in skips)
            if (items.ContainsKey(id)) b[id] = b.GetValueOrDefault(id) + Math.Max(n * Skip, MaxSkip);
        return items.ToDictionary(kv => kv.Key, kv => new Weighted(kv.Value, b[kv.Key]));
    }

    /// <summary>I gusti già pronti: pesi (senza i brani non graditi), filtro per escludere brani e artisti sotto zero, punteggio per artista.</summary>
    public sealed record Profile(Dictionary<string, Weighted> Weights, Func<Track, bool> Bad, Dictionary<string, double> ArtistScore, List<Track> Dislikes);

    /// <summary>Un artista è escluso quando il suo totale è sotto zero: i suoi brani (ascolti, preferiti, salti) più -1 per ogni «non mi piace». Il brano non gradito è sempre escluso.</summary>
    public static Profile BuildProfile(IReadOnlyDictionary<string, Play> plays, IEnumerable<Track> history, IEnumerable<Track> favorites, IReadOnlyDictionary<string, int> skips, IEnumerable<Track> dislikes)
    {
        var dis = dislikes.ToList();
        var ids = dis.Select(d => d.VideoId).ToHashSet();
        var w = Weights(plays, history, favorites, skips).Where(kv => !ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var artist = ArtistScores(w, dis);
        return new Profile(w, MakeBad(ids, artist), artist, dis);
    }

    private static Dictionary<string, double> ArtistScores(Dictionary<string, Weighted> w, IEnumerable<Track> dislikes)
    {
        var artist = new Dictionary<string, double>();
        foreach (var v in w.Values)
            foreach (var a in ArtistNames(v.Item.Artist)) artist[Norm(a)] = artist.GetValueOrDefault(Norm(a)) + v.Weight;
        foreach (var d in dislikes)
            foreach (var a in ArtistNames(d.Artist)) artist[Norm(a)] = artist.GetValueOrDefault(Norm(a)) + Dislike;
        return artist;
    }

    private static Func<Track, bool> MakeBad(HashSet<string> ids, Dictionary<string, double> artist) => t =>
    {
        if (ids.Contains(t.VideoId)) return true;
        var first = ArtistNames(t.Artist).FirstOrDefault();
        return first != null && artist.GetValueOrDefault(Norm(first)) < 0.0;
    };

    /// <summary>Punteggio dei generi: somma dei pesi dei brani di ogni genere, -1 per ogni «non mi piace». genreOf dà l'id del genere (o null).</summary>
    public static Dictionary<string, double> GenreScores(Profile p, Func<Track, string?> genreOf)
    {
        var o = new Dictionary<string, double>();
        foreach (var v in p.Weights.Values) { var g = genreOf(v.Item); if (g != null) o[g] = o.GetValueOrDefault(g) + v.Weight; }
        foreach (var d in p.Dislikes) { var g = genreOf(d); if (g != null) o[g] = o.GetValueOrDefault(g) + Dislike; }
        return o;
    }

    /// <summary>Esclude i brani non graditi e gli artisti con punteggio negativo.</summary>
    public static Func<Track, bool> Excluder(IEnumerable<Track> dislikes, Dictionary<string, Weighted>? w = null)
    {
        var list = dislikes.ToList();
        var ids = list.Select(d => d.VideoId).ToHashSet();
        return MakeBad(ids, ArtistScores(w ?? new Dictionary<string, Weighted>(), list));
    }

    public static List<Track> TopTracks(Dictionary<string, Weighted> w, int n) =>
        w.Values.OrderByDescending(v => v.Weight).Take(n).Select(v => v.Item).ToList();

    /// <summary>"A, B &amp; C" → [A, B, C].</summary>
    public static List<string> ArtistNames(string artist) =>
        artist.Split(new[] { ",", "&", " e ", " x ", " feat. ", " ft. " }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>Nome senza maiuscole, accenti e punteggiatura.</summary>
    public static string Norm(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return NotAlnum.Replace(Marks.Replace(d, ""), " ").Trim();
    }

    /// <summary>Gli artisti più ascoltati (nome, peso), sommando i pesi dei loro brani; solo quelli con punteggio positivo.</summary>
    public static List<(string Name, double Weight)> TopArtists(Dictionary<string, Weighted> w, int n)
    {
        var names = new Dictionary<string, string>();
        var sum = new Dictionary<string, double>();
        foreach (var v in w.Values)
            foreach (var a in ArtistNames(v.Item.Artist))
            {
                var k = Norm(a);
                if (k.Length == 0) continue;
                names.TryAdd(k, a);
                sum[k] = sum.GetValueOrDefault(k) + v.Weight;
            }
        return sum.Where(kv => kv.Value > 0.0).OrderByDescending(kv => kv.Value).Take(n).Select(kv => (names[kv.Key], kv.Value)).ToList();
    }

    /// <summary>Un brano di partenza per ogni artista tra i primi: il suo brano più ascoltato.</summary>
    public static List<Track> Seeds(Dictionary<string, Weighted> w, int n)
    {
        var used = new HashSet<string>();
        var o = new List<Track>();
        foreach (var e in w.Values.Where(v => v.Weight > 0.0).OrderByDescending(v => v.Weight))
        {
            var a = ArtistNames(e.Item.Artist).FirstOrDefault();
            if (a == null) continue;
            if (used.Add(Norm(a))) o.Add(e.Item);
            if (o.Count >= n) break;
        }
        return o;
    }

    /// <summary>Preferiti che non ascolti da più tempo, fino a n.</summary>
    public static List<Track> ForgottenFavorites(IEnumerable<Track> favorites, IReadOnlyDictionary<string, Play> plays, int n) =>
        favorites.OrderBy(f => plays.TryGetValue(f.VideoId, out var p) ? p.LastAt : 0L).Take(n).ToList();
}

/// <summary>I generi che si possono scegliere come preferiti.</summary>
public static class FavGenres
{
    public sealed record Genre(string Id, string Label, string Query);

    public static readonly IReadOnlyList<Genre> All = new[]
    {
        new Genre("pop", "Pop", "Pop hits"), new Genre("pop-it", "Pop italiano", "Pop italiano hits"),
        new Genre("rap-it", "Rap italiano", "Rap italiano"), new Genre("cantautori", "Cantautori", "Cantautori italiani"),
        new Genre("rock", "Rock", "Rock hits"), new Genre("indie", "Indie", "Indie"),
        new Genre("hiphop", "Hip-hop", "Hip hop hits"), new Genre("trap", "Trap", "Trap"),
        new Genre("rnb", "R&B", "R&B hits"), new Genre("dance", "Dance", "Dance hits"),
        new Genre("electronic", "Elettronica", "Musica elettronica"), new Genre("house", "House", "House music"),
        new Genre("techno", "Techno", "Techno"), new Genre("reggaeton", "Reggaeton e latino", "Reggaeton latino hits"),
        new Genre("metal", "Metal", "Metal"), new Genre("punk", "Punk", "Punk rock"),
        new Genre("jazz", "Jazz", "Jazz"), new Genre("blues", "Blues", "Blues"),
        new Genre("classica", "Classica", "Musica classica"), new Genre("soul", "Soul e funk", "Soul funk"),
        new Genre("reggae", "Reggae", "Reggae"), new Genre("country", "Country", "Country hits"),
        new Genre("folk", "Folk e acustica", "Folk acoustic"), new Genre("kpop", "K-pop", "K-pop"),
        new Genre("anni", "Anni '80 e '90", "Hit anni 80 90"), new Genre("lofi", "Lo-fi e relax", "Lofi chill"),
        new Genre("colonne", "Colonne sonore", "Colonne sonore"),
    };

    public static List<Genre> Parse(string csv)
    {
        var ids = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        return All.Where(g => ids.Contains(g.Id)).ToList();
    }

    /// <summary>Vecchio formato (etichette separate da «|», es. "Hip hop|Latino") → id separati da virgola.</summary>
    public static string MigrateLegacy(string old)
    {
        if (string.IsNullOrWhiteSpace(old) || !old.Contains('|') && All.Any(g => g.Id == old.Trim())) return old ?? "";
        var alias = new Dictionary<string, string> { ["hip hop"] = "hiphop", ["latino"] = "reggaeton", ["elettronica"] = "electronic" };
        var ids = new List<string>();
        foreach (var part in old.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var key = part.ToLowerInvariant();
            var g = All.FirstOrDefault(x => x.Id == key || x.Label.Equals(part, StringComparison.OrdinalIgnoreCase));
            var id = g?.Id ?? alias.GetValueOrDefault(key);
            if (id != null && !ids.Contains(id)) ids.Add(id);
        }
        return string.Join(",", ids);
    }

    public static string Toggle(string csv, string id)
    {
        var ids = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!ids.Remove(id)) ids.Add(id);
        return string.Join(",", ids);
    }
}
