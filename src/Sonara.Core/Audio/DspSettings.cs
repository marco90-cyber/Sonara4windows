using System.Text.Json;
using System.Text.Json.Serialization;
using Sonara.Core.Data;

namespace Sonara.Core.Audio;

/// <summary>Filtro del parametrico (PK campana, LS/HS shelf, LP/HP, BP, notch).</summary>
public sealed record PeqBand(bool On = true, int Type = PeqBand.PK, float Freq = 1000f, float Gain = 0f, float Q = 1f)
{
    public const int PK = 0, LS = 1, HS = 2, LP = 3, HP = 4, BP = 5, NOTCH = 6;
    public bool HasGain => Type is PK or LS or HS;

    public static string Label(int t) => t switch
    {
        LS => "Shelf bassi", HS => "Shelf alti", LP => "Passa-basso", HP => "Passa-alto", BP => "Passa-banda", NOTCH => "Notch", _ => "Campana",
    };
}

/// <summary>Tutte le impostazioni del motore DSP (file eq.json). Porting di DspSettings.kt.</summary>
public sealed class DspSettings
{
    public const int EqGraphic = 0, EqParametric = 1, EqAutoEq = 2;
    public const float GainRange = 12f;
    public const int PeqMax = 16;
    public static readonly int[] BandOptions = { 3, 5, 7, 10, 15, 31 };
    public static readonly string[] SurroundModes = { "Spento", "Tradizionale", "Palco frontale", "Ultra Wide" };

    /// <summary>Spento = audio intatto (bit-exact).</summary>
    public bool Master { get; set; }

    public bool Eq { get; set; } = true;
    public int Bands { get; set; } = 10;
    public List<float> Graphic { get; set; } = Enumerable.Repeat(0f, 10).ToList();
    public int EqMode { get; set; } = EqGraphic;
    public List<PeqBand> Peq { get; set; } = new();
    public float PeqPreamp { get; set; }
    public string AutoEqName { get; set; } = "";

    public bool PreampOn { get; set; }
    public float PreampDb { get; set; }

    public bool Tone { get; set; } = true;
    public float BassDb { get; set; }
    public float BassFreq { get; set; } = 90f;
    public float BassQ { get; set; } = 0.8f;
    public float TrebleDb { get; set; }
    public float TrebleFreq { get; set; } = 10000f;
    public float TrebleQ { get; set; } = 0.6f;

    public bool AnalogBass { get; set; }
    public float AnalogDrive { get; set; } = 0.4f;
    public float AnalogWarmth { get; set; } = 0.5f;
    public float AnalogFreq { get; set; } = 60f;
    public bool Air { get; set; }
    public float AirAmount { get; set; } = 0.5f;

    public bool Stereo { get; set; }
    public int SurroundMode { get; set; }
    public float Balance { get; set; }
    public float Expand { get; set; }
    public bool Mono { get; set; }
    public float Crossfeed { get; set; }

    public bool Loudness { get; set; }
    public float LoudnessAmount { get; set; } = 0.7f;
    public bool Clarity { get; set; }
    public float ClarityAmount { get; set; } = 1f;

    public bool DcBlock { get; set; } = true;
    public bool SoftSat { get; set; }
    public bool AutoHeadroom { get; set; } = true;
    public bool Dvc { get; set; }
    public float DvcDb { get; set; } = -10f;
    public bool Limiter { get; set; } = true;
    public float LimiterThresholdDb { get; set; } = -0.5f;
    public float LimiterReleaseMs { get; set; } = 80f;
    /// <summary>ReplayGain: 0 spento (la scansione del volume dei brani non c'è ancora su Windows).</summary>
    public int ReplayGain { get; set; }

    [JsonIgnore]
    public IReadOnlyList<float> Freqs => FreqsFor(Bands);

    public static IReadOnlyList<float> FreqsFor(int n) => n switch
    {
        3 => new[] { 100f, 1000f, 10000f },
        5 => new[] { 60f, 230f, 910f, 3600f, 14000f },
        7 => new[] { 63f, 160f, 400f, 1000f, 2500f, 6300f, 16000f },
        15 => new[] { 25f, 40f, 63f, 100f, 160f, 250f, 400f, 630f, 1000f, 1600f, 2500f, 4000f, 6300f, 10000f, 16000f },
        31 => new[] { 20f, 25f, 31.5f, 40f, 50f, 63f, 80f, 100f, 125f, 160f, 200f, 250f, 315f, 400f, 500f, 630f, 800f, 1000f, 1250f, 1600f, 2000f, 2500f, 3150f, 4000f, 5000f, 6300f, 8000f, 10000f, 12500f, 16000f, 20000f },
        _ => new[] { 31f, 62f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f },
    };

    public float GainAt(int i) => i >= 0 && i < Graphic.Count ? Graphic[i] : 0f;

    public void SetGain(int i, float db)
    {
        if (i < 0 || i >= Bands) return;
        while (Graphic.Count < Bands) Graphic.Add(0f);
        Graphic[i] = Math.Clamp(db, -GainRange, GainRange);
    }

