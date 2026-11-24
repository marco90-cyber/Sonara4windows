using Sonara.Core;
using Sonara.Core.Data;
using Sonara.Core.Lyrics;

namespace Sonara.App.Services;

/// <summary>Il testo del brano in corso: lo cerca quando il brano cambia (con cache su disco).</summary>
public sealed class LyricsService
{
    private readonly AppServices _a;
    private string _videoId = "";
    private long _token;

    public LyricsText? Current { get; private set; }
    public bool Loading { get; private set; }
    public bool NotFound { get; private set; }
    public event Action? Changed;
    public event Action? OffsetChanged;

    private readonly Dictionary<string, long> _offsets;
    private static string OffsetPath => Path.Combine(Persist.Root, "lyric_offsets.json");

    /// <summary>Ritardo di sincronizzazione del brano, in ms: con un valore positivo il testo arriva prima, con uno negativo dopo.</summary>
    public long Offset(string videoId) => _offsets.GetValueOrDefault(videoId);

    public void SetOffset(string videoId, long ms)
    {
        ms = Math.Clamp(ms, -10_000, 10_000);
        if (ms == 0) _offsets.Remove(videoId); else _offsets[videoId] = ms;
        try { Persist.WriteAtomic(OffsetPath, System.Text.Json.JsonSerializer.Serialize(_offsets, Persist.Options)); } catch (IOException) { }
        OffsetChanged?.Invoke();
    }

    /// <summary>La riga in corso del testo, tenendo conto del ritardo del brano.</summary>
    public int IndexNow(LyricsText ly)
    {
        var t = _a.Player.Current;
        var off = t == null ? 0 : Offset(t.VideoId);
        return ly.IndexAt((long)_a.Player.Position.TotalMilliseconds + off);
    }

    public LyricsService(AppServices a)
    {
        _a = a;
        _offsets = Persist.Read<Dictionary<string, long>>(OffsetPath) ?? new();
        a.Player.StateChanged += OnPlayer;
    }

    private string DirPath { get { var d = Path.Combine(Persist.CacheDir, "lyrics"); Directory.CreateDirectory(d); return d; } }

    private void OnPlayer()
    {
        var t = _a.Player.Current;
        if (t == null || t.VideoId == _videoId) return;
        _videoId = t.VideoId;
        Current = null; NotFound = false; Loading = true;
        Changed?.Invoke();
        _ = LoadAsync(t, Interlocked.Increment(ref _token));
    }

    public async Task ReloadAsync()
    {
        var t = _a.Player.Current;
        if (t == null) return;
        try { File.Delete(Path.Combine(DirPath, t.VideoId + ".lrc")); } catch (IOException) { }
        Current = null; NotFound = false; Loading = true; Changed?.Invoke();
        await LoadAsync(t, Interlocked.Increment(ref _token));
    }

    private async Task LoadAsync(Track t, long token)
    {
        LyricsText? res = null;
        try
        {
            var file = Path.Combine(DirPath, t.VideoId + ".lrc");
            if (File.Exists(file)) res = Lrc.Parse(await File.ReadAllTextAsync(file), "LRCLIB");
            else
            {
                res = await LyricsProviders.FetchAsync(new LyricsQuery(t.Title, t.Artist, "", (int)t.DurationSec));
                if (res != null && !res.IsEmpty)
                {
                    var raw = res.Synced ? string.Join("\n", res.Lines.Select(l => $"[{Stamp(l.Start)}]{l.Text}")) : res.PlainText;
                    await File.WriteAllTextAsync(file, raw);
                }
            }
        }
        catch (Exception) { }
        if (token != Volatile.Read(ref _token)) return;
        _a.RunUi(() =>
        {
            Current = res != null && !res.IsEmpty ? res : null;
            NotFound = Current == null; Loading = false;
            Changed?.Invoke();
        });
    }

    private static string Stamp(long ms)
    {
        var t = Math.Max(0, ms);
        return $"{t / 60000:00}:{t / 1000 % 60:00}.{t % 1000 / 10:00}";
    }
}
