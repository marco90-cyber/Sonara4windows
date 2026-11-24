using Sonara.Core;
using Sonara.Core.Data;
using Sonara.Core.Yt;
using Xunit;

public class TasteTests
{
    private static Track T(string id, string artist) => new(id, "t" + id, artist, 200, "");

    [Fact]
    public void WeightsAndTopArtists()
    {
        var plays = new Dictionary<string, Play> { ["a"] = new Play(T("a", "Daft Punk"), 5, 1, 2) };
        var w = Taste.Weights(plays, new[] { T("b", "Daft Punk, Pharrell") }, new[] { T("a", "Daft Punk") });
        Assert.Equal(7.0, w["a"].Weight);
        var top = Taste.TopArtists(w, 3);
        Assert.Equal("Daft Punk", top[0].Name);
    }

    [Fact]
    public void PointsCapListensAndSkips()
    {
        var plays = new Dictionary<string, Play> { ["a"] = new Play(T("a", "X"), 12, 1, 2) };
        var skips = new Dictionary<string, int> { ["a"] = 5, ["h"] = 1 };
        var w = Taste.Weights(plays, new[] { T("h", "Y") }, Array.Empty<Track>(), skips);
        Assert.Equal(4.0, w["a"].Weight);   // 5 ascolti (tetto) - 1 (tetto dei salti)
        Assert.Equal(0.0, w["h"].Weight);   // in cronologia ma saltato
    }

    [Fact]
    public void NegativeArtistIsExcluded()
    {
        var p = Taste.BuildProfile(new Dictionary<string, Play>(), new[] { T("a", "Z") }, Array.Empty<Track>(),
            new Dictionary<string, int>(), new[] { T("d1", "Z"), T("d2", "Z") });
        Assert.True(p.Bad(T("new", "Z")));      // 1 - 2 < 0
        Assert.True(p.Bad(T("d1", "Q")));       // brano non gradito
        Assert.False(p.Bad(T("new", "W")));
        Assert.DoesNotContain("d1", p.Weights.Keys);
    }

    [Fact]
    public void GenreScoresSumWeights()
    {
        var plays = new Dictionary<string, Play> { ["a"] = new Play(T("a", "X"), 3, 1, 2), ["b"] = new Play(T("b", "Y"), 1, 1, 2) };
        var p = Taste.BuildProfile(plays, Array.Empty<Track>(), Array.Empty<Track>(), new Dictionary<string, int>(), new[] { T("d", "Q") });
        var g = Taste.GenreScores(p, t => t.VideoId == "a" || t.VideoId == "d" ? "rock" : t.VideoId == "b" ? "pop" : null);
        Assert.Equal(2.0, g["rock"]);   // 3 - 1
        Assert.Equal(1.0, g["pop"]);
    }

    [Fact]
    public void ExcluderBlocksArtistAfterThree()
    {
        var bad = Taste.Excluder(new[] { T("1", "X"), T("2", "X"), T("3", "X") });
        Assert.True(bad(T("9", "X")));
        Assert.False(bad(T("9", "Y")));
    }

    [Fact]
    public void ImportLinks()
    {
        Assert.Equal("dz:123", PlaylistImport.ParseLink("https://www.deezer.com/it/playlist/123")?.BrowseId);
        Assert.Equal(PlaylistImport.Source.Spotify, PlaylistImport.ParseLink("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M?si=x")?.Source);
        Assert.Equal(PlaylistImport.Source.YouTube, PlaylistImport.ParseLink("https://music.youtube.com/playlist?list=PLabc123")?.Source);
        Assert.Null(PlaylistImport.ParseLink("ciao"));
        Assert.Equal("dz", PlaylistImport.RefOf("imp:dz:5")?.Source.Prefix());
    }

    [Fact]
    public void MatchScoring()
    {
        var raw = new PlaylistImport.Raw("Get Lucky - Radio Edit", "Daft Punk", 248);
        var good = new Track("a", "Get Lucky (Radio Edit)", "Daft Punk", 249, "");
        var bad = new Track("b", "Other Song", "Daft Punk", 249, "");
        Assert.Same(good, PlaylistImport.Best(raw, new[] { bad, good }));
        Assert.Null(PlaylistImport.Best(raw, new[] { bad }));
    }

    [Fact]
    public void StoreRoundTrip()
    {
        var p = Path.Combine(Path.GetTempPath(), "sonara-store-" + Guid.NewGuid() + ".json");
        var s = new YtStore(p);
        s.ToggleFavorite(T("a", "X"));
        s.RecordPlay(T("a", "X"));
        s.ToggleSaved(new Entry(Kind.Albums, "MPRE1", "A", "B", ""));
        var s2 = new YtStore(p);
        Assert.True(s2.IsFavorite("a"));
        Assert.Equal(1, s2.Plays["a"].Count);
        Assert.True(s2.IsSaved("MPRE1"));
        var pl = s2.CreatePlaylist("Mia");
        Assert.True(s2.AddToPlaylist(pl.Id, T("z", "Q")));
        Assert.False(s2.AddToPlaylist(pl.Id, T("z", "Q")));
        s2.RecordSkip(T("z", "Q"));
        var s3 = new YtStore(p);
        Assert.Single(s3.MyPlaylists[0].Items);
        Assert.Equal(1, s3.Skips["z"]);
        File.Delete(p);
    }
}
