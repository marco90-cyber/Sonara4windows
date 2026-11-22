namespace Sonara.Core.Audio;

/// <summary>Filtro biquad (formule RBJ Audio EQ Cookbook), stato per canale.</summary>
public sealed class Biquad
{
    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private double _x1L, _x2L, _y1L, _y2L, _x1R, _x2R, _y1R, _y2R;

    public bool Identity { get; private set; } = true;

    public void Set(int type, double sr, double f0, double gainDb, double q)
    {
        f0 = Math.Clamp(f0, 10, sr * 0.45);
        q = Math.Max(q, 0.05);
        var a = Math.Pow(10, gainDb / 40);
        var w0 = 2 * Math.PI * f0 / sr;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * q);
        double b0, b1, b2, a0, a1, a2;
        switch (type)
        {
            case PeqBand.LS:
            {
                var s = 2 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) - (a - 1) * cos + s); b1 = 2 * a * ((a - 1) - (a + 1) * cos); b2 = a * ((a + 1) - (a - 1) * cos - s);
                a0 = (a + 1) + (a - 1) * cos + s; a1 = -2 * ((a - 1) + (a + 1) * cos); a2 = (a + 1) + (a - 1) * cos - s;
                break;
            }
            case PeqBand.HS:
            {
                var s = 2 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) + (a - 1) * cos + s); b1 = -2 * a * ((a - 1) + (a + 1) * cos); b2 = a * ((a + 1) + (a - 1) * cos - s);
                a0 = (a + 1) - (a - 1) * cos + s; a1 = 2 * ((a - 1) - (a + 1) * cos); a2 = (a + 1) - (a - 1) * cos - s;
                break;
            }
            case PeqBand.LP:
                b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = (1 - cos) / 2; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha; break;
            case PeqBand.HP:
                b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = (1 + cos) / 2; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha; break;
            case PeqBand.BP:
                b0 = alpha; b1 = 0; b2 = -alpha; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha; break;
            case PeqBand.NOTCH:
                b0 = 1; b1 = -2 * cos; b2 = 1; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha; break;
            default: // campana
                b0 = 1 + alpha * a; b1 = -2 * cos; b2 = 1 - alpha * a; a0 = 1 + alpha / a; a1 = -2 * cos; a2 = 1 - alpha / a; break;
        }
        _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0; _a1 = a1 / a0; _a2 = a2 / a0;
        Identity = false;
    }

    public void SetIdentity() { _b0 = 1; _b1 = _b2 = _a1 = _a2 = 0; Identity = true; }

    public void Reset() { _x1L = _x2L = _y1L = _y2L = _x1R = _x2R = _y1R = _y2R = 0; }

    public float ProcessL(float x)
    {
        var y = _b0 * x + _b1 * _x1L + _b2 * _x2L - _a1 * _y1L - _a2 * _y2L;
        _x2L = _x1L; _x1L = x; _y2L = _y1L; _y1L = y;
        return (float)y;
    }

    public float ProcessR(float x)
    {
        var y = _b0 * x + _b1 * _x1R + _b2 * _x2R - _a1 * _y1R - _a2 * _y2R;
        _x2R = _x1R; _x1R = x; _y2R = _y1R; _y1R = y;
        return (float)y;
    }

    /// <summary>Modulo in dB del filtro alla frequenza f (per disegnare la curva).</summary>
    public double ResponseDb(double sr, double f)
    {
        var w = 2 * Math.PI * f / sr;
        double cw = Math.Cos(w), sw = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
        double nr = _b0 + _b1 * cw + _b2 * c2, ni = -(_b1 * sw + _b2 * s2);
        double dr = 1 + _a1 * cw + _a2 * c2, di = -(_a1 * sw + _a2 * s2);
        var m = Math.Sqrt((nr * nr + ni * ni) / (dr * dr + di * di));
        return 20 * Math.Log10(Math.Max(m, 1e-12));
    }
}

