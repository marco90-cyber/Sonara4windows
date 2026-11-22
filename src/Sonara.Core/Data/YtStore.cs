using System.Text.Json;

namespace Sonara.Core.Data;

/// <summary>Quanti ascolti (di almeno una trentina di secondi) ha un brano, e quando l'ultima volta.</summary>
public sealed record Play(Track Item, int Count, long FirstAt, long LastAt);

/// <summary>Preferiti, cronologia, ricerche, salvati, ascolti e «non mi piace» (file yt_store.json, nessun account).</summary>
public sealed class YtStore
{
    private sealed class Dto
    {
        public List<Track> Favorites { get; set; } = new();
        public List<Track> History { get; set; } = new();
        public List<string> Searches { get; set; } = new();
        public List<Yt.Entry> Saved { get; set; } = new();
        public List<Track> Dislikes { get; set; } = new();
        public List<Play> Plays { get; set; } = new();
    }

    private readonly string _path;
    private readonly object _gate = new();

    public List<Track> Favorites { get; private set; } = new();
    public List<Track> History { get; private set; } = new();
    public List<string> Searches { get; private set; } = new();
    public List<Yt.Entry> Saved { get; private set; } = new();
    public List<Track> Dislikes { get; private set; } = new();
    public Dictionary<string, Play> Plays { get; private set; } = new();

    /// <summary>Cresce a ogni «mi piace» o «non mi piace»: la Home sa così che deve rifare i suggerimenti.</summary>
    public int TasteVersion { get; private set; }

    public event Action? Changed;

    public YtStore(string? path = null)
    {
        _path = path ?? Path.Combine(Persist.Root, "yt_store.json");
        var d = Persist.Read<Dto>(_path);
        if (d == null) return;
        Favorites = d.Favorites; History = d.History; Searches = d.Searches; Saved = d.Saved; Dislikes = d.Dislikes;
        Plays = d.Plays.GroupBy(p => p.Item.VideoId).ToDictionary(g => g.Key, g => g.First());
    }

    public bool IsFavorite(string videoId) => Favorites.Any(t => t.VideoId == videoId);
    public bool IsDisliked(string videoId) => Dislikes.Any(t => t.VideoId == videoId);
    public bool IsSaved(string browseId) => Saved.Any(e => e.BrowseId == browseId);

    public bool ToggleFavorite(Track t)
    {
        lock (_gate)
        {
            var now = !IsFavorite(t.VideoId);
            Favorites = now ? Favorites.Prepend(t).ToList() : Favorites.Where(x => x.VideoId != t.VideoId).ToList();
            if (now) Dislikes = Dislikes.Where(x => x.VideoId != t.VideoId).ToList();
            TasteVersion++;
            Save();
            return now;
        }
    }

    /// <summary>«Non mi piace»: lo toglie dai preferiti e dai suggerimenti; toccando di nuovo si annulla.</summary>
    public bool ToggleDislike(Track t)
    {
        lock (_gate)
        {
            var now = !IsDisliked(t.VideoId);
            Dislikes = now ? Dislikes.Where(x => x.VideoId != t.VideoId).Prepend(t).ToList() : Dislikes.Where(x => x.VideoId != t.VideoId).ToList();
            if (now) Favorites = Favorites.Where(x => x.VideoId != t.VideoId).ToList();
            TasteVersion++;
            Save();
            return now;
        }
    }

    /// <summary>Salva o toglie dalla libreria una playlist, un album o un artista.</summary>
    public bool ToggleSaved(Yt.Entry e)
    {
        lock (_gate)
        {
            var now = !IsSaved(e.BrowseId);
            Saved = now ? Saved.Prepend(e).ToList() : Saved.Where(x => x.BrowseId != e.BrowseId).ToList();
            Save();
            return now;
        }
    }

    /// <summary>Un brano ascoltato: in cima alla cronologia, senza doppioni, al massimo 300.</summary>
    public void AddHistory(Track t)
    {
        lock (_gate)
        {
            History = History.Where(x => x.VideoId != t.VideoId).Prepend(t).Take(300).ToList();
            Save();
        }
    }

    /// <summary>Un ascolto vero (brano sentito per un po'): conta, ricorda la data e tiene al massimo 2000 brani.</summary>
    public void RecordPlay(Track t, long? nowMs = null)
    {
        lock (_gate)
        {
            var now = nowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Plays.TryGetValue(t.VideoId, out var old);
            Plays[t.VideoId] = new Play(t, (old?.Count ?? 0) + 1, old?.FirstAt ?? now, now);
            if (Plays.Count > 2000) Plays = Plays.Values.OrderByDescending(p => p.LastAt).Take(2000).ToDictionary(p => p.Item.VideoId);
            Save();
        }
    }

    public void AddSearch(string q)
    {
        var t = q.Trim();
        if (t.Length == 0) return;
        lock (_gate)
        {
            Searches = Searches.Where(s => !s.Equals(t, StringComparison.OrdinalIgnoreCase)).Prepend(t).Take(15).ToList();
            Save();
        }
    }

    public void ClearHistory() { lock (_gate) { History = new(); Plays = new(); Save(); } }

    public void ClearAll()
    {
        lock (_gate)
        {
            Favorites = new(); Saved = new(); History = new(); Searches = new(); Plays = new(); Dislikes = new();
            TasteVersion++;
            Save();
        }
    }

    public string ExportJson() => JsonSerializer.Serialize(new Dto
    {
        Favorites = Favorites, History = History, Searches = Searches, Saved = Saved, Dislikes = Dislikes, Plays = Plays.Values.ToList(),
    }, Persist.Options);

    /// <summary>Unisce una copia di riserva a ciò che c'è; restituisce quanti elementi nuovi.</summary>
    public int MergeFrom(string text)
    {
        var d = JsonSerializer.Deserialize<Dto>(text, Persist.Options);
        if (d == null) return 0;
        lock (_gate)
        {
            var fav = d.Favorites.Where(n => !IsFavorite(n.VideoId)).ToList();
            var his = d.History.Where(n => History.All(x => x.VideoId != n.VideoId)).ToList();
            var sav = d.Saved.Where(n => !IsSaved(n.BrowseId)).ToList();
            var dis = d.Dislikes.Where(n => !IsDisliked(n.VideoId)).ToList();
            var newPlays = 0;
            foreach (var p in d.Plays)
            {
                if (!Plays.TryGetValue(p.Item.VideoId, out var cur)) { Plays[p.Item.VideoId] = p; newPlays++; }
                else Plays[p.Item.VideoId] = new Play(cur.Item, Math.Max(cur.Count, p.Count), Math.Min(cur.FirstAt, p.FirstAt), Math.Max(cur.LastAt, p.LastAt));
            }
            Favorites = Favorites.Concat(fav).ToList();
            History = History.Concat(his).Take(300).ToList();
            Saved = Saved.Concat(sav).ToList();
            Dislikes = Dislikes.Concat(dis).ToList();
            var n = fav.Count + his.Count + sav.Count + dis.Count + newPlays;
            if (n > 0) { TasteVersion++; Save(); }
            return n;
        }
    }

    private void Save()
    {
        try { Persist.WriteAtomic(_path, ExportJson()); } catch (IOException) { }
        Changed?.Invoke();
    }
}
