using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sonara.Core.Yt;

/// <summary>Confronto di titoli e durate (senza rete, quindi collaudabile): serve a capire se un brano ha la durata giusta e a scegliere l'audio più simile.</summary>
public static class DurationMatch
{
    private static readonly Regex Brackets = new(@"[(\[].*?[)\]]", RegexOptions.Compiled);
    private static readonly Regex Suffix = new(@"\s[-–]\s(remaster(ed)?|live|radio edit|single version|album version|version|\d{4}).*$", RegexOptions.Compiled);
    private static readonly Regex Feat = new(@"\b(feat|ft|featuring)\b\.?.*$", RegexOptions.Compiled);
    private static readonly Regex NonAlnum = new(@"[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex SplitArtists = new(@"\s*(,|&|\bfeat\.?\b|\bft\.?\b|\bx\b)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Unwanted = new(@"\b(bootleg|unofficial|un official|non ufficiale|cover|covers|tribute|fan made|fanmade|mashup|mash up|sped up|speed up|slowed|nightcore|8d|dj mix)\b", RegexOptions.Compiled);
    private static readonly Regex Remix = new(@"\b(remix|remixed|rmx)\b", RegexOptions.Compiled);
    private static readonly Regex OfficialRemix = new(@"\b(official remix|remix official|remix ufficiale|ufficiale remix)\b", RegexOptions.Compiled);

    private static string Strip(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Titolo o artista «pulito»: minuscolo, senza accenti, parentesi, «feat.» e «- Remastered».</summary>
    public static string Norm(string s)
    {
        var t = Strip(s);
        t = Brackets.Replace(t, " "); t = Suffix.Replace(t, ""); t = Feat.Replace(t, "");
        return NonAlnum.Replace(t, " ").Trim();
    }

    private static HashSet<string> Tokens(string s) => Norm(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    public static double TitleSim(string a, string b)
    {
        var x = Tokens(a); var y = Tokens(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        return (double)x.Intersect(y).Count() / x.Union(y).Count();
    }

    public static bool SameTitle(string a, string b) { var n = Norm(a); return n.Length > 0 && n == Norm(b); }

    public static bool ArtistMatch(string a, string b) => Tokens(a).Intersect(Tokens(b)).Any(t => t.Length > 1);

    private static string Plain(string s) => NonAlnum.Replace(Strip(s), " ");

    private static HashSet<string> UnwantedIn(string text) => Unwanted.Matches(Plain(text)).Select(m => m.Value).ToHashSet();

    /// <summary>La versione è da scartare (bootleg, cover, tribute, mashup, sped up, nightcore, 8D, DJ mix, remix non ufficiali…), salvo che la parola compaia in <paramref name="context"/>.</summary>
    public static bool IsUnwanted(string title, string artist, string origArtist, params string[] context)
    {
        var allowed = context.SelectMany(UnwantedIn).ToHashSet();
        if (UnwantedIn(title + " " + artist).Any(w => !allowed.Contains(w))) return true;
        var t = Plain(title);
        if (Remix.IsMatch(t) && !context.Any(c => Remix.IsMatch(Plain(c))))
        {
            var official = OfficialRemix.IsMatch(t) || ArtistMatch(origArtist, artist);
            if (!official) return true;
        }
        return false;
    }

    public static string MainArtist(string artist)
    {
        var p = SplitArtists.Split(artist).Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0 && x != "," && x != "&");
        return p ?? artist.Trim();
    }

    public static long Tolerance(long refSec) => Math.Max(6, refSec * 3 / 100);

    public static bool IsOff(long currentSec, long refSec) => refSec > 0 && currentSec > 0 && Math.Abs(currentSec - refSec) > Tolerance(refSec);

    public sealed record Source(Track Item, bool IsVideo);
    public sealed record Pick(Source Source, double Score, bool Sure);

    public static Pick? Best(string title, string artist, long refSec, IEnumerable<Source> candidates, string? excludeId = null)
    {
        var tol = Tolerance(refSec);
        return candidates
            .Where(c => c.Item.VideoId != excludeId && c.Item.DurationSec > 0)
            .Where(c => !IsUnwanted(c.Item.Title, c.Item.Artist, artist, title))
            .Where(c => Math.Abs(c.Item.DurationSec - refSec) <= tol)
            .Where(c => TitleSim(title, c.Item.Title) >= 0.6)
            .Select(c =>
            {
                var d = Math.Abs(c.Item.DurationSec - refSec);
                var sure = d <= 3 && SameTitle(title, c.Item.Title) && ArtistMatch(artist, c.Item.Artist) && !c.IsVideo;
                return new Pick(c, 100.0 - d * 5 - (c.IsVideo ? 10 : 0) + TitleSim(title, c.Item.Title) * 20, sure);
            })
            .OrderByDescending(p => p.Score).FirstOrDefault();
    }

    /// <summary>Candidati per un brano: prima i brani, poi i video.</summary>
    public static async Task<List<Source>> SearchSourcesAsync(string query, CancellationToken ct = default)
    {
        var songs = await YtApi.SearchSongsAsync(query, 1, null, ct).ConfigureAwait(false);
        var vids = new List<Track>();
        try { vids = await YtApi.SearchVideosAsync(query, 1, ct).ConfigureAwait(false); } catch (Exception) { }
        var ids = songs.Select(s => s.VideoId).ToHashSet();
        return songs.Select(s => new Source(s, false)).Concat(vids.Where(v => !ids.Contains(v.VideoId)).Select(v => new Source(v, true))).ToList();
    }
}

/// <summary>Durata di riferimento di un brano da Deezer e, in seconda battuta, da iTunes (nessuna chiave).</summary>
public static class DurationRef
{
    public sealed record Ref(long Seconds, string Source);
    public sealed record Lookup(Ref? Ref, bool Reachable);

    private static readonly ConcurrentDictionary<string, Ref?> Cache = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public static async Task<Lookup> LookupAsync(string title, string artist, CancellationToken ct = default)
    {
        var key = DurationMatch.Norm(title) + "|" + DurationMatch.Norm(artist);
        if (Cache.TryGetValue(key, out var hit)) return new Lookup(hit, true);
        var main = DurationMatch.MainArtist(artist);
        var answered = false;
        foreach (var url in new[]
        {
            "https://api.deezer.com/search?limit=8&q=" + Uri.EscapeDataString($"track:\"{title}\" artist:\"{main}\""),
            "https://api.deezer.com/search?limit=8&q=" + Uri.EscapeDataString($"{title} {main}"),
        })
        {
            var j = await GetJson(url, ct).ConfigureAwait(false);
            if (j == null) continue;
            answered = true;
            var r = Parse(j["data"] as JsonArray, "title", "duration", false, title, artist, "Deezer", o => o["artist"]?["name"]?.ToString() ?? "");
            if (r != null) { Cache[key] = r; return new Lookup(r, true); }
        }
        var it = await GetJson("https://itunes.apple.com/search?entity=song&limit=8&country=IT&term=" + Uri.EscapeDataString($"{title} {main}"), ct).ConfigureAwait(false);
        if (it != null)
        {
            answered = true;
            var r = Parse(it["results"] as JsonArray, "trackName", "trackTimeMillis", true, title, artist, "iTunes", o => o["artistName"]?.ToString() ?? "");
            if (r != null) { Cache[key] = r; return new Lookup(r, true); }
        }
        if (answered) Cache[key] = null;
        return new Lookup(null, answered);
    }

    private static async Task<JsonNode?> GetJson(string url, CancellationToken ct)
    {
        try { return JsonNode.Parse(await Http.GetStringAsync(url, ct).ConfigureAwait(false)); }
        catch (Exception) { return null; }
    }

    private static Ref? Parse(JsonArray? a, string titleKey, string durKey, bool millis, string title, string artist, string source, Func<JsonNode, string> artistOf)
    {
        if (a == null) return null;
        foreach (var o in a)
        {
            if (o == null) continue;
            long raw; try { raw = o[durKey]?.GetValue<long>() ?? 0; } catch (Exception) { continue; }
            var sec = millis ? raw / 1000 : raw;
            if (sec <= 0) continue;
            if (DurationMatch.TitleSim(title, o[titleKey]?.ToString() ?? "") < 0.6) continue;
            if (!DurationMatch.ArtistMatch(artist, artistOf(o))) continue;
            return new Ref(sec, source);
        }
        return null;
    }
}

/// <summary>Controllo delle durate di un elenco di brani.</summary>
public static class DurationCheck
{
    public enum Status { Ok, Off, NoRef }
    public sealed record Row(Track Item, long CurrentSec, DurationRef.Ref? Ref, Status Status, DurationMatch.Pick? Pick);

    public static async Task<List<Row>> ScanAsync(IEnumerable<Track> items, Data.YtOverrides overrides, Action<int, int>? progress = null, CancellationToken ct = default)
    {
        var list = items.DistinctBy(t => t.VideoId).ToList();
        var gate = new SemaphoreSlim(3);
        var done = 0;
        var tasks = list.Select(async raw =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try { return await RowFor(raw, overrides, ct).ConfigureAwait(false); }
            finally { gate.Release(); progress?.Invoke(Interlocked.Increment(ref done), list.Count); }
        });
        return (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
    }

    private static async Task<Row> RowFor(Track orig, Data.YtOverrides ov, CancellationToken ct)
    {
        var cur = ov.Apply(orig).DurationSec;
        var look = await DurationRef.LookupAsync(orig.Title, orig.Artist, ct).ConfigureAwait(false);
        if (look.Ref == null) return new Row(orig, cur, null, Status.NoRef, null);
        if (!DurationMatch.IsOff(cur, look.Ref.Seconds)) return new Row(orig, cur, look.Ref, Status.Ok, null);
        List<DurationMatch.Source> cand;
        try { cand = await DurationMatch.SearchSourcesAsync($"{orig.Title} {DurationMatch.MainArtist(orig.Artist)}", ct).ConfigureAwait(false); }
        catch (Exception) { cand = new(); }
        var pick = DurationMatch.Best(orig.Title, orig.Artist, look.Ref.Seconds, cand, ov.Target(orig.VideoId));
        return new Row(orig, cur, look.Ref, Status.Off, pick);
    }
}
