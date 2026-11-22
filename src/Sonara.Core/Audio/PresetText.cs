using System.Globalization;
using System.Text.RegularExpressions;

namespace Sonara.Core.Audio;

/// <summary>Preset testuali: curve a bande (grafico) e filtri parametrici in formato AutoEQ / Equalizer APO.</summary>
public static class PresetText
{
    private static readonly Regex PreampRe = new(@"preamp\s*:\s*(-?[\d.,]+)\s*db", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FilterRe = new(
        @"filter\s*\d*\s*:\s*(on|off)\s+(\w+)\s+fc\s+(-?[\d.,]+)\s*hz(?:\s+gain\s+(-?[\d.,]+)\s*db)?(?:\s+q\s+(-?[\d.,]+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Parametric(string Name, float PreampDb, List<PeqBand> Bands, List<string> Notes);

    private static float? Num(string s) =>
        float.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static int? TypeOf(string code) => code.ToUpperInvariant() switch
    {
        "PK" or "PEQ" or "PEAK" => PeqBand.PK,
        "LS" or "LSC" or "LSQ" or "LSF" => PeqBand.LS,
        "HS" or "HSC" or "HSQ" or "HSF" => PeqBand.HS,
        "LP" or "LPQ" or "LPF" => PeqBand.LP,
        "HP" or "HPQ" or "HPF" => PeqBand.HP,
        "BP" => PeqBand.BP,
        "NO" or "NOTCH" => PeqBand.NOTCH,
        _ => null,
    };

    /// <summary>Preset parametrico da testo AutoEQ / Equalizer APO. Null se non ci sono filtri.</summary>
    public static Parametric? ParseParametric(string text, string fallbackName)
    {
        var preamp = 0f;
        var list = new List<PeqBand>();
        foreach (var raw in text.TrimStart('﻿').Split('\n'))
        {
            var line = raw.Trim();
            var pm = PreampRe.Match(line);
            if (pm.Success && Num(pm.Groups[1].Value) is float p) preamp = p;
            var m = FilterRe.Match(line);
            if (!m.Success) continue;
            if (TypeOf(m.Groups[2].Value) is not int type) continue;
            if (Num(m.Groups[3].Value) is not float f) continue;
            var g = m.Groups[4].Success ? Num(m.Groups[4].Value) ?? 0f : 0f;
            var q = m.Groups[5].Success ? Num(m.Groups[5].Value) ?? 0.707f : 0.707f;
            list.Add(new PeqBand(m.Groups[1].Value.Equals("ON", StringComparison.OrdinalIgnoreCase), type,
                Math.Clamp(f, 20f, 20000f), Math.Clamp(g, -24f, 24f), Math.Clamp(q, 0.1f, 20f)));
        }
        if (list.Count == 0) return null;
        var notes = list.Count > DspSettings.PeqMax ? new List<string> { $"{list.Count - DspSettings.PeqMax} filtri oltre il {DspSettings.PeqMax}° non importati" } : new();
        return new Parametric(fallbackName, Math.Clamp(preamp, -30f, 0f), list.Take(DspSettings.PeqMax).ToList(), notes);
    }

    /// <summary>Curva a bande da testo «frequenza guadagno» (una coppia per riga) o da «GraphicEQ: 20 1.5; 25 0; ...».</summary>
    public static IReadOnlyList<(float F, float G)>? ParseCurve(string text)
    {
        var t = text.Trim();
        var gi = t.IndexOf("GraphicEQ:", StringComparison.OrdinalIgnoreCase);
        var parts = gi >= 0 ? t[(gi + 10)..].Split(';', '\n') : t.Split('\n');
        var pts = new List<(float, float)>();
        foreach (var p in parts)
        {
            var nums = Regex.Matches(p, @"-?\d+(?:[.,]\d+)?").Select(m => Num(m.Value)).Where(x => x != null).Select(x => x!.Value).ToList();
            if (nums.Count >= 2 && nums[0] >= 10 && nums[0] <= 24000) pts.Add((nums[0], nums[1]));
        }
        return pts.Count >= 2 ? pts : null;
    }

    public static string ExportParametric(DspSettings s)
    {
        string F(float v, int d) => v.ToString("F" + d, CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder($"Preamp: {F(s.PeqPreamp, 1)} dB\n");
        var i = 1;
        foreach (var b in s.Peq)
        {
            var code = b.Type switch { PeqBand.LS => "LSC", PeqBand.HS => "HSC", PeqBand.LP => "LP", PeqBand.HP => "HP", PeqBand.BP => "BP", PeqBand.NOTCH => "NO", _ => "PK" };
            sb.Append($"Filter {i++}: {(b.On ? "ON" : "OFF")} {code} Fc {F(b.Freq, 0)} Hz Gain {F(b.Gain, 1)} dB Q {F(b.Q, 2)}\n");
        }
        return sb.ToString();
    }
}
