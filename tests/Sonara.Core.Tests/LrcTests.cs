using Sonara.Core.Lyrics;
using Xunit;

public class LrcTests
{
    [Fact]
    public void ParsesSyncedLines()
    {
        var l = Lrc.Parse("[00:01.00]Ciao\n[00:03.50]Mondo\n[00:06.00]Fine", "t");
        Assert.True(l.Synced);
        Assert.Equal(3, l.Lines.Count);
        Assert.Equal(1000, l.Lines[0].Start);
        Assert.Equal(3500, l.Lines[1].Start);
        Assert.Equal(1, l.IndexAt(4000));
        Assert.Equal(-1, l.IndexAt(500));
    }

    [Fact]
    public void ParsesPlain()
    {
        var l = Lrc.Parse("uno\ndue", "t");
        Assert.False(l.Synced);
        Assert.Equal(SyncKind.Plain, l.Kind);
    }

    [Fact]
    public void ParsesWords()
    {
        var l = Lrc.Parse("[00:01.00]<00:01.00>Ci<00:01.40>ao<00:02.00>\n[00:03.00]<00:03.00>x<00:03.50>", "t");
        Assert.Equal(SyncKind.Words, l.Kind);
        Assert.Equal("Ciao", l.Lines[0].Text);
        Assert.Equal(2, l.Lines[0].Words.Count);
    }
}
