using System.Text.Json;

namespace Sonara.Core.Data;

/// <summary>Playlist importate da Spotify, Deezer e YouTube, con i brani già abbinati: si aprono anche senza rete.</summary>
public sealed class ImportStore
{
    /// <param name="Keys">per ogni brano dell'origine il video scelto ("" = non trovato), serve agli aggiornamenti</param>
    /// <param name="Total">i brani dell'origine</param>
    /// <param name="CheckedAt">l'ultima volta importata o controllata (ms)</param>
    public sealed record Imp(Yt.Entry Entry, List<Track> Tracks, Dictionary<string, string> Keys, int Total, long CheckedAt);

    private sealed class Dto { public List<Imp> Items { get; set; } = new(); }

    private readonly string _path;
    private readonly object _gate = new();

    public List<Imp> All { get; private set; } = new();
    public event Action? Changed;

    public ImportStore(string? path = null)
    {
        _path = path ?? Path.Combine(Persist.Root, "yt_imports.json");
        All = Persist.Read<Dto>(_path)?.Items ?? new();
    }

    public long LastCheck => All.Count == 0 ? 0 : All.Max(i => i.CheckedAt);

    public Imp? Find(string importId) => All.FirstOrDefault(i => i.Entry.BrowseId == importId);

    /// <summary>Salva una playlist nuova, o ne sostituisce una già presente mantenendo la sua posizione.</summary>
    public void Put(Imp imp)
    {
        lock (_gate)
        {
            var i = All.FindIndex(x => x.Entry.BrowseId == imp.Entry.BrowseId);
            var list = All.ToList();
            if (i < 0) list.Insert(0, imp); else list[i] = imp;
            All = list;
            Save();
        }
    }

    public void Remove(string importId)
    {
        lock (_gate) { All = All.Where(i => i.Entry.BrowseId != importId).ToList(); Save(); }
    }

    public void ClearAll() { lock (_gate) { All = new(); Save(); } }

    public string ExportJson() => JsonSerializer.Serialize(new Dto { Items = All }, Persist.Options);

    public int MergeFrom(string text)
    {
        var d = JsonSerializer.Deserialize<Dto>(text, Persist.Options);
        if (d == null) return 0;
        lock (_gate)
        {
            var add = d.Items.Where(n => All.All(i => i.Entry.BrowseId != n.Entry.BrowseId)).ToList();
            if (add.Count == 0) return 0;
            All = All.Concat(add).ToList();
            Save();
            return add.Count;
        }
    }

    private void Save()
    {
        try { Persist.WriteAtomic(_path, ExportJson()); } catch (IOException) { }
        Changed?.Invoke();
    }
}
