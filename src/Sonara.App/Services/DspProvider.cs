using NAudio.Wave;
using Sonara.Core.Audio;

namespace Sonara.App.Services;

/// <summary>Passa l'audio dalla catena DSP e misura i picchi per il VU meter.</summary>
public sealed class DspProvider : ISampleProvider
{
    private readonly ISampleProvider _src;
    private readonly DspChain _chain;
    private float _peakL, _peakR;

    public DspProvider(ISampleProvider src, DspChain chain)
    {
        _src = src;
        _chain = chain;
        WaveFormat = src.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }
    public float PeakL => _peakL;
    public float PeakR => _peakR;

    public int Read(float[] buffer, int offset, int count)
    {
        var n = _src.Read(buffer, offset, count);
        if (n > 0) _chain.Process(buffer, offset, n);
        float pl = 0, pr = 0;
        for (var i = 0; i + 1 < n; i += 2)
        {
            var a = Math.Abs(buffer[offset + i]); var b = Math.Abs(buffer[offset + i + 1]);
            if (a > pl) pl = a; if (b > pr) pr = b;
        }
        _peakL = pl; _peakR = pr;
        return n;
    }
}