/// <summary>
/// Catena DSP su audio float interlacciato (mono o stereo; oltre due canali passa invariato).
/// Ordine: DC, preamp, EQ, toni, bassi analogici, aria, loudness, stereo, saturazione, volume digitale, limiter.
/// </summary>
public sealed class DspChain
{
    private DspSettings _s = new();
    private int _sr = 44100;
    private int _channels = 2;
    private readonly List<Biquad> _eq = new();
    private readonly Biquad _bass = new(), _treble = new(), _air = new(), _loudLo = new(), _loudHi = new(), _clarity = new();
    private readonly Biquad _analogLp = new(), _xfeedLp = new();
    private double _dcX1L, _dcY1L, _dcX1R, _dcY1R;
    private float _preGain = 1f, _dvcGain = 1f;
    private float _gainLeft = 1f, _gainRight = 1f;
    private readonly object _lock = new();

    // limiter
    private float[] _delay = Array.Empty<float>();
    private int _delayPos, _look;
    private readonly LinkedList<(long Idx, float V)> _peaks = new();
    private long _idx;
    private float _limGain = 1f;

    public int SampleRate => _sr;
    public bool Active => _s.Master;

    /// <summary>Latenza introdotta dal limiter (in campioni per canale).</summary>
    public int LatencyFrames => _s.Master && _s.Limiter ? _look : 0;

    public DspChain(int sampleRate = 44100, int channels = 2) => Configure(sampleRate, channels, new DspSettings());

    public void Configure(int sampleRate, int channels, DspSettings settings)
    {
        lock (_lock)
        {
            _sr = sampleRate; _channels = channels; _s = settings.Clone();
            Rebuild();
        }
    }

    public void Update(DspSettings settings)
    {
        lock (_lock) { _s = settings.Clone(); Rebuild(); }
    }

    private static float DbToLin(float db) => (float)Math.Pow(10, db / 20.0);

    private void Rebuild()
    {
        var s = _s;
        _eq.Clear();
        double boost = 0;
        var para = s.Eq && s.EqMode != DspSettings.EqGraphic;
        if (s.Eq && !para)
        {
            var f = s.Freqs;
            // larghezza di banda in ottave: la gamma 20 Hz - 20 kHz divisa per il numero di bande
            var bw = Math.Max(0.33, 10.0 / s.Bands);
            var q = 1 / (2 * Math.Sinh(Math.Log(2) / 2 * bw));
            for (var i = 0; i < f.Count; i++)
            {
                var g = s.GainAt(i);
                if (Math.Abs(g) < 0.01f) continue;
                var b = new Biquad(); b.Set(PeqBand.PK, _sr, f[i], g, q); _eq.Add(b);
                boost = Math.Max(boost, g);
            }
        }
        else if (para)
        {
            foreach (var p in s.Peq.Take(DspSettings.PeqMax))
            {
                if (!p.On) continue;
                if (p.HasGain && Math.Abs(p.Gain) < 0.01f) continue;
                var b = new Biquad(); b.Set(p.Type, _sr, p.Freq, p.Gain, p.Q); _eq.Add(b);
                if (p.HasGain) boost = Math.Max(boost, p.Gain);
            }
        }
        var tone = s.Tone;
        if (tone && s.BassDb > 0.01f) _bass.Set(PeqBand.LS, _sr, s.BassFreq, s.BassDb, s.BassQ); else _bass.SetIdentity();
        if (tone && s.TrebleDb > 0.01f) _treble.Set(PeqBand.HS, _sr, s.TrebleFreq, s.TrebleDb, s.TrebleQ); else _treble.SetIdentity();
        if (s.Air) _air.Set(PeqBand.HS, _sr, 12000, 6 * s.AirAmount, 0.7); else _air.SetIdentity();
        if (s.Loudness) { _loudLo.Set(PeqBand.LS, _sr, 100, 8 * s.LoudnessAmount, 0.7); _loudHi.Set(PeqBand.HS, _sr, 10000, 4 * s.LoudnessAmount, 0.7); }
        else { _loudLo.SetIdentity(); _loudHi.SetIdentity(); }
        if (s.Clarity) _clarity.Set(PeqBand.PK, _sr, 3500, 2.5 * s.ClarityAmount, 0.9); else _clarity.SetIdentity();
        _analogLp.Set(PeqBand.LP, _sr, Math.Max(40, s.AnalogFreq * 2), 0, 0.707);
        _xfeedLp.Set(PeqBand.LP, _sr, 700, 0, 0.707);

        var pre = s.PreampOn ? s.PreampDb : 0f;
        if (para) pre += s.PeqPreamp;
        if (s.AutoHeadroom) pre -= (float)boost + (tone ? Math.Max(0, s.BassDb) * 0.5f + Math.Max(0, s.TrebleDb) * 0.5f : 0f);
        _preGain = DbToLin(pre);
        _dvcGain = s.Dvc ? DbToLin(Math.Clamp(s.DvcDb, -60f, 0f)) : 1f;
        var bal = Math.Clamp(s.Stereo ? s.Balance : 0f, -1f, 1f);
        _gainLeft = bal > 0 ? 1 - bal : 1;
        _gainRight = bal < 0 ? 1 + bal : 1;

        _look = Math.Max(1, (int)(_sr * 0.0015));
        if (_delay.Length != _look * Math.Max(1, _channels)) { _delay = new float[_look * Math.Max(1, _channels)]; _delayPos = 0; _peaks.Clear(); _idx = 0; _limGain = 1f; }
    }

