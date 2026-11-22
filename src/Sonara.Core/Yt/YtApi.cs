using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sonara.Core.Yt;

/// <summary>Chiamate a YouTube Music (InnerTube, client WEB_REMIX), senza account. Porting di YtApi.kt.</summary>
public static class YtApi
{
    private const string Origin = "https://music.youtube.com";
    private const string Key = "AIzaSyC9XL3ZjWddXya6X74dJoCTL-WEYFDNX30";
    private const string ClientVersion = "1.20250929.01.00";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    private static readonly Regex ThumbSize = new(@"=(w\d+-h\d+|s\d+)", RegexOptions.Compiled);

    // parametri di ricerca di YouTube Music per i filtri (gli stessi usati da ytmusicapi)
    private const string FilterSongs = "EgWKAQIIAWoMEA4QChADEAQQCRAF";
    private const string FilterVideos = "EgWKAQIQAWoMEA4QChADEAQQCRAF";
    private const string FilterAlbums = "EgWKAQIYAWoMEA4QChADEAQQCRAF";
    private const string FilterArtists = "EgWKAQIgAWoMEA4QChADEAQQCRAF";
    private const string FilterPlaylists = "EgeKAQQoAEABagwQDhAKEAMQBBAJEAU%3D";

    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var c = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(25),
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        c.DefaultRequestHeaders.Add("Origin", Origin);
        c.DefaultRequestHeaders.Referrer = new Uri(Origin + "/");
        c.DefaultRequestHeaders.Add("X-Origin", Origin);
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("it-IT,it;q=0.9");
        return c;
    }

    /// <summary>Paese dei consigliati (codice ISO).</summary>
    public static string Country { get; set; } = "IT";

    /// <summary>La miniatura di Google con la misura nell'indirizzo: ne chiedo una nitida da 544 px.</summary>
    public static string BigThumb(string url) =>
        url.Contains("googleusercontent.com") || url.Contains("ggpht.com") ? ThumbSize.Replace(url, "=w544-h544") : url;

    /// <summary>POST a un endpoint InnerTube (senza account).</summary>
    public static async Task<JsonNode> PostAsync(string endpoint, JsonObject body, string? continuation = null, CancellationToken ct = default)
    {
        body["context"] = new JsonObject
        {
            ["client"] = new JsonObject
            {
                ["clientName"] = "WEB_REMIX",
                ["clientVersion"] = ClientVersion,
                ["hl"] = "it",
                ["gl"] = Country,
            },
        };
        var url = $"{Origin}/youtubei/v1/{endpoint}?key={Key}&prettyPrint=false";
        if (continuation != null) url += $"&ctoken={continuation}&continuation={continuation}&type=next";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new IOException($"YouTube Music: HTTP {(int)resp.StatusCode}");
        return JsonNode.Parse(text) ?? throw new IOException("YouTube Music: risposta vuota");
    }

    public static Task<JsonNode> BrowseAsync(string? browseId, string? continuation = null, string? @params = null, CancellationToken ct = default)
    {
        var b = new JsonObject();
        if (browseId != null) b["browseId"] = browseId;
        if (@params != null) b["params"] = @params;
        return PostAsync("browse", b, continuation, ct);
    }

    // ------------------------------------------------------------------ ricerca

    private static async Task<List<JsonNode>> SearchPagesAsync(string query, string? filter, int maxPages, Action<JsonNode>? onPage, CancellationToken ct)
    {
        var pages = new List<JsonNode>();
        var body = new JsonObject { ["query"] = query };
        if (filter != null) body["params"] = Uri.UnescapeDataString(filter);
        var json = await PostAsync("search", body, null, ct).ConfigureAwait(false);
        pages.Add(json);
        onPage?.Invoke(json);
        for (var n = 1; n < maxPages; n++)
        {
            var next = NextToken(json);
            if (next == null) break;
            try { json = await PostAsync("search", new JsonObject(), next, ct).ConfigureAwait(false); }
            catch (Exception) { break; }
            pages.Add(json);
            onPage?.Invoke(json);
        }
        return pages;
    }

    /// <summary>Cerca brani (solo canzoni), su più pagine. <paramref name="progress"/> riceve l'elenco parziale dopo ogni pagina.</summary>
    public static async Task<List<Track>> SearchSongsAsync(string query, int maxPages = 4, Action<List<Track>>? progress = null, CancellationToken ct = default)
        => await SearchTracksAsync(query, FilterSongs, maxPages, progress, ct).ConfigureAwait(false);

    /// <summary>Cerca i video musicali (per «Cambia sorgente»).</summary>
    public static async Task<List<Track>> SearchVideosAsync(string query, int maxPages = 2, CancellationToken ct = default)
        => await SearchTracksAsync(query, FilterVideos, maxPages, null, ct).ConfigureAwait(false);

    private static async Task<List<Track>> SearchTracksAsync(string query, string filter, int maxPages, Action<List<Track>>? progress, CancellationToken ct)
    {
        var all = new List<Track>();
        var seen = new HashSet<string>();
        await SearchPagesAsync(query, filter, maxPages, page =>
        {
            foreach (var o in J.Collect(page, "musicResponsiveListItemRenderer"))
            {
                var t = ParseTrack(o);
                if (t != null && seen.Add(t.VideoId)) all.Add(t);
            }
            progress?.Invoke(all.ToList());
        }, ct).ConfigureAwait(false);
        return all;
    }

    /// <summary>Cerca artisti, album o playlist, su più pagine.</summary>
    public static async Task<List<Entry>> SearchEntriesAsync(string query, Kind kind, int maxPages = 3, Action<List<Entry>>? progress = null, CancellationToken ct = default)
    {
        var filter = kind switch
        {
            Kind.Artists => FilterArtists,
            Kind.Albums => FilterAlbums,
            Kind.Playlists => FilterPlaylists,
            _ => FilterSongs,
        };
        var all = new List<Entry>();
        var seen = new HashSet<string>();
        await SearchPagesAsync(query, filter, maxPages, page =>
        {
            foreach (var o in J.Collect(page, "musicResponsiveListItemRenderer"))
            {
                var e = ParseEntry(o, kind);
                if (e != null && seen.Add(e.BrowseId)) all.Add(e);
            }
            progress?.Invoke(all.ToList());
        }, ct).ConfigureAwait(false);
        return all;
    }

    /// <summary>Suggerimenti di completamento mentre si scrive. Vuota se non riesce.</summary>
    public static async Task<List<string>> SuggestAsync(string query, CancellationToken ct = default)
    {
        var q = query.Trim();
        if (q.Length == 0) return new();
        try
        {
            var json = await PostAsync("music/get_search_suggestions", new JsonObject { ["input"] = q }, null, ct).ConfigureAwait(false);
            var list = new List<string>();
            foreach (var s in J.Collect(json, "searchSuggestionRenderer"))
            {
                var text = J.Runs(s["suggestion"]).Trim();
                if (text.Length > 0 && !list.Contains(text)) list.Add(text);
            }
            return list;
        }
        catch (Exception) { return new(); }
    }

    // ------------------------------------------------------------------ playlist e artisti

    /// <summary>Brani di una playlist (id "VL...", oppure "VLLM"), fino a ~1500.</summary>
    public static async Task<List<Track>> PlaylistTracksAsync(string browseId, CancellationToken ct = default)
    {
        var res = new List<Track>();
        var seen = new HashSet<string>();
        var json = await BrowseAsync(browseId, ct: ct).ConfigureAwait(false);
        for (var page = 0; page < 15; page++)
        {
            foreach (var o in J.Collect(json, "musicResponsiveListItemRenderer"))
            {
                var t = ParseTrack(o);
                if (t != null && seen.Add(t.VideoId)) res.Add(t);
            }
            var next = NextToken(json);
            if (next == null) break;
            json = await BrowseAsync(null, next, ct: ct).ConfigureAwait(false);
        }
        return res;
    }

    /// <summary>Pagina di un artista: i suoi brani e gli scaffali di album e singoli.</summary>
    public static async Task<Detail> ArtistAsync(string channelId, CancellationToken ct = default)
    {
        var json = await BrowseAsync(channelId, ct: ct).ConfigureAwait(false);
        var shelf = J.Collect(json, "musicShelfRenderer").FirstOrDefault();
        var songs = shelf == null ? new List<Track>()
            : J.Collect(shelf, "musicResponsiveListItemRenderer").Select(ParseTrack).Where(t => t != null).Cast<Track>().ToList();
        string? moreId = null;
        if (shelf != null)
        {
            moreId = J.Str(J.Collect(shelf, "bottomEndpoint").FirstOrDefault()?["browseEndpoint"]?["browseId"]);
            if (string.IsNullOrWhiteSpace(moreId))
                moreId = J.Str(J.Collect(shelf, "title").FirstOrDefault()?["runs"]?[0]?["navigationEndpoint"]?["browseEndpoint"]?["browseId"]);
        }
        if (!string.IsNullOrWhiteSpace(moreId))
        {
            try
            {
                var all = await PlaylistTracksAsync(moreId!, ct).ConfigureAwait(false);
                if (all.Count > songs.Count) songs = all;
            }
            catch (Exception) { /* resta l'elenco corto */ }
        }
        var shelves = J.Collect(json, "musicCarouselShelfRenderer").Select(ParseShelf).Where(s => s != null).Cast<Shelf>()
            .GroupBy(s => s.Title).Select(g => g.First()).ToList();
        return new Detail(songs, shelves);
    }

    /// <summary>Artisti simili (lo scaffale della pagina dell'artista fatto solo di artisti).</summary>
    public static async Task<List<Entry>> SimilarArtistsAsync(string channelId, CancellationToken ct = default)
    {
        var json = await BrowseAsync(channelId, ct: ct).ConfigureAwait(false);
        var shelves = J.Collect(json, "musicCarouselShelfRenderer").Select(ParseShelf).Where(s => s != null).Cast<Shelf>();
        var sim = shelves.FirstOrDefault(s => s.Cards.Count > 0 && s.Cards.All(c => c.Entry?.Kind == Kind.Artists));
        return sim?.Cards.Select(c => c.Entry!).ToList() ?? new();
    }

    // ------------------------------------------------------------------ home e radio

    /// <summary>Home di YouTube Music: gli scaffali di suggerimenti.</summary>
    public static async Task<List<Shelf>> HomeAsync(CancellationToken ct = default)
    {
        var res = new List<Shelf>();
        var json = await BrowseAsync("FEmusic_home", ct: ct).ConfigureAwait(false);
        for (var page = 0; page < 3; page++)
        {
            foreach (var sh in J.Collect(json, "musicCarouselShelfRenderer")) { var s = ParseShelf(sh); if (s != null) res.Add(s); }
            var next = NextToken(json);
            if (next == null) break;
            json = await BrowseAsync(null, next, ct: ct).ConfigureAwait(false);
        }
        return res.GroupBy(s => s.Title).Select(g => g.First()).ToList();
    }

    /// <summary>I filtri in alto («Relax», «Energia pura»…) e gli scaffali della pagina <paramref name="params"/>.</summary>
    public static async Task<(List<Chip> Chips, List<Shelf> Shelves)> HomeFeedAsync(string? @params = null, CancellationToken ct = default)
    {
        var json = await BrowseAsync("FEmusic_home", @params: @params, ct: ct).ConfigureAwait(false);
        var chips = new List<Chip>();
        foreach (var c in J.Collect(json, "chipCloudChipRenderer"))
        {
            var title = J.Runs(c["text"]);
            var p = J.Str(c["navigationEndpoint"]?["browseEndpoint"]?["params"]);
            if (title.Length > 0 && p.Length > 0 && !IsPodcast(title)) chips.Add(new Chip(title, p));
        }
        var shelves = J.Collect(json, "musicCarouselShelfRenderer").Select(ParseShelf).Where(s => s != null).Cast<Shelf>()
            .GroupBy(s => s.Title).Select(g => g.First()).ToList();
        return (chips.GroupBy(c => c.Title).Select(g => g.First()).ToList(), shelves);
    }

    /// <summary>Generi e atmosfere. Le voci hanno id «cat:browseId|params» e si aprono con <see cref="CategoryAsync"/>.</summary>
    public static async Task<List<Entry>> MoodsAndGenresAsync(CancellationToken ct = default)
    {
        var json = await BrowseAsync("FEmusic_moods_and_genres", ct: ct).ConfigureAwait(false);
        var res = new List<Entry>();
        foreach (var b in J.Collect(json, "musicNavigationButtonRenderer"))
        {
            var title = J.Runs(b["buttonText"]);
            var ep = b["clickCommand"]?["browseEndpoint"];
            var id = J.Str(ep?["browseId"]);
            var p = J.Str(ep?["params"]);
            if (title.Length == 0 || id.Length == 0 || IsPodcast(title)) continue;
            res.Add(new Entry(Kind.Playlists, $"cat:{id}|{p}", title, "", ""));
        }
        return res.GroupBy(e => e.BrowseId).Select(g => g.First()).ToList();
    }

    /// <summary>Gli scaffali di una categoria (id «cat:browseId|params»).</summary>
    public static async Task<Detail> CategoryAsync(string entryId, CancellationToken ct = default)
    {
        var raw = entryId.StartsWith("cat:") ? entryId[4..] : entryId;
        var bar = raw.IndexOf('|');
        var browseId = bar < 0 ? raw : raw[..bar];
        var p = bar < 0 ? null : raw[(bar + 1)..];
        if (string.IsNullOrEmpty(p)) p = null;
        var json = await BrowseAsync(browseId, @params: p, ct: ct).ConfigureAwait(false);
        var shelves = J.Collect(json, "musicCarouselShelfRenderer").Select(ParseShelf).Where(s => s != null).Cast<Shelf>()
            .GroupBy(s => s.Title).Select(g => g.First()).ToList();
        return new Detail(new(), shelves);
    }

    /// <summary>Nuove uscite (album e singoli).</summary>
    public static async Task<Shelf?> NewReleasesAsync(CancellationToken ct = default)
    {
        var json = await BrowseAsync("FEmusic_new_releases_albums", ct: ct).ConfigureAwait(false);
        var cards = J.Collect(json, "musicTwoRowItemRenderer")
            .Select(t => CardOf(new JsonObject { ["musicTwoRowItemRenderer"] = JsonNode.Parse(t.ToJsonString()) }))
            .Where(c => c != null).Cast<Card>().GroupBy(c => c.Id).Select(g => g.First()).Take(30).ToList();
        return cards.Count == 0 ? null : new Shelf("Nuove uscite", cards);
    }

    /// <summary>Classifiche (quelle del tuo paese o globali), al massimo 3 scaffali.</summary>
    public static async Task<List<Shelf>> ChartsAsync(CancellationToken ct = default)
    {
        var json = await BrowseAsync("FEmusic_charts", ct: ct).ConfigureAwait(false);
        return J.Collect(json, "musicCarouselShelfRenderer").Select(ParseShelf).Where(s => s != null).Cast<Shelf>()
            .GroupBy(s => s.Title).Select(g => g.First()).Take(3).ToList();
    }

    /// <summary>Radio di un brano: i brani che YouTube Music propone di seguito (il primo è il brano stesso).</summary>
    public static async Task<List<Track>> RadioAsync(string videoId, CancellationToken ct = default)
    {
        var json = await PostAsync("next", new JsonObject
        {
            ["videoId"] = videoId,
            ["playlistId"] = "RDAMVM" + videoId,
            ["isAudioOnly"] = true,
        }, null, ct).ConfigureAwait(false);
        var res = new List<Track>();
        var seen = new HashSet<string>();
        foreach (var o in J.Collect(json, "playlistPanelVideoRenderer"))
        {
            var id = J.Str(o["videoId"]);
            if (id.Length == 0 || !seen.Add(id)) continue;
            var title = J.Runs(o["title"]);
            var artist = J.Str(o["longBylineText"]?["runs"]?[0]?["text"]);
            var dur = J.Str(o["lengthText"]?["runs"]?[0]?["text"]);
            res.Add(new Track(id, title, artist, ParseDuration(dur), ThumbOf(o["thumbnail"])));
        }
        return res;
    }

    // ------------------------------------------------------------------ lettura delle risposte

    public static bool IsPodcast(string text)
    {
        var t = text.ToLowerInvariant();
        return t.Contains("podcast") || t.StartsWith("episodio") || t.StartsWith("episode");
    }

    internal static string? NextToken(JsonNode json)
    {
        var a = J.Str(J.Collect(json, "nextContinuationData").FirstOrDefault()?["continuation"]);
        if (a.Length > 0) return a;
        var b = J.Str(J.Collect(json, "continuationCommand").FirstOrDefault()?["token"]);
        return b.Length > 0 ? b : null;
    }

    internal static Shelf? ParseShelf(JsonObject sh)
    {
        var title = J.Runs(J.Collect(sh["header"], "title").FirstOrDefault());
        if (sh["contents"] is not JsonArray arr) return null;
        if (IsPodcast(title)) return null;
        var cards = new List<Card>();
        foreach (var n in arr) if (n is JsonObject o) { var c = CardOf(o); if (c != null) cards.Add(c); }
        var clean = cards.Where(c => !IsPodcast(c.Subtitle)).GroupBy(c => c.Id).Select(g => g.First()).ToList();
        return title.Length == 0 || clean.Count == 0 ? null : new Shelf(title, clean);
    }

    internal static Card? CardOf(JsonObject c)
    {
        if (c["musicResponsiveListItemRenderer"] is JsonObject r)
        {
            var t = ParseTrack(r);
            if (t != null) return new Card(t, null);
        }
        if (c["musicTwoRowItemRenderer"] is not JsonObject two) return null;
        var nav = two["navigationEndpoint"];
        var videoId = J.Str(nav?["watchEndpoint"]?["videoId"]);
        var browseId = J.Str(nav?["browseEndpoint"]?["browseId"]);
        var name = J.Runs(two["title"]);
        var sub = J.Runs(two["subtitle"]);
        var thumb = ThumbOf(two["thumbnailRenderer"]);
        if (name.Length == 0) return null;
        if (videoId.Length > 0)
        {
            var parts = sub.Split(" • ").Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            var artist = parts.Count > 1 && new[] { "brano", "song", "video" }.Contains(parts[0].ToLowerInvariant())
                ? parts[1] : parts.FirstOrDefault() ?? "";
            return new Card(new Track(videoId, name, artist, 0, thumb), null);
        }
        Kind kind;
        if (browseId.StartsWith("UC")) kind = Kind.Artists;
        else if (browseId.StartsWith("MPRE")) kind = Kind.Albums;
        else if (browseId.StartsWith("VL")) kind = Kind.Playlists;
        else return null;
        return new Card(null, new Entry(kind, browseId, name, sub, thumb));
    }

    internal static Entry? ParseEntry(JsonObject o, Kind kind)
    {
        var id = J.Str(o["navigationEndpoint"]?["browseEndpoint"]?["browseId"]);
        if (id.Length == 0) return null;
        var ok = kind switch
        {
            Kind.Artists => id.StartsWith("UC"),
            Kind.Albums => id.StartsWith("MPRE"),
            Kind.Playlists => id.StartsWith("VL"),
            _ => false,
        };
        if (!ok) return null;
        var flex = o["flexColumns"] as JsonArray;
        JsonNode? Col(int i) => flex != null && i < flex.Count ? flex[i]?["musicResponsiveListItemFlexColumnRenderer"]?["text"] : null;
        var title = J.Runs(Col(0));
        if (title.Length == 0) return null;
        var sub = kind == Kind.Artists ? "Artista" : J.Runs(Col(1)).Replace(" • ", " · ");
        return new Entry(kind, id, title, sub, ThumbOf(o["thumbnail"]));
    }

    internal static Track? ParseTrack(JsonObject o)
    {
        var id = J.Str(o["playlistItemData"]?["videoId"]);
        if (id.Length == 0) id = J.Str(J.Collect(o, "watchEndpoint").FirstOrDefault()?["videoId"]);
        if (id.Length == 0) return null;
        if (o["flexColumns"] is not JsonArray flex) return null;
        JsonNode? Col(int i) => i < flex.Count ? flex[i]?["musicResponsiveListItemFlexColumnRenderer"]?["text"] : null;
        var title = J.Str(Col(0)?["runs"]?[0]?["text"]);
        if (title.Length == 0) return null;
        var runs = Col(1)?["runs"] as JsonArray;
        var artists = new List<string>();
        var dur = "";
        if (runs != null)
        {
            foreach (var r in runs)
            {
                var text = J.Str(r?["text"]);
                var bid = J.Str(r?["navigationEndpoint"]?["browseEndpoint"]?["browseId"]);
                if (bid.StartsWith("UC")) artists.Add(text);
                if (Regex.IsMatch(text, @"^\d+:\d\d(:\d\d)?$")) dur = text;
            }
        }
        var artist = artists.Count > 0 ? string.Join(", ", artists) : J.Str(runs?[0]?["text"]);
        var fixedDur = J.Str(o["fixedColumns"]?[0]?["musicResponsiveListItemFixedColumnRenderer"]?["text"]?["runs"]?[0]?["text"]);
        if (fixedDur.Length > 0) dur = fixedDur;
        return new Track(id, title, artist, ParseDuration(dur), ThumbOf(o["thumbnail"]));
    }

    public static long ParseDuration(string s)
    {
        var p = s.Split(':').Select(x => long.TryParse(x.Trim(), out var v) ? (long?)v : null).Where(x => x != null).Select(x => x!.Value).ToArray();
        return p.Length switch { 2 => p[0] * 60 + p[1], 3 => p[0] * 3600 + p[1] * 60 + p[2], _ => 0 };
    }

    /// <summary>La miniatura più grande trovata sotto <paramref name="root"/>.</summary>
    public static string ThumbOf(JsonNode? root)
    {
        JsonArray? found = null;
        void Walk(JsonNode? n)
        {
            if (found != null) return;
            if (n is JsonObject o)
            {
                foreach (var kv in o)
                {
                    if (kv.Key == "thumbnails" && kv.Value is JsonArray a && a.Count > 0) { found = a; return; }
                    Walk(kv.Value);
                    if (found != null) return;
                }
            }
            else if (n is JsonArray arr)
            {
                foreach (var x in arr) { Walk(x); if (found != null) return; }
            }
        }
        Walk(root);
        if (found == null) return "";
        var url = J.Str(found[found.Count - 1]?["url"]);
        return ThumbSize.Replace(url, "=w544-h544");
    }
}
