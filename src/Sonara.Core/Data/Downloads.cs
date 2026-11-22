using System.Text.Json;
using System.Text.RegularExpressions;
using Sonara.Core.Yt;

namespace Sonara.Core.Data;

/// <summary>
/// Download offline in una cartella a scelta, con una sottocartella per playlist/album/artista, un file indice
/// (.sonara-downloads.json) e una copia di riserva di preferiti e playlist (.sonara-library.json) per ritrovare tutto dopo una reinstallazione.
/// </summary>
public sealed class DownloadManager
{
    public sealed record Item(string VideoId, string Title, string Artist, long DurationSec, string Thumb, string Group, string RelPath);

    private sealed class IndexDto { public List<Item> Items { get; set; } = new(); }

    public sealed record Job(string Group, int Done, int Total, string Current, bool Running);

    private readonly AppSettings _settings;
    private readonly StreamResolver _resolver;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _queue = new(1, 1);
    private CancellationTokenSource _cts = new();
    private int _pending;
    private readonly Dictionary<string, (int Done, int Total)> _groups = new();
    private string _current = "";

    public List<Item> Items { get; private set; } = new();
    public event Action? Changed;

    public DownloadManager(AppSettings settings, StreamResolver resolver)
    {
        _settings = settings;
        _resolver = resolver;
        LoadIndex();
    }

    public bool HasFolder => _settings.DownloadFolder.Length > 0 && Directory.Exists(_settings.DownloadFolder);
    public string Folder => _settings.DownloadFolder;
    public bool Active => Volatile.Read(ref _pending) > 0;

    public IReadOnlyDictionary<string, (int Done, int Total)> Progress { get { lock (_gate) return new Dictionary<string, (int, int)>(_groups); } }
    public string Current => _current;

    private string IndexPath => Path.Combine(_settings.DownloadFolder, ".sonara-downloads.json");
    private string LibraryPath => Path.Combine(_settings.DownloadFolder, ".sonara-library.json");

    private static readonly Regex Bad = new("[\\\\/:*?\"<>|]+", RegexOptions.Compiled);

    public static string Safe(string s, int max = 80)
    {
        var t = Bad.Replace(s, "_").Trim().TrimEnd('.');
        return t.Length == 0 ? "_" : t.Length > max ? t[..max].TrimEnd() : t;
    }

    /// <summary>Rilegge l'indice dalla cartella scelta (anche dopo una reinstallazione).</summary>
    public void LoadIndex()
    {
        Items = new();
        if (!HasFolder) return;
        var d = Persist.Read<IndexDto>(IndexPath);
        Items = (d?.Items ?? new()).Where(i => File.Exists(Path.Combine(_settings.DownloadFolder, i.RelPath))).ToList();
        Changed?.Invoke();
    }

    private void SaveIndex()
    {
        if (!HasFolder) return;
        try { Persist.WriteAtomic(IndexPath, JsonSerializer.Serialize(new IndexDto { Items = Items }, Persist.Options)); } catch (IOException) { }
    }

    public Item? Find(string videoId) => Items.FirstOrDefault(i => i.VideoId == videoId);
    public bool IsDownloaded(string videoId) => Find(videoId) != null;
    public string? FileOf(string videoId)
    {
        var i = Find(videoId);
        if (i == null) return null;
        var p = Path.Combine(_settings.DownloadFolder, i.RelPath);
        return File.Exists(p) ? p : null;
    }

    public IEnumerable<IGrouping<string, Item>> Groups() => Items.GroupBy(i => i.Group).OrderBy(g => g.Key);

    public void CancelAll() { _cts.Cancel(); }

    /// <summary>Scarica i brani in una sottocartella col nome di playlist, album o artista.</summary>
    public async Task DownloadAsync(string group, IEnumerable<Track> tracks)
    {
        if (!HasFolder) throw new InvalidOperationException("Cartella dei download non scelta");
        var list = tracks.Where(t => !IsDownloaded(t.VideoId)).ToList();
        if (list.Count == 0) return;
        var groupName = Safe(group);
        lock (_gate) _groups[groupName] = (0, list.Count);
        if (Interlocked.Increment(ref _pending) == 1) { _cts.Dispose(); _cts = new CancellationTokenSource(); }
        var ct = _cts.Token;
        try
        {
            await _queue.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var dir = Path.Combine(_settings.DownloadFolder, groupName);
                Directory.CreateDirectory(dir);
                var done = 0;
                foreach (var t in list)
                {
                    ct.ThrowIfCancellationRequested();
                    _current = $"{t.Artist} – {t.Title}";
                    Changed?.Invoke();
                    try
                    {
                        var r = await _resolver.ResolveAsync(t.VideoId, null, ct, preferMp4: true).ConfigureAwait(false);
                        var name = Safe($"{t.Artist} - {t.Title}") + Path.GetExtension(r.FilePath);
                        var target = Path.Combine(dir, name);
                        if (File.Exists(target)) target = Path.Combine(dir, Safe($"{t.Artist} - {t.Title}") + $" [{t.VideoId}]" + Path.GetExtension(r.FilePath));
                        File.Copy(r.FilePath, target, true);
                        lock (_gate)
                        {
                            Items = Items.Append(new Item(t.VideoId, t.Title, t.Artist, t.DurationSec, t.Thumb, groupName, Path.GetRelativePath(_settings.DownloadFolder, target))).ToList();
                            SaveIndex();
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* un brano non riuscito non ferma gli altri */ }
                    done++;
                    lock (_gate) _groups[groupName] = (done, list.Count);
                    Changed?.Invoke();
                }
            }
            finally { _queue.Release(); }
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_gate) _groups.Remove(groupName);
            if (Interlocked.Decrement(ref _pending) == 0) _current = "";
            Changed?.Invoke();
        }
    }

    /// <summary>Elimina i file scaricati di un gruppo (playlist/album/artista) o di un solo brano.</summary>
    public void Delete(Func<Item, bool> match)
    {
        lock (_gate)
        {
            foreach (var i in Items.Where(match).ToList())
            {
                try { File.Delete(Path.Combine(_settings.DownloadFolder, i.RelPath)); } catch (IOException) { }
                var dir = Path.GetDirectoryName(Path.Combine(_settings.DownloadFolder, i.RelPath));
                try { if (dir != null && dir != _settings.DownloadFolder && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (IOException) { }
            }
            Items = Items.Where(i => !match(i)).ToList();
            SaveIndex();
        }
        Changed?.Invoke();
    }

    // ---- copia di riserva

    /// <summary>Scrive nella cartella dei download la copia di riserva (preferiti, cronologia, salvati, importate).</summary>
    public void WriteBackup(YtStore store, ImportStore imports)
    {
        if (!HasFolder) return;
        try
        {
            var json = JsonSerializer.Serialize(new { store = store.ExportJson(), imports = imports.ExportJson() }, Persist.Options);
            Persist.WriteAtomic(LibraryPath, json);
        }
        catch (IOException) { }
    }

    /// <summary>Unisce la copia di riserva della cartella scelta; restituisce quanti elementi nuovi.</summary>
    public int RestoreBackup(YtStore store, ImportStore imports)
    {
        if (!HasFolder || !File.Exists(LibraryPath)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(LibraryPath));
            var n = 0;
            if (doc.RootElement.TryGetProperty("store", out var s) && s.GetString() is string st) n += store.MergeFrom(st);
            if (doc.RootElement.TryGetProperty("imports", out var i) && i.GetString() is string im) n += imports.MergeFrom(im);
            return n;
        }
        catch (Exception) { return 0; }
    }
}
