using Sonara.Core.Lyrics;
using Xunit;

public class AiLyricsTests
{
    private const string Json = """
    {"transcription":[
     {"offsets":{"from":1000,"to":4000},"text":" Ciao mondo","tokens":[
       {"text":"[_BEG_]","offsets":{"from":1000,"to":1000}},
       {"text":" Ci","offsets":{"from":1000,"to":1300}},{"text":"ao","offsets":{"from":1300,"to":1800}},
       {"text":" mondo","offsets":{"from":2000,"to":3500}},{"text":"[_TT_150]","offsets":{"from":3500,"to":3500}}]},
     {"offsets":{"from":4000,"to":5000},"text":" [Musica]","tokens":[]},
     {"offsets":{"from":5000,"to":8000},"text":" Seconda riga","tokens":[{"text":" Seconda","offsets":{"from":5000,"to":6000}},{"text":" riga","offsets":{"from":6100,"to":7000}}]}
    ]}
    """;

    [Fact]
    public void MergesTokensIntoWordsAndDropsJunk()
    {
        var lines = AiLyrics.Clean(AiLyrics.ReadWhisperJson(Json));
        Assert.Equal(2, lines.Count);
        Assert.Equal("Ciao mondo", lines[0].Text);
        Assert.Equal(2, lines[0].Words.Count);
        Assert.Equal("Ciao ", lines[0].Words[0].Text);
        Assert.Equal(1000, lines[0].Words[0].Start);
        Assert.Equal(2000, lines[0].Words[1].Start);
    }

    [Fact]
    public void LrcRoundTripKeepsWordTimes()
    {
        var lines = AiLyrics.Clean(AiLyrics.ReadWhisperJson(Json));
        var lrc = AiLyrics.ToLrc(lines);
        Assert.True(AiLyrics.IsAiLrc(lrc));
        var back = Lrc.Parse(lrc, AiLyrics.Source);
        Assert.Equal(SyncKind.Words, back.Kind);
        Assert.Equal(2, back.Lines.Count);
        Assert.Equal(1000, back.Lines[0].Start);
        Assert.Equal(2, back.Lines[0].Words.Count);
    }

    [Fact]
    public void DetectsAndVotesLanguage()
    {
        var d = AiLyrics.ParseDetected("whisper_full_with_state: auto-detected language: it (p = 0.971234)");
        Assert.NotNull(d);
        Assert.Equal("it", d!.Value.Lang);
        Assert.Equal("it", AiLyrics.Vote(new[] { ("it", 0.9), ("en", 0.95), ("it", 0.6) }));
    }

    [Fact]
    public void DropsEndlessRepeats()
    {
        var raw = Enumerable.Range(0, 8).Select(i => new AiLyrics.RawLine(i * 1000, i * 1000 + 900, "Grazie", new())).ToList();
        Assert.Equal(4, AiLyrics.Clean(raw).Count);
    }
}
