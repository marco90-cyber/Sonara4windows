namespace Sonara.Core.Yt;

/// <summary>Playlist «In evidenza» della Home: le classifiche di Spotify e Deezer per il paese scelto. Porting di Featured.kt.</summary>
public static class Featured
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);

    private sealed record Spot(string Id, string Title);

    private static readonly Spot GlobalTop = new("37i9dQZEVXbMDoHDwVN2tF", "Top 50 · Mondo");
    private static readonly Dictionary<string, Spot> CountryTop = new() { ["IT"] = new("37i9dQZEVXbIQnj7RRhdSX", "Top 50 · Italia") };
    private static readonly Spot GlobalSummer = new("37i9dQZF1DX4zbZrYRGVam", "Songs of Summer");
    private static readonly Dictionary<string, Spot> CountrySummer = new() { ["IT"] = new("37i9dQZF1DX3sDhLvLnzj8", "Estate Italiana") };
    private static readonly Spot[] Fixed = { new("37i9dQZF1DXcBWIGoYBM5M", "Today's Top Hits"), new("37i9dQZF1DX4JAvHpjipBk", "New Music Friday") };

    private static string _cacheCountry = "";
    private static DateTime _cacheAt = DateTime.MinValue;
    private static List<Entry> _cache = new();

    private static List<Spot> Spots(string country) =>
        new[] { CountryTop.GetValueOrDefault(country) ?? GlobalTop }.Concat(Fixed).Append(CountrySummer.GetValueOrDefault(country) ?? GlobalSummer).ToList();

    private static Entry EntryOf(Spot s, string cover = "") =>
        new(Kind.Playlists, new PlaylistImport.Ref(PlaylistImport.Source.Spotify, s.Id).BrowseId, s.Title, "Spotify", cover);

    /// <summary>Subito, senza rete: le playlist di Spotify (senza copertina).</summary>
    public static List<Entry> SpotifyNow(string country) => Spots(country).Select(s => EntryOf(s)).ToList();

    public static async Task<List<Entry>> LoadAsync(string country, Action<List<Entry>>? onPartial = null, CancellationToken ct = default)
    {
        if (_cacheCountry == country && _cache.Count > 0 && DateTime.UtcNow - _cacheAt < Ttl) return _cache;
        var spots = Spots(country);
        var baseList = SpotifyNow(country);
        onPartial?.Invoke(baseList);
        var charts = await PlaylistImport.DeezerChartsAsync(6, ct).ConfigureAwait(false);
        var deezer = charts.GroupBy(c => c.BrowseId).Select(g => g.First()).ToList();
        onPartial?.Invoke(baseList.Concat(deezer).ToList());
        var covered = new List<Entry>();
        for (var i = 0; i < spots.Count; i++)
        {
            var cover = "";
            try { cover = (await PlaylistImport.FetchSpotifyAsync(spots[i].Id, ct).ConfigureAwait(false)).Meta.Cover; } catch (Exception) { }
            covered.Add(EntryOf(spots[i], cover.Length > 0 ? cover : baseList[i].Thumb));
        }
        var all = covered.Concat(deezer).ToList();
        _cacheCountry = country; _cacheAt = DateTime.UtcNow; _cache = all;
        return all;
    }
}
