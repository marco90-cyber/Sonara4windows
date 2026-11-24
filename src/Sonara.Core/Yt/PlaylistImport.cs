using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sonara.Core.Yt;

/// <summary>
/// Importa playlist pubbliche da Spotify, Deezer e YouTube. Deezer e Spotify danno solo titolo, artista e durata:
/// ogni brano viene cercato su YouTube Music e scelto confrontando titolo, artista e durata. Porting di PlaylistImport.kt.
/// </summary>
public static class PlaylistImport
{
    public enum Source { YouTube, Deezer, Spotify }

    public static string Label(this Source s) => s switch { Source.YouTube => "YouTube", Source.Deezer => "Deezer", _ => "Spotify" };
    public static string Prefix(this Source s) => s switch { Source.YouTube => "ytl", Source.Deezer => "dz", _ => "sp" };

    /// <summary>Una playlist di un servizio: Id è quello del servizio.</summary>
    public sealed record Ref(Source Source, string Id)
    {
        /// <summary>Id usato quando la playlist è solo aperta (ricerca o link).</summary>
        public string BrowseId => $"{Source.Prefix()}:{Id}";
        /// <summary>Id con cui la playlist importata viene salvata.</summary>
        public string ImportId => $"imp:{Source.Prefix()}:{Id}";
        public Entry Placeholder() => new(Kind.Playlists, BrowseId, $"Playlist {Source.Label()}", Source.Label(), "");
    }

    /// <summary>Un brano come lo descrive il servizio di origine.</summary>
    public sealed record Raw(string Title, string Artist, long DurationSec);
    public sealed record Meta(string Name, string Owner, string Cover);
    public sealed record Fetched(Meta Meta, List<Raw> Tracks);
    public sealed record Imported(Meta Meta, List<Track> Tracks, int Total, Dictionary<string, string> Keys);

