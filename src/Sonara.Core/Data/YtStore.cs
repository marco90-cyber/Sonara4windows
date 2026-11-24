using System.Text.Json;

namespace Sonara.Core.Data;

/// <summary>Quanti ascolti (di almeno una trentina di secondi) ha un brano, e quando l'ultima volta.</summary>
public sealed record Play(Track Item, int Count, long FirstAt, long LastAt);

/// <summary>Una playlist creata dall'utente in Sonara.</summary>
public sealed record MyPlaylist(string Id, string Name, List<Track> Items);

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
        public Dictionary<string, int> Skips { get; set; } = new();
        public Dictionary<string, string> TrackGenres { get; set; } = new();
        public Dictionary<string, string> ArtistGenres { get; set; } = new();
        public List<MyPlaylist> Playlists { get; set; } = new();
    }

    private readonly string _path;
    private readonly object _gate = new();

    public List<Track> Favorites { get; private set; } = new();
    public List<Track> History { get; private set; } = new();
    public List<string> Searches { get; private set; } = new();
    public List<Yt.Entry> Saved { get; private set; } = new();
    public List<Track> Dislikes { get; private set; } = new();
    public Dictionary<string, Play> Plays { get; private set; } = new();
    /// <summary>Salti entro 30 secondi per brano: pesano -0,5 l'uno, fino a -1.</summary>
    public Dictionary<string, int> Skips { get; private set; } = new();
    /// <summary>Genere (id di FavGenres) dei brani dove l'origine è nota.</summary>
    public Dictionary<string, string> TrackGenres { get; private set; } = new();
    /// <summary>Genere per artista (nome normalizzato → id di FavGenres, vuoto se sconosciuto), ricavato da Deezer.</summary>
    public Dictionary<string, string> ArtistGenres { get; private set; } = new();
    /// <summary>Le playlist create da te; le più recenti in alto.</summary>
    public List<MyPlaylist> MyPlaylists { get; private set; } = new();

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
        Skips = d.Skips.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        TrackGenres = d.TrackGenres; ArtistGenres = d.ArtistGenres; MyPlaylists = d.Playlists;
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
            NoteGenre(t.VideoId);
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
            NoteGenre(t.VideoId);
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
            NoteGenre(t.VideoId);
            Save();
        }
    }

    /// <summary>Un salto manuale entro 30 secondi dall'inizio del brano.</summary>
    public void RecordSkip(Track t)
    {
        lock (_gate)
        {
            Skips[t.VideoId] = Math.Min(50, Skips.GetValueOrDefault(t.VideoId) + 1);
            NoteGenre(t.VideoId);
            TasteVersion++;
            Save();
        }
    }

    /// <summary>Se il brano è arrivato da uno scaffale di un genere ricorda quel genere.</summary>
    private void NoteGenre(string videoId)
    {
        var g = Yt.Genres.OriginOf(videoId);
        if (g == null || TrackGenres.GetValueOrDefault(videoId) == g) return;
        TrackGenres[videoId] = g;
        if (TrackGenres.Count > 2000) TrackGenres = TrackGenres.Skip(TrackGenres.Count - 2000).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>Aggiunge i generi di artisti appena ricavati da Deezer.</summary>
    public void AddArtistGenres(IReadOnlyDictionary<string, string> more)
    {
        if (more.Count == 0) return;
        lock (_gate) { foreach (var (k, v) in more) ArtistGenres[k] = v; Save(); }
    }

    /// <summary>Aggiunge alla libreria (se non c'è già) una playlist, un album o un artista, senza mai toglierlo.</summary>
    public void AddSaved(Yt.Entry e)
    {
        lock (_gate) { if (IsSaved(e.BrowseId)) return; Saved = Saved.Prepend(e).ToList(); Save(); }
    }

    // ------------------------------------------------------------------ playlist tue

    public MyPlaylist CreatePlaylist(string name)
    {
        lock (_gate)
        {
            var n = name.Trim();
            var p = new MyPlaylist("p" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x") + Random.Shared.Next(1000), n.Length == 0 ? "Nuova playlist" : n, new());
            MyPlaylists = MyPlaylists.Prepend(p).ToList();
            Save();
            return p;
        }
    }

    public void RenamePlaylist(string id, string name)
    {
        var n = name.Trim();
        if (n.Length == 0) return;
        lock (_gate) { MyPlaylists = MyPlaylists.Select(p => p.Id == id ? p with { Name = n } : p).ToList(); Save(); }
    }

    public void DeletePlaylist(string id) { lock (_gate) { MyPlaylists = MyPlaylists.Where(p => p.Id != id).ToList(); Save(); } }

    /// <summary>Aggiunge il brano in fondo alla playlist; false se c'era già.</summary>
    public bool AddToPlaylist(string id, Track t)
    {
        lock (_gate)
        {
            var p = MyPlaylists.FirstOrDefault(x => x.Id == id);
            if (p == null || p.Items.Any(i => i.VideoId == t.VideoId)) return false;
            MyPlaylists = MyPlaylists.Select(x => x.Id == id ? x with { Items = x.Items.Append(t).ToList() } : x).ToList();
            Save();
            return true;
        }
    }

    public void RemoveFromPlaylist(string id, string videoId)
    {
        lock (_gate)
        {
            MyPlaylists = MyPlaylists.Select(x => x.Id == id ? x with { Items = x.Items.Where(i => i.VideoId != videoId).ToList() } : x).ToList();
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

    public void ClearHistory() { lock (_gate) { History = new(); Plays = new(); Skips = new(); Save(); } }

    public void ClearAll()
    {
        lock (_gate)
        {
            Favorites = new(); Saved = new(); History = new(); Searches = new(); Plays = new(); Dislikes = new();
            Skips = new(); TrackGenres = new(); MyPlaylists = new();
            TasteVersion++;
            Save();
        }
    }

    public string ExportJson() => JsonSerializer.Serialize(new Dto
    {
        Favorites = Favorites, History = History, Searches = Searches, Saved = Saved, Dislikes = Dislikes, Plays = Plays.Values.ToList(),
        Skips = Skips, TrackGenres = TrackGenres, ArtistGenres = ArtistGenres, Playlists = MyPlaylists,
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
            // playlist tue: nuove per id; se c'è già, si uniscono i brani
            var newLists = 0;
            var lists = MyPlaylists.ToDictionary(p => p.Id);
            foreach (var np in d.Playlists)
            {
                if (!lists.TryGetValue(np.Id, out var c)) { lists[np.Id] = np; newLists++; }
                else lists[np.Id] = c with { Items = c.Items.Concat(np.Items.Where(i => c.Items.All(x => x.VideoId != i.VideoId))).ToList() };
            }
            MyPlaylists = d.Playlists.Select(p => p.Id).Concat(MyPlaylists.Select(p => p.Id)).Distinct().Select(id => lists[id]).ToList();
            foreach (var (k, v) in d.Skips) Skips[k] = Math.Max(Skips.GetValueOrDefault(k), v);
            foreach (var (k, v) in d.TrackGenres) TrackGenres.TryAdd(k, v);
            foreach (var (k, v) in d.ArtistGenres) ArtistGenres.TryAdd(k, v);
            var n = fav.Count + his.Count + sav.Count + dis.Count + newPlays + newLists;
            Save();
            if (n > 0) TasteVersion++;
            return n;
        }
    }

    private void Save()
    {
        try { Persist.WriteAtomic(_path, ExportJson()); } catch (IOException) { }
        Changed?.Invoke();
    }
}
