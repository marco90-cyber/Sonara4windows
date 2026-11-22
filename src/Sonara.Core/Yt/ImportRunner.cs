using Sonara.Core.Data;

namespace Sonara.Core.Yt;

/// <summary>Esegue le importazioni e gli aggiornamenti delle playlist uno alla volta, in background. Porting di ImportRunner.kt.</summary>
public sealed class ImportRunner
{
    public sealed record Snapshot(string Name, int Done, int Total, int Queued);

    /// <summary>Cosa è cambiato in una playlist aggiornata.</summary>
    public sealed record Change(string Title, int Added, int Removed, bool Reordered)
    {
        public bool Any => Added > 0 || Removed > 0 || Reordered;
    }

    public sealed class NoTracksException : IOException { public NoTracksException(string m) : base(m) { } }

    private readonly ImportStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pending;
    private CancellationTokenSource _cts = new();
    private string _name = "";
    private int _done, _total;

    public ImportRunner(ImportStore store) => _store = store;

    /// <summary>L'avanzamento è cambiato (da qualunque thread).</summary>
    public event Action? Progress;

    public bool Active => Volatile.Read(ref _pending) > 0;
    public Snapshot Current => new(_name, _done, _total, Volatile.Read(ref _pending));

    public void CancelAll() => _cts.Cancel();

    private void Report(string name, int done, int total)
    {
        _name = name; _done = done; _total = total;
        Progress?.Invoke();
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> job)
    {
        if (Interlocked.Increment(ref _pending) == 1) { _cts.Dispose(); _cts = new CancellationTokenSource(); }
        var ct = _cts.Token;
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try { return await job(ct).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
        finally
        {
            if (Interlocked.Decrement(ref _pending) == 0) { _name = ""; _done = 0; _total = 0; }
            Progress?.Invoke();
        }
    }

    /// <summary>Importa una playlist nuova e la salva.</summary>
    public Task<ImportStore.Imp> ImportPlaylistAsync(PlaylistImport.Ref r, Action<PlaylistImport.Meta, List<Track>, int, int>? listener = null) =>
        RunAsync(async ct =>
        {
            var res = await PlaylistImport.ImportAsync(r, null, null, (meta, found, d, t) =>
            {
                Report(meta.Name, d, t);
                listener?.Invoke(meta, found, d, t);
            }, ct).ConfigureAwait(false);
            if (res.Tracks.Count == 0) throw new NoTracksException("Non ho trovato nessun brano di questa playlist su YouTube Music.");
            var entry = new Entry(Kind.Playlists, r.ImportId, res.Meta.Name, PlaylistImport.Subtitle(r.Source, res.Tracks.Count, res.Total), res.Meta.Cover);
            var imp = new ImportStore.Imp(entry, res.Tracks, res.Keys, res.Total, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _store.Put(imp);
            return imp;
        });

    /// <summary>Rilegge una playlist importata: i brani già abbinati restano, i nuovi vengono cercati, quelli spariti tolti.</summary>
    public Task<Change> RefreshPlaylistAsync(ImportStore.Imp imp, Action<PlaylistImport.Meta, List<Track>, int, int>? listener = null) =>
        RunAsync(ct => RefreshCoreAsync(imp, listener, ct));

    private async Task<Change> RefreshCoreAsync(ImportStore.Imp imp, Action<PlaylistImport.Meta, List<Track>, int, int>? listener, CancellationToken ct)
    {
        var r = PlaylistImport.RefOf(imp.Entry.BrowseId) ?? throw new IOException("Playlist non riconosciuta");
        var res = await PlaylistImport.ImportAsync(r, imp.Keys, imp.Tracks.GroupBy(t => t.VideoId).ToDictionary(g => g.Key, g => g.First()), (meta, found, d, t) =>
        {
            Report(meta.Name, d, t);
            listener?.Invoke(meta, found, d, t);
        }, ct).ConfigureAwait(false);
        if (res.Tracks.Count == 0) throw new NoTracksException("Nessun brano trovato: tengo la copia che avevo.");
        var oldIds = imp.Tracks.Select(t => t.VideoId).ToList();
        var newIds = res.Tracks.Select(t => t.VideoId).ToList();
        var oldSet = oldIds.ToHashSet(); var newSet = newIds.ToHashSet();
        var change = new Change(res.Meta.Name.Length > 0 ? res.Meta.Name : imp.Entry.Title,
            newIds.Count(i => !oldSet.Contains(i)), oldIds.Count(i => !newSet.Contains(i)),
            newSet.SetEquals(oldSet) && !newIds.SequenceEqual(oldIds));
        var entry = imp.Entry with
        {
            Title = change.Title,
            Subtitle = PlaylistImport.Subtitle(r.Source, res.Tracks.Count, res.Total),
            Thumb = res.Meta.Cover.Length > 0 ? res.Meta.Cover : imp.Entry.Thumb,
        };
        _store.Put(new ImportStore.Imp(entry, res.Tracks, res.Keys.Count > 0 ? res.Keys : imp.Keys, res.Total, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        return change;
    }

    /// <summary>Aggiorna tutte le importate, una dopo l'altra; quelle che non si riesce a leggere restano come sono.</summary>
    public Task<List<Change>> RefreshAllAsync() =>
        RunAsync(async ct =>
        {
            var changes = new List<Change>();
            foreach (var imp in _store.All.ToList())
            {
                if (ct.IsCancellationRequested) break;
                if (PlaylistImport.RefOf(imp.Entry.BrowseId) == null) continue;
                try { changes.Add(await RefreshCoreAsync(imp, null, ct).ConfigureAwait(false)); }
                catch (OperationCanceledException) { break; }
                catch (Exception) { /* resta com'è */ }
            }
            return changes;
        });
}