    private static readonly Regex Deezer = new(@"deezer\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?playlist/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SpotifyUrl = new(@"open\.spotify\.com/(?:intl-[a-z]+/)?(?:embed/)?playlist/([A-Za-z0-9]{22})", RegexOptions.Compiled);
    private static readonly Regex SpotifyUri = new(@"^spotify:playlist:([A-Za-z0-9]{22})", RegexOptions.Compiled);
    private static readonly Regex YouTubeRx = new(@"(?:music\.)?youtube\.com/.*[?&]list=([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] ShortHosts = { "link.deezer.com", "deezer.page.link", "spotify.link", "spotify.app.link", "dzr.page.link" };
    private static readonly Regex NextData = new("<script id=\"__NEXT_DATA__\" type=\"application/json\">(.*?)</script>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Marks = new(@"\p{M}+", RegexOptions.Compiled);
    private static readonly Regex Brackets = new(@"[(\[].*?[)\]]", RegexOptions.Compiled);
    private static readonly Regex AfterDash = new(@"\s-\s.*$", RegexOptions.Compiled);
    private static readonly Regex NotAlnum = new(@"[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex ArtistSplit = new(@"\s*(?:,|&|;|\bfeat\.?\b|\bft\.?\b|\be\b)\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var c = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All, AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(25) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("it-IT,it;q=0.9,en;q=0.8");
        return c;
    }

    // ------------------------------------------------------------------ link

    /// <summary>Il testo sembra un link (o un URI Spotify).</summary>
    public static bool MaybeLink(string text)
    {
        var t = text.Trim();
        return !t.Contains(' ') && (t.StartsWith("http", StringComparison.OrdinalIgnoreCase) || t.StartsWith("spotify:"));
    }

    /// <summary>Riconosce il link di una playlist di Deezer, Spotify o YouTube; null se non lo è. Non usa la rete.</summary>
    public static Ref? ParseLink(string text)
    {
        var t = text.Trim();
        var m = Deezer.Match(t); if (m.Success) return new Ref(Source.Deezer, m.Groups[1].Value);
        m = SpotifyUrl.Match(t); if (m.Success) return new Ref(Source.Spotify, m.Groups[1].Value);
        m = SpotifyUri.Match(t); if (m.Success) return new Ref(Source.Spotify, m.Groups[1].Value);
        m = YouTubeRx.Match(t); if (m.Success) return new Ref(Source.YouTube, m.Groups[1].Value);
        return null;
    }

    /// <summary>Come ParseLink, ma segue i link brevi (link.deezer.com, spotify.link...).</summary>
    public static async Task<Ref?> ParseResolvingAsync(string text, CancellationToken ct = default)
    {
        var r = ParseLink(text);
        if (r != null) return r;
        var t = text.Trim();
        if (!ShortHosts.Any(h => t.Contains(h, StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            using var resp = await Http.GetAsync(t, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return ParseLink(resp.RequestMessage?.RequestUri?.ToString() ?? "");
        }
        catch (Exception) { return null; }
    }

    /// <summary>Dal browse id di una voce ("dz:12", "sp:...", "ytl:...", "imp:dz:12"); null per tutti gli altri.</summary>
    public static Ref? RefOf(string browseId)
    {
        var s = browseId.StartsWith("imp:") ? browseId[4..] : browseId;
        var i = s.IndexOf(':');
        if (i < 0 || i == s.Length - 1) return null;
        var prefix = s[..i];
        foreach (var src in Enum.GetValues<Source>())
            if (src.Prefix() == prefix) return new Ref(src, s[(i + 1)..]);
        return null;
    }

    // ------------------------------------------------------------------ ricerca

    /// <summary>Playlist pubbliche di Deezer per query. Vuoto se non raggiungibile.</summary>
    public static async Task<List<Entry>> SearchDeezerAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var j = await GetJsonAsync($"https://api.deezer.com/search/playlist?q={Uri.EscapeDataString(query)}&limit=25", ct).ConfigureAwait(false);
            return DeezerEntries(j["data"] as JsonArray);
        }
        catch (Exception) { return new(); }
    }

    /// <summary>Le playlist più ascoltate su Deezer in questo momento.</summary>
    public static async Task<List<Entry>> DeezerChartsAsync(int limit = 8, CancellationToken ct = default)
    {
        try
        {
            var j = await GetJsonAsync($"https://api.deezer.com/chart/0/playlists?limit={limit}", ct).ConfigureAwait(false);
            return DeezerEntries(j["data"] as JsonArray);
        }
        catch (Exception) { return new(); }
    }

    private static Dictionary<int, string>? _genreNames;

    /// <summary>I generi (nomi di Deezer) degli album di un artista; null se non si trova o la rete non risponde.</summary>
    public static async Task<List<string>?> DeezerArtistGenresAsync(string name, CancellationToken ct = default)
    {
        var key = Taste.Norm(name);
        var found = await GetJsonAsync($"https://api.deezer.com/search/artist?q={Uri.EscapeDataString(name)}&limit=5", ct).ConfigureAwait(false);
        var artist = (found["data"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(o => Taste.Norm(o["name"]?.GetValue<string>() ?? "") == key);
        var id = artist?["id"]?.GetValue<long>() ?? 0;
        if (id <= 0) return null;
        var albums = (await GetJsonAsync($"https://api.deezer.com/artist/{id}/albums?limit=25", ct).ConfigureAwait(false))["data"] as JsonArray;
        var ids = albums?.OfType<JsonObject>().Select(o => o["genre_id"]?.GetValue<int>() ?? -1).Where(g => g > 0).ToList();
        if (ids == null || ids.Count == 0) return null;
        var names = await GenreNamesAsync(ct).ConfigureAwait(false);
        var res = ids.Select(g => names.GetValueOrDefault(g)).Where(n => n != null).Select(n => n!).ToList();
        return res.Count == 0 ? null : res;
    }

    private static async Task<Dictionary<int, string>> GenreNamesAsync(CancellationToken ct)
    {
        if (_genreNames != null) return _genreNames;
        var m = new Dictionary<int, string>();
        var a = (await GetJsonAsync("https://api.deezer.com/genre", ct).ConfigureAwait(false))["data"] as JsonArray;
        if (a != null)
            foreach (var o in a.OfType<JsonObject>())
                m[o["id"]?.GetValue<int>() ?? 0] = o["name"]?.GetValue<string>() ?? "";
        if (m.Count > 0) _genreNames = m;
        return m;
    }

    private static List<Entry> DeezerEntries(JsonArray? data)
    {
        var res = new List<Entry>();
        if (data == null) return res;
        foreach (var n in data)
        {
            if (n is not JsonObject o) continue;
            var id = o["id"]?.GetValue<long>() ?? 0;
            if (id <= 0) continue;
            var parts = new List<string> { "Deezer" };
            var nb = o["nb_tracks"]?.GetValue<int>() ?? 0;
            if (nb > 0) parts.Add($"{nb} brani");
            var by = J.Str(o["user"]?["name"]);
            if (by.Length > 0) parts.Add($"di {by}");
            res.Add(new Entry(Kind.Playlists, new Ref(Source.Deezer, id.ToString()).BrowseId, J.Str(o["title"]), string.Join(" · ", parts), J.Str(o["picture_medium"])));
        }
        return res;
    }

    // ------------------------------------------------------------------ lettura dei servizi

    private static async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        using var r = await Http.GetAsync(url, ct).ConfigureAwait(false);
        if (!r.IsSuccessStatusCode) throw new IOException($"HTTP {(int)r.StatusCode}");
        return await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static async Task<JsonNode> GetJsonAsync(string url, CancellationToken ct) =>
        JsonNode.Parse(await GetTextAsync(url, ct).ConfigureAwait(false)) ?? throw new IOException("risposta vuota");

    /// <summary>Titolo, autore e brani di una playlist pubblica di Deezer.</summary>
    public static async Task<Fetched> FetchDeezerAsync(string id, CancellationToken ct = default)
    {
        var head = await GetJsonAsync($"https://api.deezer.com/playlist/{id}", ct).ConfigureAwait(false);
        if (head["error"] is JsonObject err) throw new IOException("Deezer: " + J.Str(err["message"]));
        if (head["public"] is JsonValue pv && pv.TryGetValue<bool>(out var pub) && !pub) throw new IOException("Playlist Deezer non pubblica");
        var res = new List<Raw>();
        var index = 0;
        for (var page = 0; page < 10; page++)
        {
            var j = await GetJsonAsync($"https://api.deezer.com/playlist/{id}/tracks?limit=100&index={index}", ct).ConfigureAwait(false);
            if (j["data"] is not JsonArray data || data.Count == 0) break;
            foreach (var n in data)
            {
                var title = J.Str(n?["title"]);
                if (title.Length > 0) res.Add(new Raw(title, J.Str(n?["artist"]?["name"]), n?["duration"]?.GetValue<long>() ?? 0));
            }
            if (data.Count < 100) break;
            index += 100;
        }
        var name = J.Str(head["title"]);
        return new Fetched(new Meta(name.Length > 0 ? name : "Playlist Deezer", J.Str(head["creator"]?["name"]), J.Str(head["picture_big"])), res);
    }

    /// <summary>Titolo e brani di una playlist pubblica di Spotify, dalla pagina «embed» (circa i primi 100 brani).</summary>
    public static async Task<Fetched> FetchSpotifyAsync(string id, CancellationToken ct = default)
    {
        var html = await GetTextAsync($"https://open.spotify.com/embed/playlist/{id}", ct).ConfigureAwait(false);
        var m = NextData.Match(html);
        if (!m.Success) throw new IOException("Spotify: pagina non riconosciuta");
        var root = JsonNode.Parse(m.Groups[1].Value);
        if (FindKey(root, "trackList") is not JsonArray list) throw new IOException("Spotify: nessun elenco di brani (playlist privata?)");
        var res = new List<Raw>();
        foreach (var n in list)
        {
            var title = J.Str(n?["title"]);
            if (title.Length == 0) continue;
            var dur = n?["duration"] is JsonValue dv && dv.TryGetValue<long>(out var d) ? d / 1000 : 0;
            res.Add(new Raw(title, J.Str(n?["subtitle"]), dur));
        }
        var entity = root?["props"]?["pageProps"]?["state"]?["data"]?["entity"];
        var name = J.Str(entity?["name"]);
        if (name.Length == 0) name = J.Str(entity?["title"]);
        if (name.Length == 0) name = "Playlist Spotify";
        var cover = J.Str((FindKey(root, "coverArt") as JsonObject)?["sources"]?[0]?["url"]);
        return new Fetched(new Meta(name, "", cover), res);
    }

    private static JsonNode? FindKey(JsonNode? node, string key)
    {
        if (node is JsonObject o)
        {
            if (o.ContainsKey(key)) return o[key];
            foreach (var kv in o) { var f = FindKey(kv.Value, key); if (f != null) return f; }
        }
        else if (node is JsonArray a)
        {
            foreach (var x in a) { var f = FindKey(x, key); if (f != null) return f; }
        }
        return null;
    }

    // ------------------------------------------------------------------ abbinamento su YouTube Music

    /// <summary>Testo confrontabile: minuscolo, senza accenti e simboli; con dropSuffix toglie anche « - Remastered 2011».</summary>
    public static string Norm(string s, bool dropSuffix = false)
    {
        var t = Marks.Replace(s.ToLowerInvariant().Normalize(NormalizationForm.FormD), "");
        t = Brackets.Replace(t, " ");
        if (dropSuffix) t = AfterDash.Replace(t, "");
        return NotAlnum.Replace(t, " ").Trim();
    }

    /// <summary>Quanto il risultato c somiglia al brano raw: titolo, artista e durata. Sotto zero = da scartare.</summary>
    public static int Score(Raw raw, Track c)
    {
        var t = Norm(raw.Title, true);
        var ct = Norm(c.Title);
        if (t.Length == 0 || ct.Length == 0) return -1;
        int s;
        if (ct == t) s = 3;
        else if (ct.Length >= 3 && t.Length >= 3 && (ct.Contains(t) || t.Contains(ct))) s = 2;
        else return -1;
        var first = ArtistSplit.Split(raw.Artist).Select(a => Norm(a)).FirstOrDefault(a => a.Length > 0);
        if (first != null && $"{Norm(c.Artist)} {ct}".Contains(first)) s += 2;
        if (raw.DurationSec > 0 && c.DurationSec > 0)
        {
            var d = Math.Abs(raw.DurationSec - c.DurationSec);
            s += d <= 5 ? 2 : d <= 12 ? 1 : d > 30 ? -2 : 0;
        }
        return s;
    }

    private const int MinScore = 4;

    /// <summary>Il risultato migliore tra i candidati (a parità, il primo); null se nessuno è abbastanza simile.</summary>
    public static Track? Best(Raw raw, IEnumerable<Track> candidates)
    {
        Track? best = null; var bs = int.MinValue;
        foreach (var c in candidates)
        {
            var s = Score(raw, c);
            if (s >= MinScore && s > bs) { best = c; bs = s; }
        }
        return best;
    }

    /// <summary>Chiave stabile di un brano di Spotify o Deezer: serve a riconoscerlo negli aggiornamenti.</summary>
    public static string RawKey(Raw raw) => Norm(raw.Title, true) + "|" + Norm(raw.Artist);

    private static async Task<Track?> MatchStrictAsync(Raw raw, CancellationToken ct)
    {
        var q = $"{raw.Title} {raw.Artist}".Trim();
        List<Track> found;
        try { found = await YtApi.SearchSongsAsync(q, 1, null, ct).ConfigureAwait(false); }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);
            found = await YtApi.SearchSongsAsync(q, 1, null, ct).ConfigureAwait(false);
        }
        return Best(raw, found);
    }

    /// <summary>
    /// Legge la playlist e trova i brani su YouTube Music. I brani già noti (knownKeys e knownItems) non vengono cercati di nuovo.
    /// </summary>
    public static async Task<Imported> ImportAsync(Ref r,
        IReadOnlyDictionary<string, string>? knownKeys = null,
        IReadOnlyDictionary<string, Track>? knownItems = null,
        Action<Meta, List<Track>, int, int>? progress = null,
        CancellationToken ct = default)
    {
        knownKeys ??= new Dictionary<string, string>();
        knownItems ??= new Dictionary<string, Track>();
        if (r.Source == Source.YouTube)
        {
            var tracks = await YtApi.PlaylistTracksAsync("VL" + r.Id, ct).ConfigureAwait(false);
            if (tracks.Count == 0) throw new IOException("YouTube: playlist vuota o non leggibile");
            var (title, cover) = await PlaylistMetaAsync(r.Id, ct).ConfigureAwait(false);
            var meta = new Meta(title.Length > 0 ? title : "Playlist YouTube", "", cover.Length > 0 ? cover : tracks[0].Cover);
            progress?.Invoke(meta, tracks, tracks.Count, tracks.Count);
            return new Imported(meta, tracks, tracks.Count, new());
        }
        var f = r.Source == Source.Deezer ? await FetchDeezerAsync(r.Id, ct).ConfigureAwait(false) : await FetchSpotifyAsync(r.Id, ct).ConfigureAwait(false);
        if (f.Tracks.Count == 0) throw new IOException($"{r.Source.Label()}: nessun brano letto");
        var found = new List<Track>();
        var seen = new HashSet<string>();
        var keys = new Dictionary<string, string>();
        progress?.Invoke(f.Meta, found, 0, f.Tracks.Count);
        for (var i = 0; i < f.Tracks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var raw = f.Tracks[i];
            var key = RawKey(raw);
            Track? item;
            if (knownKeys.TryGetValue(key, out var known))
            {
                if (known == "") item = null;
                else if (knownItems.TryGetValue(known, out var ki)) item = ki;
                else { item = await MatchStrictAsync(raw, ct).ConfigureAwait(false); await Task.Delay(120, ct).ConfigureAwait(false); }
            }
            else { item = await MatchStrictAsync(raw, ct).ConfigureAwait(false); await Task.Delay(120, ct).ConfigureAwait(false); }
            keys[key] = item?.VideoId ?? "";
            if (item != null && seen.Add(item.VideoId)) found.Add(item);
            progress?.Invoke(f.Meta, found.ToList(), i + 1, f.Tracks.Count);
        }
        return new Imported(f.Meta, found, f.Tracks.Count, keys);
    }

    /// <summary>Titolo e copertina di una playlist YouTube (dalla pagina di YouTube Music).</summary>
    public static async Task<(string Title, string Cover)> PlaylistMetaAsync(string listId, CancellationToken ct = default)
    {
        try
        {
            var j = await YtApi.BrowseAsync("VL" + listId, ct: ct).ConfigureAwait(false);
            var title = J.Runs(J.Collect(j, "musicResponsiveHeaderRenderer").FirstOrDefault()?["title"]);
            if (title.Length == 0) title = J.Runs(J.Collect(j, "musicDetailHeaderRenderer").FirstOrDefault()?["title"]);
            if (title.Length == 0) title = J.Runs(J.Collect(j, "musicEditablePlaylistDetailHeaderRenderer").FirstOrDefault()?["title"]);
            var cover = YtApi.ThumbOf(J.Collect(j, "musicResponsiveHeaderRenderer").FirstOrDefault()
                                       ?? J.Collect(j, "musicDetailHeaderRenderer").FirstOrDefault() as JsonNode);
            return (title, cover);
        }
        catch (Exception) { return ("", ""); }
    }

    /// <summary>Il sottotitolo di una playlist importata, per la Home e la sua pagina.</summary>
    public static string Subtitle(Source s, int found, int total) =>
        found == total ? $"Importata da {s.Label()} · {total} brani" : $"Importata da {s.Label()} · {found} di {total} brani";
}
