using Sonara.Core;
using Sonara.Core.Yt;
using Xunit;

public class DurationMatchTests
{
    [Fact] public void NormStripsNoise() => Assert.Equal("get lucky", DurationMatch.Norm("Get Lucky (feat. Pharrell) - Remastered 2013"));
    [Fact] public void MainArtistSplits() => Assert.Equal("Fedez", DurationMatch.MainArtist("Fedez, Marco Masini"));
    [Fact] public void Tolerance() { Assert.Equal(6, DurationMatch.Tolerance(100)); Assert.Equal(9, DurationMatch.Tolerance(300)); }
    [Fact] public void IsOffWorks() { Assert.True(DurationMatch.IsOff(250, 300)); Assert.False(DurationMatch.IsOff(305, 300)); Assert.False(DurationMatch.IsOff(0, 300)); }
    [Fact] public void UnwantedDetected()
    {
        Assert.True(DurationMatch.IsUnwanted("Song (Nightcore)", "X", "Y", "Song"));
        Assert.False(DurationMatch.IsUnwanted("Song (Nightcore)", "X", "Y", "Song nightcore"));
        Assert.True(DurationMatch.IsUnwanted("Song (DJ Remix)", "Someone", "Artist", "Song"));
        Assert.False(DurationMatch.IsUnwanted("Song", "Artist - Topic", "Artist", "Song"));
    }
    [Fact] public void BestPicksSure()
    {
        var good = new DurationMatch.Source(new Track("a", "Get Lucky", "Daft Punk", 369, ""), false);
        var vid = new DurationMatch.Source(new Track("b", "Get Lucky", "Daft Punk", 369, ""), true);
        var bad = new DurationMatch.Source(new Track("c", "Get Lucky (Cover)", "Someone", 369, ""), false);
        var p = DurationMatch.Best("Get Lucky", "Daft Punk", 369, new[] { vid, bad, good });
        Assert.NotNull(p); Assert.Equal("a", p!.Source.Item.VideoId); Assert.True(p.Sure);
    }
}
