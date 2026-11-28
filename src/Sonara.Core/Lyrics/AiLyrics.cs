using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sonara.Core.Lyrics;

/// <summary>
/// Il testo scritto dall'AI (Whisper): trasforma l'uscita JSON di whisper.cpp in righe con i tempi delle parole
/// (togliendo le tipiche invenzioni di Whisper) e le scrive in formato LRC «enhanced». Porting di LyricsAi.kt.
/// </summary>
public static class AiLyrics
{
    public const string Source = "Generato con AI";
    public const string Tag = "[ai:Whisper]";

    private static readonly Regex Junk = new(@"^[\[(*♪♫♬\s].*[\])*♪♫♬\s]$|^[♪♫♬\s.]*$", RegexOptions.Compiled);
    private static readonly Regex Detected = new(@"auto-detected language:\s*([a-z]{2,3})\s*\(p\s*=\s*([0-9.,]+)\)", RegexOptions.Compiled);

    /// <summary>Un pezzo di testo con i tempi delle sue parole, ancora da pulire.</summary>
    public sealed record RawLine(long Start, long End, string Text, List<(long S, long E, string W)> Words);

    /// <summary>Legge l'uscita «--output-json-full»: un segmento per riga, i token uniti in parole.</summary>
    public static List<RawLine> ReadWhisperJson(string json)
    {
        var res = new List<RawLine>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("transcription", out var tr)) return res;
        foreach (var seg in tr.EnumerateArray())
        {
            var (s, e) = Offsets(seg);
            var text = seg.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
            var words = new List<(long, long, string)>();
            if (seg.TryGetProperty("tokens", out var toks))
            {
                string cur = ""; long cs = -1, ce = -1;
                void Flush() { if (cur.Trim().Length > 0 && cs >= 0) words.Add((cs, Math.Max(cs, ce), cur.Trim())); cur = ""; cs = -1; ce = -1; }
                foreach (var tk in toks.EnumerateArray())
                {
                    var t = tk.TryGetProperty("text", out var tt) ? tt.GetString() ?? "" : "";
                    if (t.Length == 0 || t.StartsWith("[_", StringComparison.Ordinal)) continue;   // [_BEG_], [_TT_123]… non sono parole
                    var has = tk.TryGetProperty("offsets", out _);
                    var (ts, te) = has ? Offsets(tk) : (-1L, -1L);
                    if (char.IsWhiteSpace(t[0]) && cur.Length > 0) Flush();
                    if (cur.Length == 0) cs = ts;
                    cur += t;
                    if (te >= 0) ce = te;
                    if (cs < 0 && ts >= 0) cs = ts;
                }
                Flush();
            }
            res.Add(new RawLine(s, e, text, words));
        }
        return res;
    }

    private static (long, long) Offsets(JsonElement e)
    {
        if (e.TryGetProperty("offsets", out var o))
            return (o.TryGetProperty("from", out var f) ? f.GetInt64() : 0, o.TryGetProperty("to", out var t) ? t.GetInt64() : 0);
        return (0, 0);
    }

    /// <summary>Righe pulite: niente righe vuote o di soli simboli, niente ripetizioni infinite (allucinazioni), tempi delle parole coerenti.</summary>
    public static List<LLine> Clean(IEnumerable<RawLine> raw)
    {
        var o = new List<LLine>();
        var repeat = 0;
        foreach (var r in raw)
        {
            var text = r.Text.Trim();
            if (text.Length == 0 || Junk.IsMatch(text)) continue;
            var start = r.Start; var end = Math.Max(start, r.End);
            var same = o.Count > 0 && string.Equals(o[^1].Text, text, StringComparison.OrdinalIgnoreCase);
            repeat = same ? repeat + 1 : 0;
            if (repeat >= 4) continue;   // ripetizione infinita: è un'allucinazione, non un ritornello
            o.Add(new LLine(start, end, text, Words(r.Words, start, end)));
        }
        return o;
    }

    /// <summary>Parole con tempi coerenti: in ordine, dentro la riga, senza buchi piccoli.</summary>
    private static List<LWord> Words(List<(long S, long E, string W)> raw, long lineStart, long lineEnd)
    {
        var o = new List<LWord>();
        if (raw.Count == 0) return o;
        var maxEnd = Math.Max(lineEnd, raw[^1].E);
        var starts = new long[raw.Count];
        var prev = lineStart;
        for (var i = 0; i < raw.Count; i++) { starts[i] = Math.Clamp(raw[i].S, prev, Math.Max(prev, maxEnd)); prev = starts[i]; }
        for (var i = 0; i < raw.Count; i++)
        {
            var nextStart = i < raw.Count - 1 ? starts[i + 1] : maxEnd;
            var e = Math.Clamp(raw[i].E, starts[i], Math.Max(starts[i], maxEnd));
            // se tra una parola e la prossima c'è una pausa breve, la parola resta accesa fino alla successiva
            if (i < raw.Count - 1 && nextStart - e < 400) e = nextStart;
            if (e <= starts[i]) e = Math.Min(starts[i] + 120, Math.Max(nextStart, starts[i] + 1));
            o.Add(new LWord(starts[i], e, raw[i].W + (i < raw.Count - 1 ? " " : "")));
        }
        return o;
    }

    private static string Stamp(long ms)
    {
        var t = Math.Max(0, ms);
        return string.Create(CultureInfo.InvariantCulture, $"{t / 60000:00}:{t / 1000 % 60:00}.{t % 1000 / 10:00}");
    }

    /// <summary>Il testo in formato LRC «enhanced» (tempo della riga e di ogni parola), con la sigla che dice che è fatto dall'AI.</summary>
    public static string ToLrc(IReadOnlyList<LLine> lines)
    {
        var sb = new StringBuilder(Tag).Append('\n');
        foreach (var l in lines)
        {
            sb.Append('[').Append(Stamp(l.Start)).Append(']');
            if (l.Words.Count == 0) sb.Append(l.Text);
            else for (var i = 0; i < l.Words.Count; i++)
            {
                var w = l.Words[i];
                sb.Append('<').Append(Stamp(w.Start)).Append('>').Append(w.Text);
                if (i == l.Words.Count - 1) sb.Append('<').Append(Stamp(w.End)).Append('>');
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>La lingua riconosciuta (codice e sicurezza) dall'uscita di «whisper-cli -dl».</summary>
    public static (string Lang, double P)? ParseDetected(string text)
    {
        var m = Detected.Match(text);
        if (!m.Success) return null;
        double.TryParse(m.Groups[2].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var p);
        return (m.Groups[1].Value, p);
    }

    /// <summary>Tra più riconoscimenti sceglie la lingua più frequente (a parità, quella più sicura).</summary>
    public static string? Vote(IEnumerable<(string Lang, double P)> votes) =>
        votes.GroupBy(v => v.Lang).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Sum(v => v.P)).Select(g => g.Key).FirstOrDefault();

    /// <summary>Quanto è attendibile un testo «Generato con AI»: serve a capire dal file in cache che viene dall'AI.</summary>
    public static bool IsAiLrc(string lrc) => lrc.TrimStart().StartsWith(Tag, StringComparison.Ordinal);
}