    /// <summary>Azzera lo stato dei filtri (dopo un salto di posizione o un nuovo brano).</summary>
    public void Reset()
    {
        lock (_lock)
        {
            foreach (var b in _eq) b.Reset();
            _bass.Reset(); _treble.Reset(); _air.Reset(); _loudLo.Reset(); _loudHi.Reset(); _clarity.Reset(); _analogLp.Reset(); _xfeedLp.Reset();
            _dcX1L = _dcY1L = _dcX1R = _dcY1R = 0;
            Array.Clear(_delay); _peaks.Clear(); _idx = 0; _limGain = 1f;
        }
    }

    /// <summary>Elabora in place <paramref name="count"/> campioni interlacciati.</summary>
    public void Process(float[] buf, int offset, int count)
    {
        lock (_lock)
        {
            var s = _s;
            if (!s.Master || _channels > 2 || _channels < 1) return;
            if (_channels == 1) { ProcessMono(buf, offset, count); return; }
            var frames = count / 2;
            var dcR = 1.0 - 2 * Math.PI * 10.0 / _sr;
            var eq = _eq.Count;
            var crossMix = s.Stereo ? s.Crossfeed * 0.5f : 0f;
            var width = s.Stereo ? 1f + s.Expand * 1.5f + (s.SurroundMode > 0 ? 0.25f * s.SurroundMode : 0f) : 1f;
            var drive = 1f + s.AnalogDrive * 4f;
            for (var n = 0; n < frames; n++)
            {
                var i = offset + n * 2;
                double l = buf[i], r = buf[i + 1];
                if (s.DcBlock)
                {
                    var yl = l - _dcX1L + dcR * _dcY1L; _dcX1L = l; _dcY1L = yl; l = yl;
                    var yr = r - _dcX1R + dcR * _dcY1R; _dcX1R = r; _dcY1R = yr; r = yr;
                }
                var fl = (float)l * _preGain; var fr = (float)r * _preGain;
                for (var k = 0; k < eq; k++) { fl = _eq[k].ProcessL(fl); fr = _eq[k].ProcessR(fr); }
                if (!_bass.Identity) { fl = _bass.ProcessL(fl); fr = _bass.ProcessR(fr); }
                if (!_treble.Identity) { fl = _treble.ProcessL(fl); fr = _treble.ProcessR(fr); }
                if (s.AnalogBass)
                {
                    var lowL = _analogLp.ProcessL(fl); var lowR = _analogLp.ProcessR(fr);
                    fl += (float)Math.Tanh(lowL * drive) * 0.35f * s.AnalogWarmth;
                    fr += (float)Math.Tanh(lowR * drive) * 0.35f * s.AnalogWarmth;
                }
                if (!_air.Identity) { fl = _air.ProcessL(fl); fr = _air.ProcessR(fr); }
                if (!_loudLo.Identity) { fl = _loudHi.ProcessL(_loudLo.ProcessL(fl)); fr = _loudHi.ProcessR(_loudLo.ProcessR(fr)); }
                if (!_clarity.Identity) { fl = _clarity.ProcessL(fl); fr = _clarity.ProcessR(fr); }
                if (s.Stereo)
                {
                    if (s.Mono) { var m = (fl + fr) * 0.5f; fl = fr = m; }
                    else
                    {
                        if (Math.Abs(width - 1f) > 0.001f)
                        {
                            var mid = (fl + fr) * 0.5f; var side = (fl - fr) * 0.5f * width;
                            fl = mid + side; fr = mid - side;
                        }
                        if (crossMix > 0)
                        {
                            var cl = _xfeedLp.ProcessL(fl); var cr = _xfeedLp.ProcessR(fr);
                            var nl = fl * (1 - crossMix * 0.5f) + cr * crossMix;
                            var nr = fr * (1 - crossMix * 0.5f) + cl * crossMix;
                            fl = nl; fr = nr;
                        }
                    }
                    fl *= _gainLeft; fr *= _gainRight;
                }
                if (s.SoftSat) { fl = SoftKnee(fl); fr = SoftKnee(fr); }
                fl *= _dvcGain; fr *= _dvcGain;
                if (s.Limiter) Limit(ref fl, ref fr, s);
                buf[i] = fl; buf[i + 1] = fr;
            }
        }
    }