    /// <summary>Cambia il numero di bande mantenendo la forma della curva.</summary>
    public void SetBands(int n)
    {
        if (!BandOptions.Contains(n) || n == Bands) return;
        var from = Freqs.Select((f, i) => (f, GainAt(i))).ToList();
        Bands = n;
        Graphic = FreqsFor(n).Select(f => Interpolate(from, f)).ToList();
    }

    /// <summary>Interpolazione lineare in dB su scala logaritmica di frequenza.</summary>
    public static float Interpolate(IReadOnlyList<(float F, float G)> pts, float f)
    {
        if (pts.Count == 0) return 0f;
        var s = pts.OrderBy(p => p.F).ToList();
        if (f <= s[0].F) return s[0].G;
        if (f >= s[^1].F) return s[^1].G;
        for (var i = 1; i < s.Count; i++)
        {
            if (f <= s[i].F)
            {
                var t = (float)(Math.Log(f / s[i - 1].F) / Math.Log(s[i].F / s[i - 1].F));
                return s[i - 1].G + (s[i].G - s[i - 1].G) * t;
            }
        }
        return s[^1].G;
    }

    /// <summary>Applica un preset (curva) sulle bande attuali.</summary>
    public void ApplyCurve(IReadOnlyList<(float F, float G)> curve, float preampDb = 0f)
    {
        Eq = true; Master = true; EqMode = EqGraphic;
        Graphic = Freqs.Select(f => Math.Clamp(Interpolate(curve, f), -GainRange, GainRange)).ToList();
        if (preampDb != 0f) { PreampOn = true; PreampDb = preampDb; }
    }

    public DspSettings Clone() => JsonSerializer.Deserialize<DspSettings>(JsonSerializer.Serialize(this, Persist.Options), Persist.Options)!;

    public string EqLabel() => EqMode switch
    {
        EqParametric => $"EQ parametrico ({Peq.Count(p => p.On)} filtri)",
        EqAutoEq => $"AutoEQ {AutoEqName}".Trim(),
        _ => $"EQ {Bands} bande",
    };

    // ---- salvataggio

    private static string PathOf => System.IO.Path.Combine(Persist.Root, "eq.json");

    public static DspSettings Load()
    {
        var s = Persist.Read<DspSettings>(PathOf) ?? new DspSettings();
        if (!BandOptions.Contains(s.Bands)) s.Bands = 10;
        while (s.Graphic.Count < s.Bands) s.Graphic.Add(0f);
        return s;
    }

    public void Save() => Persist.WriteAtomic(PathOf, JsonSerializer.Serialize(this, Persist.Options));
}

/// <summary>Preset di fabbrica: curve sulle 15 bande ISO, adattate al numero di bande scelto.</summary>
public static class GraphicPresets
{
    public sealed record Preset(string Name, float[] G15)
    {
        public IReadOnlyList<(float F, float G)> Curve => DspSettings.FreqsFor(15).Select((f, i) => (f, G15[i])).ToList();
    }

    public static readonly IReadOnlyList<Preset> All = new[]
    {
        new Preset("Piatto", new float[15]),
        new Preset("Bassi", new[] { 6f, 6f, 5f, 4f, 2.5f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f }),
        new Preset("Alti", new[] { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.5f, 1.5f, 2.5f, 3.5f, 4.5f, 5f, 5f }),
        new Preset("Voce", new[] { -3f, -3f, -2f, -1f, 0f, 1f, 2f, 3f, 3.5f, 3f, 2f, 1f, 0f, -1f, -1f }),
        new Preset("Rock", new[] { 4f, 4f, 3f, 1.5f, 0f, -1f, -1.5f, -1f, 0f, 1f, 2.5f, 3.5f, 4f, 4f, 3.5f }),
        new Preset("Pop", new[] { -1f, 0f, 1f, 2.5f, 3.5f, 3.5f, 2f, 0.5f, -0.5f, -0.5f, 0f, 1f, 1.5f, 2f, 2f }),
        new Preset("Jazz", new[] { 3f, 3f, 2f, 1.5f, 0f, -1f, -1f, 0f, 1f, 1.5f, 2f, 2.5f, 3f, 3f, 3f }),
        new Preset("Classica", new[] { 3.5f, 3f, 2.5f, 2f, 1f, 0f, 0f, 0f, 0f, 0f, 0.5f, 1.5f, 2.5f, 3f, 3.5f }),
        new Preset("Elettronica", new[] { 5f, 5f, 4f, 2.5f, 0.5f, -1f, -1f, 0f, 0.5f, 1.5f, 2.5f, 3f, 3.5f, 4f, 4f }),
        new Preset("Loudness", new[] { 6f, 5.5f, 4.5f, 3f, 1f, 0f, -0.5f, -0.5f, 0f, 0.5f, 1.5f, 3f, 4f, 4.5f, 5f }),
        new Preset("Podcast", new[] { -6f, -5f, -3f, -1f, 0f, 1f, 2f, 3f, 3f, 2.5f, 2f, 1f, 0f, -2f, -4f }),
    };
}
