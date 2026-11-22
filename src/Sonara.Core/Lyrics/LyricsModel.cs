using System.Globalization;
using System.Text.RegularExpressions;

namespace Sonara.Core.Lyrics;

/// <summary>Una parola (o sillaba) con il suo tempo, per il testo sincronizzato a parole.</summary>
public sealed record LWord(long Start, long End, string Text);

/// <summary>Una riga del testo; Start &lt; 0 = testo senza tempi.</summary>
public sealed record LLine(long Start, long End, string Text, IReadOnlyList<LWord> Words)
{
    public LLine(long start, long end, string text) : this(start, end, text, Array.Empty<LWord>()) { }
}

public enum SyncKind { Words, Lines, Plain }

/// <summary>Testo di un brano con la fonte da cui viene.</summary>
public sealed record LyricsText(IReadOnlyList<LLine> Lines, string Source)
{
    public bool Synced => Lines.Any(l => l.Start >= 0);
    public SyncKind Kind => Lines.Any(l => l.Words.Count > 0) ? SyncKind.Words : Synced ? SyncKind.Lines : SyncKind.Plain;
    public bool IsEmpty => Lines.All(l => string.IsNullOrWhiteSpace(l.Text));
    public string PlainText => string.Join("\n", Lines.Select(l => l.Text));

    /// <summary>Indice della riga attiva alla posizione data (-1 se prima della prima riga o senza tempi).</summary>
    public int IndexAt(long positionMs)
    {
        if (!Synced) return -1;
        var idx = -1;
        for (var i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Start >= 0 && Lines[i].Start <= positionMs) idx = i; else if (Lines[i].Start > positionMs) break;
        }
        return idx;
    }
}

/// <summary>Lettura dei formati LRC: normale, con più tempi per riga, «enhanced» con i tempi delle parole.</summary>
public static class Lrc
{
    private static readonly Regex Time = new(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"<(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?>", RegexOptions.Compiled);
    private static readonly Regex Offset = new(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"^\[[a-zA-Z#]+:.*\]$", RegexOptions.Compiled);

    private static long Ms(string m, string s, string frac)
    {
        long f = frac.Length switch
        {
            0 => 0,
            1 => long.Parse(frac, CultureInfo.InvariantCulture) * 100,
            2 => long.Parse(frac, CultureInfo.InvariantCulture) * 10,
            _ => long.Parse(frac[..3], CultureInfo.InvariantCulture),
        };
        return long.Parse(m, CultureInfo.InvariantCulture) * 60000 + long.Parse(s, CultureInfo.InvariantCulture) * 1000 + f;
    }

    public static bool IsLrc(string text) =>
        text.Split('\n').Take(40).Count(l => Time.IsMatch(l)) >= 2;

    public static LyricsText Parse(string text, string source)
    {
        var rows = text.Replace("\r", "").Split('\n');
        if (!IsLrc(text))
        {
            var plain = rows.Select(l => new LLine(-1, -1, l.Trim())).ToList();
            while (plain.Count > 0 && plain[0].Text.Length == 0) plain.RemoveAt(0);
            while (plain.Count > 0 && plain[^1].Text.Length == 0) plain.RemoveAt(plain.Count - 1);
            return new LyricsText(plain, source);
        }
        long offset = 0;
        var raws = new List<(long T, string Body)>();
        foreach (var row in rows)
        {
            var line = row.Trim();
            if (line.Length == 0) continue;
            var om = Offset.Match(line);
            if (om.Success) { offset = long.TryParse(om.Groups[1].Value, out var o) ? o : 0; continue; }
            var stamps = Time.Matches(line);
            if (stamps.Count == 0 || (stamps.Count == 1 && Tag.IsMatch(line) && stamps[0].Index != 0)) continue;
            var body = Time.Replace(line, "");
            foreach (Match m in stamps) raws.Add((Ms(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value), body));
        }
        raws = raws.OrderBy(r => r.T).ToList();
        var res = new List<LLine>();
        for (var i = 0; i < raws.Count; i++)
        {
            var r = raws[i];
            var next = i + 1 < raws.Count ? raws[i + 1].T : r.T + 5000;
            var start = Math.Max(0, r.T - offset);
            var end = Math.Max(start, next - offset);
            var words = ParseWords(r.Body, -offset, end);
            var clean = Spaces.Replace(Word.Replace(r.Body, ""), " ").Trim();
            res.Add(new LLine(start, end, clean, words));
        }
        return new LyricsText(res, source);
    }

    private static List<LWord> ParseWords(string body, long shift, long lineEnd)
    {
        var marks = Word.Matches(body);
        var words = new List<LWord>();
        for (var i = 0; i < marks.Count; i++)
        {
            var m = marks[i];
            var textStart = m.Index + m.Length;
            var textEnd = i + 1 < marks.Count ? marks[i + 1].Index : body.Length;
            var t = body[textStart..textEnd];
            if (string.IsNullOrWhiteSpace(t)) continue;
            var s = Math.Max(0, Ms(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value) + shift);
            var e = i + 1 < marks.Count
                ? Ms(marks[i + 1].Groups[1].Value, marks[i + 1].Groups[2].Value, marks[i + 1].Groups[3].Value) + shift
                : lineEnd;
            words.Add(new LWord(s, Math.Max(s, e), t));
        }
        return words;
    }
}