    private void ProcessMono(float[] buf, int offset, int count)
    {
        var s = _s;
        for (var n = 0; n < count; n++)
        {
            var x = buf[offset + n] * _preGain;
            foreach (var b in _eq) x = b.ProcessL(x);
            if (!_bass.Identity) x = _bass.ProcessL(x);
            if (!_treble.Identity) x = _treble.ProcessL(x);
            if (!_air.Identity) x = _air.ProcessL(x);
            if (s.SoftSat) x = SoftKnee(x);
            x *= _dvcGain;
            buf[offset + n] = Math.Clamp(x, -1f, 1f);
        }
    }

    private static float SoftKnee(float x)
    {
        const float k = 0.8f;
        var a = Math.Abs(x);
        if (a <= k) return x;
        var over = (a - k) / (1 - k);
        var y = k + (1 - k) * (float)Math.Tanh(over);
        return x < 0 ? -y : y;
    }

    /// <summary>Limiter con anticipo: il segnale esce ritardato di poco e il guadagno scende prima del picco.</summary>
    private void Limit(ref float l, ref float r, DspSettings s)
    {
        var thr = DbToLin(s.LimiterThresholdDb);
        var peak = Math.Max(Math.Abs(l), Math.Abs(r));
        while (_peaks.Count > 0 && _peaks.Last!.Value.V <= peak) _peaks.RemoveLast();
        _peaks.AddLast((_idx, peak));
        while (_peaks.First!.Value.Idx <= _idx - _look) _peaks.RemoveFirst();
        var win = _peaks.First!.Value.V;
        var target = win > thr ? thr / win : 1f;
        if (target < _limGain) _limGain += (target - _limGain) * Math.Min(1f, 3f / _look);
        else
        {
            var rel = 1f - (float)Math.Exp(-1.0 / (Math.Max(5f, s.LimiterReleaseMs) * 0.001 * _sr));
            _limGain += (target - _limGain) * rel;
        }
        if (_limGain > target && target < 1f) _limGain = Math.Min(_limGain, target + (1f - target) * 0.0f);
        var di = _delayPos * 2;
        var ol = _delay[di]; var or = _delay[di + 1];
        _delay[di] = l; _delay[di + 1] = r;
        _delayPos = (_delayPos + 1) % _look;
        _idx++;
        l = Math.Clamp(ol * _limGain, -1f, 1f);
        r = Math.Clamp(or * _limGain, -1f, 1f);
    }
}
