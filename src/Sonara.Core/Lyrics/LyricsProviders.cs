using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sonara.Core.Lyrics;

public sealed record LyricsQuery(string Title, string Artist, string Album, int DurationSec);

/// <summary>Testi da LRCLIB (archivio libero di testi sincronizzati riga per riga).</summary>
public static class LyricsProviders
{
    private static readonly HttpClient Http = MakeClient();
    private static readonly Regex NotAlnum = new(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);
    private static readonly Regex Brackets = new(@"\s*[\(\[][^\)\]]*[\)\]]", RegexOptions.Compiled);
    private static readonly Regex Dash = new(@"\s+-\s+.*$", RegexOptions.Compiled);

    private static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Sonara-Windows/0.1 (https://github.com/marco90-cyber/Sonara4windows)");
        return c;
    }

    public static string CleanTitle(string raw)
    {
        var t = Brackets.Replace(raw, "");
        t = Dash.Replace(t, "");
        return t.Trim();
    }

    public static string CleanArtist(string raw) => raw.Split(new[] { ", ", " & ", " feat. ", " ft. " }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? raw;

    private static string Norm(string s) => NotAlnum.Replace(s.ToLowerInvariant(), "");

    private static LyricsText? Record(JsonNode? o)
    {
        if (o == null) return null;
        var synced = J.Str(o["syncedLyrics"]);
        if (synced.Length > 0) return Lrc.Parse(synced, "LRCLIB");
        var plain = J.Str(o["plainLyrics"]);
        return plain.Length > 0 ? Lrc.Parse(plain, "LRCLIB") : null;
    }

    private static async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var r = await Http.GetAsync(url, ct).ConfigureAwait(false);
            return r.IsSuccessStatusCode ? await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false) : null;
        }
        catch (Exception) { return null; }
    }

    private static string Url(string baseUrl, params (string K, string V)[] q) =>
        baseUrl + "?" + string.Join("&", q.Where(p => p.V.Length > 0).Select(p => $"{p.K}={Uri.EscapeDataString(p.V)}"));

    /// <summary>Cerca il testo di un brano su LRCLIB (prima la corrispondenza esatta, poi la ricerca). Null se non c'è.</summary>
    public static async Task<LyricsText?> FetchAsync(LyricsQuery q, CancellationToken ct = default)
    {
        var title = CleanTitle(q.Title);
        var artist = CleanArtist(q.Artist);
        if (title.Length == 0) return null;

        var exact = await GetAsync(Url("https://lrclib.net/api/get",
            ("track_name", title), ("artist_name", artist), ("album_name", q.Album),
            ("duration", q.DurationSec > 0 ? q.DurationSec.ToString() : "")), ct).ConfigureAwait(false);
        if (exact != null)
        {
            var rec = Record(JsonNode.Parse(exact));
            if (rec != null && !rec.IsEmpty) return rec;
        }

        var body = await GetAsync(Url("https://lrclib.net/api/search", ("track_name", title), ("artist_name", artist)), ct).ConfigureAwait(false)
                   ?? await GetAsync(Url("https://lrclib.net/api/search", ("q", $"{artist} {title}")), ct).ConfigureAwait(false);
        if (body == null || JsonNode.Parse(body) is not JsonArray arr) return null;

        var cands = arr.OfType<JsonObject>()
            .Where(o => Norm(J.Str(o["trackName"])).Contains(Norm(title)) || Norm(title).Contains(Norm(J.Str(o["trackName"]))))
            .OrderBy(o => q.DurationSec > 0 && o["duration"] is JsonValue dv && dv.TryGetValue<double>(out var d) ? Math.Abs(d - q.DurationSec) : 999)
            .ThenByDescending(o => J.Str(o["syncedLyrics"]).Length > 0)
            .ToList();
        foreach (var c in cands)
        {
            var r = Record(c);
            if (r != null && !r.IsEmpty) return r;
        }
        return null;
    }
}
