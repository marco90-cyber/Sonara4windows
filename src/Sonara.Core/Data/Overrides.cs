using System.Text.Json;

namespace Sonara.Core.Data;

/// <summary>«Cambia sorgente»: l'audio di un brano sostituito con un altro video, valido ovunque (file yt_overrides.json).</summary>
public sealed class YtOverrides
{
    /// <param name="KeepOriginalMeta">tiene titolo, artista e copertina del brano originale</param>
    public sealed record Over(string From, Track To, bool KeepOriginalMeta);

    private readonly string _path;
    private readonly object _gate = new();
    public List<Over> All { get; private set; } = new();
    public event Action? Changed;

    public YtOverrides(string? path = null)
    {
        _path = path ?? Path.Combine(Persist.Root, "yt_overrides.json");
        All = Persist.Read<List<Over>>(_path) ?? new();
    }

    public bool Has(string videoId) => All.Any(o => o.From == videoId);

    /// <summary>L'id del video da suonare davvero per questo brano.</summary>
    public string Target(string videoId) => All.FirstOrDefault(o => o.From == videoId)?.To.VideoId ?? videoId;

    /// <summary>Il brano come va mostrato dopo l'eventuale sostituzione.</summary>
    public Track Apply(Track t)
    {
        var o = All.FirstOrDefault(x => x.From == t.VideoId);
        if (o == null) return t;
        return o.KeepOriginalMeta
            ? t with { DurationSec = o.To.DurationSec > 0 ? o.To.DurationSec : t.DurationSec }
            : o.To with { VideoId = t.VideoId };
    }

    public void Set(string from, Track to, bool keepMeta)
    {
        lock (_gate)
        {
            All = All.Where(o => o.From != from).Append(new Over(from, to, keepMeta)).ToList();
            Save();
        }
    }

    public void Remove(string from) { lock (_gate) { All = All.Where(o => o.From != from).ToList(); Save(); } }
    public void ClearAll() { lock (_gate) { All = new(); Save(); } }

    private void Save()
    {
        try { Persist.WriteAtomic(_path, JsonSerializer.Serialize(All, Persist.Options)); } catch (IOException) { }
        Changed?.Invoke();
    }
}
