using Sonara.Core.Audio;
using Xunit;

public class DspTests
{
    private static float[] Sine(double f, int sr, int frames, double amp = 0.1)
    {
        var b = new float[frames * 2];
        for (var i = 0; i < frames; i++) { var v = (float)(amp * Math.Sin(2 * Math.PI * f * i / sr)); b[i * 2] = v; b[i * 2 + 1] = v; }
        return b;
    }

    private static double Rms(float[] b, int from)
    {
        double s = 0; var n = 0;
        for (var i = from; i < b.Length; i += 2) { s += b[i] * b[i]; n++; }
        return Math.Sqrt(s / n);
    }

    [Fact]
    public void MasterOffIsBitExact()
    {
        var c = new DspChain(44100, 2);
        var a = Sine(1000, 44100, 4410); var b = (float[])a.Clone();
        c.Process(b, 0, b.Length);
        Assert.Equal(a, b);
    }

    [Fact]
    public void PeakingBoostRaisesLevel()
    {
        var s = new DspSettings { Master = true, AutoHeadroom = false, DcBlock = false, Limiter = false, Peq = { new PeqBand(true, PeqBand.PK, 1000, 6, 1) }, EqMode = DspSettings.EqParametric };
        var c = new DspChain(44100, 2); c.Configure(44100, 2, s);
        var x = Sine(1000, 44100, 44100);
        var y = (float[])x.Clone(); c.Process(y, 0, y.Length);
        var db = 20 * Math.Log10(Rms(y, 40000) / Rms(x, 40000));
        Assert.InRange(db, 5.5, 6.5);
    }

    [Fact]
    public void LimiterHoldsThreshold()
    {
        var s = new DspSettings { Master = true, AutoHeadroom = false, DcBlock = false, PreampOn = true, PreampDb = 12, LimiterThresholdDb = -3 };
        var c = new DspChain(44100, 2); c.Configure(44100, 2, s);
        var y = Sine(440, 44100, 44100, 0.8);
        c.Process(y, 0, y.Length);
        var thr = Math.Pow(10, -3 / 20.0);
        var max = y.Skip(2000).Max(Math.Abs);
        Assert.True(max <= thr * 1.02, $"picco {max} oltre {thr}");
    }

    [Fact]
    public void ParsesAutoEq()
    {
        var p = PresetText.ParseParametric("Preamp: -6.2 dB\nFilter 1: ON PK Fc 105 Hz Gain -1.5 dB Q 0.70\nFilter 2: ON HSC Fc 10000 Hz Gain 3.0 dB Q 0.7", "x");
        Assert.NotNull(p);
        Assert.Equal(-6.2f, p!.PreampDb);
        Assert.Equal(2, p.Bands.Count);
        Assert.Equal(PeqBand.HS, p.Bands[1].Type);
    }

    [Fact]
    public void SetBandsKeepsShape()
    {
        var s = new DspSettings(); s.SetGain(5, 6); s.SetBands(31);
        Assert.Equal(31, s.Graphic.Count);
        Assert.InRange(s.Graphic[17], 5.9, 6.1);
    }
}
