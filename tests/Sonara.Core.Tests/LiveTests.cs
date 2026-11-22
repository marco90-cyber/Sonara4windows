using Sonara.Core.Lyrics;
using Sonara.Core.Yt;
using Xunit;
using Xunit.Abstractions;

/// <summary>Prove contro i servizi veri (solo in CI): controllano che le risposte di YouTube Music si leggano ancora.</summary>
[Trait("Category", "Live")]
public class LiveTests
{
    private readonly ITestOutputHelper _o;
    public LiveTests(ITestOutputHelper o) => _o = o;

    [Fact]
    public async Task SearchSongs()
    {
        var r = await YtApi.SearchSongsAsync("daft punk get lucky", 1);
        _o.WriteLine($"brani: {r.Count}");
        foreach (var t in r.Take(5)) _o.WriteLine($"  {t.VideoId} | {t.Title} | {t.Artist} | {t.DurationSec}s | {t.Thumb.Length > 0}");
        Assert.NotEmpty(r);
    }

    [Theory]
    [InlineData(Kind.Artists)]
    [InlineData(Kind.Albums)]
    [InlineData(Kind.Playlists)]
    public async Task SearchEntries(Kind k)
    {
        var r = await YtApi.SearchEntriesAsync("daft punk", k, 1);
        _o.WriteLine($"{k}: {r.Count}");
        foreach (var e in r.Take(4)) _o.WriteLine($"  {e.BrowseId} | {e.Title} | {e.Subtitle}");
        Assert.NotEmpty(r);
    }

    [Fact]
    public async Task Home()
    {
        var r = await YtApi.HomeAsync();
        _o.WriteLine($"scaffali: {r.Count}");
        foreach (var s in r.Take(5)) _o.WriteLine($"  {s.Title}: {s.Cards.Count}");
        Assert.NotEmpty(r);
    }

    [Fact]
    public async Task Radio()
    {
        var r = await YtApi.RadioAsync("4D7u5KF7SP8");
        _o.WriteLine($"radio: {r.Count}");
        Assert.NotEmpty(r);
    }

    [Fact]
    public async Task Suggest()
    {
        var r = await YtApi.SuggestAsync("daft p");
        _o.WriteLine(string.Join(", ", r));
        Assert.NotEmpty(r);
    }

    [Fact]
    public async Task Lyrics()
    {
        var r = await LyricsProviders.FetchAsync(new LyricsQuery("Get Lucky", "Daft Punk", "Random Access Memories", 369));
        _o.WriteLine($"testo: {r?.Kind} {r?.Lines.Count}");
        Assert.NotNull(r);
    }

    [Fact]
    public async Task Stream()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sonara-test");
        var res = new StreamResolver(dir) { Quality = 2 };
        var errors = new List<string>();
        foreach (var id in new[] { "dQw4w9WgXcQ", "jNQXAC9IVRw", "9bZkp7q19f0", "kJQP7kiw5Fk", "4D7u5KF7SP8" })
        {
            try
            {
                var r = await res.ResolveAsync(id);
                _o.WriteLine($"OK {id}: {r.FilePath} {r.Container} {r.BitrateKbps}kbps {new FileInfo(r.FilePath).Length} byte");
                Assert.True(new FileInfo(r.FilePath).Length > 10000);
                return;
            }
            catch (Exception e) { errors.Add($"{id}: {e.GetType().Name}: {e.Message}"); }
        }
        throw new Xunit.Sdk.XunitException("Nessun video riproducibile: " + string.Join(" | ", errors));
    }
}
