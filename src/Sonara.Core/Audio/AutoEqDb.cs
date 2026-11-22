using System.Net;
using System.Text.RegularExpressions;
using Sonara.Core.Data;

namespace Sonara.Core.Audio;

/// <summary>Profili AutoEQ (https://github.com/jaakkopasanen/AutoEq, licenza MIT): indice delle cuffie e profilo scaricato da GitHub.</summary>
public static class AutoEqDb
{
    private const string Raw = "https://raw.githubusercontent.com/jaakkopasanen/AutoEq/master/results/";
    private static readonly Regex IndexRe = new(@"^- \[(.+?)\]\(\./(.+?)\) by (.+?)(?: on (.+))?$", RegexOptions.Compiled);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public sealed record Entry(string Name, string Source, string Rig, string Path)
    {
        public string Key => $"{Name}|{Source}|{Rig}";
        public string Subtitle => string.Join(" · ", new[] { Source, Rig }.Where(s => s.Length > 0));
    }

    private static List<Entry>? _cache;
    private static string IndexPath => System.IO.Path.Combine(Persist.Root, "autoeq_index.md");

    public static List<Entry> ParseIndex(string md)
    {
        var res = new List<Entry>();
        foreach (var line in md.Split('\n'))
        {
            var m = IndexRe.Match(line.Trim());
            if (!m.Success) continue;
            var path = WebUtility.UrlDecode(m.Groups[2].Value.Replace("+", "%2B"));
            var parts = path.Split('/');
            if (parts.Length < 3) continue;
            res.Add(new Entry(m.Groups[1].Value, parts[0], string.Join("/", parts[1..^1]), path));
        }
        return res;
    }

    /// <summary>L'elenco delle cuffie (dalla copia locale; la prima volta serve la rete).</summary>
    public static async Task<List<Entry>> AllAsync(bool refresh = false, CancellationToken ct = default)
    {
        if (_cache != null && !refresh) return _cache;
        string? md = null;
        if (!refresh && File.Exists(IndexPath)) md = await File.ReadAllTextAsync(IndexPath, ct).ConfigureAwait(false);
        if (md == null)
        {
            md = await Http.GetStringAsync(Raw + "INDEX.md", ct).ConfigureAwait(false);
            File.WriteAllText(IndexPath, md);
        }
        _cache = ParseIndex(md);
        return _cache;
    }

    public static List<Entry> Search(IEnumerable<Entry> list, string q, int limit = 200)
    {
        var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return list.Where(e => terms.All(t => e.Name.Contains(t, StringComparison.OrdinalIgnoreCase))).Take(limit).ToList();
    }

    /// <summary>Scarica il profilo parametrico completo di una cuffia.</summary>
    public static async Task<PresetText.Parametric> ResolveAsync(Entry e, CancellationToken ct = default)
    {
        var segs = e.Path.Split('/').Append($"{e.Name} ParametricEQ.txt").Select(s => Uri.EscapeDataString(s));
        var text = await Http.GetStringAsync(Raw + string.Join("/", segs), ct).ConfigureAwait(false);
        return PresetText.ParseParametric(text, e.Name) ?? throw new IOException("profilo non leggibile");
    }
}
