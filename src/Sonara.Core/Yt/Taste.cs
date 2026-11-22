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

    /// <summary>Peso di un brano: ascolti contati; quelli della cronologia senza conteggio valgono 1, i preferiti +2.</summary>
    public static Dictionary<string, (Track Item, int Weight)> Weights(IReadOnlyDictionary<string, Play> plays, IEnumerable<Track> history, IEnumerable<Track> favorites)
    {
        var o = new Dictionary<string, (Track, int)>();
        foreach (var p in plays.Values) o[p.Item.VideoId] = (p.Item, p.Count);
        foreach (var h in history) if (!o.ContainsKey(h.VideoId)) o[h.VideoId] = (h, 1);
        foreach (var f in favorites)
        {
            o.TryGetValue(f.VideoId, out var cur);
            o[f.VideoId] = (cur.Item1 ?? f, cur.Item2 + 2);
        }
        return o;
    }

    /// <summary>Esclude i brani non graditi e gli artisti con almeno 3 brani non graditi.</summary>
    public static Func<Track, bool> Excluder(IEnumerable<Track> dislikes)
    {
        var list = dislikes.ToList();
        var ids = list.Select(d => d.VideoId).ToHashSet();
        var perArtist = new Dictionary<string, int>();
        foreach (var d in list)
        {
            var a = ArtistNames(d.Artist).FirstOrDefault();
            if (a != null) perArtist[Norm(a)] = perArtist.GetValueOrDefault(Norm(a)) + 1;
        }
        var blocked = perArtist.Where(kv => kv.Value >= 3).Select(kv => kv.Key).ToHashSet();
        return t =>
        {
            if (ids.Contains(t.VideoId)) return true;
            var first = ArtistNames(t.Artist).FirstOrDefault();
            return first != null && blocked.Contains(Norm(first));
        };
    }

    public static List<Track> TopTracks(Dictionary<string, (Track Item, int Weight)> w, int n) =>
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

    /// <summary>Gli artisti più ascoltati (nome, peso), sommando i pesi dei loro brani.</summary>
    public static List<(string Name, int Weight)> TopArtists(Dictionary<string, (Track Item, int Weight)> w, int n)
    {
        var names = new Dictionary<string, string>();
        var sum = new Dictionary<string, int>();
        foreach (var (item, wt) in w.Values)
            foreach (var a in ArtistNames(item.Artist))
            {
                var k = Norm(a);
                if (k.Length == 0) continue;
                names.TryAdd(k, a);
                sum[k] = sum.GetValueOrDefault(k) + wt;
            }
        return sum.OrderByDescending(kv => kv.Value).Take(n).Select(kv => (names[kv.Key], kv.Value)).ToList();
    }

    /// <summary>Un brano di partenza per ogni artista tra i primi: il suo brano più ascoltato.</summary>
    public static List<Track> Seeds(Dictionary<string, (Track Item, int Weight)> w, int n)
    {
        var used = new HashSet<string>();
        var o = new List<Track>();
        foreach (var e in w.Values.OrderByDescending(v => v.Weight))
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

    public static string Toggle(string csv, string id)
    {
        var ids = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!ids.Remove(id)) ids.Add(id);
        return string.Join(",", ids);
    }
}
