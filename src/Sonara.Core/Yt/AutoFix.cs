using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Sonara.Core.Data;

namespace Sonara.Core.Yt;

/// <summary>
/// «Correzione brani»: con l'impostazione attiva ogni brano che entra in coda viene controllato in background;
/// se la durata non torna con quella ufficiale e si trova un audio sicuro, la sorgente cambia da sola.
/// Non tocca i brani già cambiati a mano o ripristinati. Ogni brano controllato si ricorda per 14 giorni.
/// </summary>
public sealed class AutoFix
{
    private static readonly long TtlMs = 14L * 24 * 3600 * 1000;
    private const int MaxRemembered = 5000;

    private sealed class Dto { public Dictionary<string, long> Checked { get; set; } = new(); public List<string> Ignored { get; set; } = new(); }

    private readonly YtOverrides _ov;
    private readonly string _path;
    private readonly ConcurrentDictionary<string, long> _checked = new();
    private readonly ConcurrentDictionary<string, byte> _ignored = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private readonly Channel<Track> _queue = Channel.CreateUnbounded<Track>();
    private int _saving;

    public Func<bool> Enabled { get; set; } = () => false;
    /// <summary>Chiamato quando l'audio di un brano è stato sostituito (originale, sostituto).</summary>
    public event Action<Track, Track>? Applied;

    public AutoFix(YtOverrides overrides, string? path = null)
    {
        _ov = overrides;
        _path = path ?? Path.Combine(Persist.Root, "yt_autofix.json");
        var d = Persist.Read<Dto>(_path);
        if (d != null)
        {
            foreach (var (k, v) in d.Checked) _checked[k] = v;
            foreach (var k in d.Ignored) _ignored[k] = 0;
        }
        for (var i = 0; i < 2; i++) _ = Task.Run(Worker);
    }

    public void Consider(Track t)
    {
        if (!Enabled()) return;
        var id = t.VideoId;
        if (_ignored.ContainsKey(id) || _ov.Has(id)) return;
        if (_checked.TryGetValue(id, out var at) && at > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - TtlMs) return;
        if (!_pending.TryAdd(id, 0)) return;
        _queue.Writer.TryWrite(t);
    }

    /// <summary>L'utente ha ripristinato la sorgente originale: la correzione non lo tocca più.</summary>
    public void Ignore(string videoId) { _ignored[videoId] = 0; Save(); }

    private async Task Worker()
    {
        await foreach (var t in _queue.Reader.ReadAllAsync())
        {
            try { await Process(t).ConfigureAwait(false); }
            catch (Exception) { }
            finally { _pending.TryRemove(t.VideoId, out _); }
            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    private async Task Process(Track t)
    {
        var id = t.VideoId;
        if (!Enabled() || _ov.Has(id) || _ignored.ContainsKey(id)) return;
        if (t.DurationSec <= 0) { Mark(id); return; }
        var look = await DurationRef.LookupAsync(t.Title, t.Artist).ConfigureAwait(false);
        if (!look.Reachable) return;
        var r = look.Ref;
        if (r == null || !DurationMatch.IsOff(t.DurationSec, r.Seconds)) { Mark(id); return; }
        List<DurationMatch.Source> cand;
        try { cand = await DurationMatch.SearchSourcesAsync($"{t.Title} {DurationMatch.MainArtist(t.Artist)}").ConfigureAwait(false); }
        catch (Exception) { return; }
        var pick = DurationMatch.Best(t.Title, t.Artist, r.Seconds, cand);
        if (pick is { Sure: true } && Enabled() && !_ov.Has(id))
        {
            _ov.Set(id, pick.Source.Item, false);
            Applied?.Invoke(t, pick.Source.Item);
        }
        Mark(id);
    }

    private void Mark(string id)
    {
        _checked[id] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_checked.Count > MaxRemembered)
            foreach (var k in _checked.OrderBy(x => x.Value).Take(500).Select(x => x.Key).ToList()) _checked.TryRemove(k, out _);
        Save();
    }

    private void Save()
    {
        if (Interlocked.Exchange(ref _saving, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000).ConfigureAwait(false);
            Interlocked.Exchange(ref _saving, 0);
            try { Persist.WriteAtomic(_path, JsonSerializer.Serialize(new Dto { Checked = _checked.ToDictionary(x => x.Key, x => x.Value), Ignored = _ignored.Keys.ToList() }, Persist.Options)); } catch (Exception) { }
        });
    }
}
