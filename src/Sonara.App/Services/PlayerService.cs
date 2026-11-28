using NAudio.MediaFoundation;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Microsoft.UI.Dispatching;
using Sonara.Core;
using Sonara.Core.Audio;
using Sonara.Core.Data;
using Sonara.Core.Yt;

namespace Sonara.App.Services;

public enum RepeatMode { Off, All, One }

/// <summary>Coda, riproduzione (NAudio → catena DSP → WASAPI), prossimo brano preparato in anticipo.</summary>
public sealed class PlayerService : IDisposable
{
    private readonly AppServices _a;
    private readonly DispatcherQueueTimer _timer;
    private readonly DspChain _chain = new();
    private readonly Random _rnd = new();
    private IWavePlayer? _out;
    private MediaFoundationReader? _reader;
    private VolumeSampleProvider? _vol;
    private DspProvider? _dsp;
    private CancellationTokenSource? _loadCts;
    private bool _userStop;
    private bool _counted;
    private double _playedSec;
    private long _loadToken;

    private List<Track> _items = new();
    private List<int> _order = new();
    private int _pos = -1;

    public PlayerService(AppServices a)
    {
        _a = a;
        _timer = a.UI.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (_, _) => Tick();
    }

    public event Action? StateChanged;
    public event Action? QueueChanged;
    public event Action? PositionChanged;
    public event Action<string>? Error;

    public Track? Current => _pos >= 0 && _pos < _order.Count ? _a.Resolve(_items[_order[_pos]]) : null;
    public IReadOnlyList<Track> Upcoming => _order.Skip(_pos + 1).Select(i => _a.Resolve(_items[i])).ToList();
    public IReadOnlyList<Track> Queue => _order.Select(i => _a.Resolve(_items[i])).ToList();
    public int Index => _pos;
    public bool Playing { get; private set; }
    public bool Loading { get; private set; }
    public bool Shuffle { get; private set; }
    public RepeatMode Repeat { get; private set; }
    public string QualityText { get; private set; } = "";
    public double PeakL => _dsp?.PeakL ?? 0;
    public double PeakR => _dsp?.PeakR ?? 0;
    public DspChain Chain => _chain;

    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.FromSeconds(Current?.DurationSec ?? 0);

    public double Volume
    {
        get => _a.Settings.Volume;
        set
        {
            _a.Settings.Volume = Math.Clamp(value, 0, 1);
            if (_vol != null) _vol.Volume = (float)_a.Settings.Volume;
            _a.Settings.Save();
        }
    }

    // ------------------------------------------------------------------ coda

    /// <summary>Suona la lista dal brano <paramref name="start"/> (o in ordine casuale).</summary>
    public void PlayList(IList<Track> list, int start = 0, bool shuffle = false)
    {
        if (list.Count == 0) return;
        _items = list.ToList();
        foreach (var x in _items) _a.AutoFix.Consider(x);
        // con "Casuale" attivo anche un brano scelto a mano parte dentro l'ordine casuale
        var wasShuffle = Shuffle;
        Shuffle = shuffle || wasShuffle;
        BuildOrder(shuffle ? _rnd.Next(_items.Count) : Math.Clamp(start, 0, _items.Count - 1));
        QueueChanged?.Invoke();
        _ = StartCurrentAsync();
    }

    public void PlayOne(Track t) => PlayList(new[] { t }, 0);

    private void BuildOrder(int first)
    {
        _order = Enumerable.Range(0, _items.Count).ToList();
        if (Shuffle)
        {
            _order.Remove(first);
            for (var i = _order.Count - 1; i > 0; i--) { var j = _rnd.Next(i + 1); (_order[i], _order[j]) = (_order[j], _order[i]); }
            _order.Insert(0, first);
            _pos = 0;
        }
        else _pos = first;
    }

    public void SetShuffle(bool on)
    {
        if (Shuffle == on) return;
        var cur = _pos >= 0 && _pos < _order.Count ? _order[_pos] : 0;
        Shuffle = on;
        if (_items.Count > 0) BuildOrder(cur);
        QueueChanged?.Invoke();
        StateChanged?.Invoke();
    }

    public void CycleRepeat()
    {
        Repeat = Repeat switch { RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off };
        StateChanged?.Invoke();
    }

    public void AddToQueue(Track t)
    {
        if (_items.Count == 0) { PlayOne(t); return; }
        _a.AutoFix.Consider(t);
        _items.Add(t);
        _order.Add(_items.Count - 1);
        QueueChanged?.Invoke();
    }

    public void PlayNext(Track t)
    {
        if (_items.Count == 0) { PlayOne(t); return; }
        _a.AutoFix.Consider(t);
        _items.Add(t);
        _order.Insert(Math.Min(_pos + 1, _order.Count), _items.Count - 1);
        QueueChanged?.Invoke();
    }

    public void AddRange(IEnumerable<Track> l)
    {
        foreach (var t in l) { _a.AutoFix.Consider(t); _items.Add(t); _order.Add(_items.Count - 1); }
        QueueChanged?.Invoke();
    }

    public void RemoveFromQueue(int orderIndex)
    {
        if (orderIndex <= _pos || orderIndex >= _order.Count) return;
        _order.RemoveAt(orderIndex);
        QueueChanged?.Invoke();
    }

    public void JumpTo(int orderIndex)
    {
        if (orderIndex < 0 || orderIndex >= _order.Count) return;
        if (orderIndex != _pos) NoteSkip();
        _pos = orderIndex;
        QueueChanged?.Invoke();
        _ = StartCurrentAsync();
    }

    public void ClearQueue()
    {
        Stop();
        _items = new(); _order = new(); _pos = -1;
        QueueChanged?.Invoke(); StateChanged?.Invoke();
    }

    // ------------------------------------------------------------------ comandi

    public void TogglePlay()
    {
        if (_out == null) { if (Current != null) _ = StartCurrentAsync(); return; }
        if (Playing) { _out.Pause(); Playing = false; _timer.Stop(); }
        else { _out.Play(); Playing = true; _timer.Start(); }
        StateChanged?.Invoke();
    }

    /// <summary>Un salto manuale entro 30 secondi dall'inizio del brano pesa -0,5 sui gusti (se il brano finisce da solo non conta).</summary>
    private void NoteSkip()
    {
        if (_reader == null || _pos < 0 || _pos >= _order.Count) return;
        if (Position.TotalSeconds is >= 0 and < 30 && Current is Track t) _a.Store.RecordSkip(t);
    }

    public void Next(bool auto = false)
    {
        if (_order.Count == 0) return;
        if (!auto) NoteSkip();
        if (auto && SleepAtEnd) { SleepAtEnd = false; Stop(); StateChanged?.Invoke(); return; }
        if (auto && Repeat == RepeatMode.One) { _ = StartCurrentAsync(); return; }
        if (_pos + 1 < _order.Count) _pos++;
        else if (Repeat == RepeatMode.All) _pos = 0;
        else { Stop(); StateChanged?.Invoke(); return; }
        QueueChanged?.Invoke();
        _ = StartCurrentAsync();
    }

    public void Previous()
    {
        if (_order.Count == 0) return;
        if (Position.TotalSeconds > 3 || _pos <= 0) { Seek(TimeSpan.Zero); return; }
        NoteSkip();
        _pos--;
        QueueChanged?.Invoke();
        _ = StartCurrentAsync();
    }

    public void Seek(TimeSpan t)
    {
        if (_reader == null) return;
        try
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t > _reader.TotalTime) t = _reader.TotalTime;
            _reader.CurrentTime = t;
            _chain.Reset();
            PositionChanged?.Invoke();
        }
        catch (Exception) { }
    }

    public void Stop()
    {
        _userStop = true;
        Teardown();
        Playing = false;
        _timer.Stop();
    }

    // ------------------------------------------------------------------ riproduzione

    private void Teardown()
    {
        _loadCts?.Cancel();
        try { _out?.Stop(); } catch (Exception) { }
        try { _out?.Dispose(); } catch (Exception) { }
        try { _reader?.Dispose(); } catch (Exception) { }
        _out = null; _reader = null; _vol = null; _dsp = null;
    }

    private async Task StartCurrentAsync()
    {
        var t = Current;
        if (t == null) return;
        var token = Interlocked.Increment(ref _loadToken);
        Teardown();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        _userStop = false; _counted = false; _playedSec = 0;
        Loading = true; Playing = false; QualityText = "";
        StateChanged?.Invoke();
        _a.Store.AddHistory(t);
        try
        {
            string path; int kbps = 0;
            var local = _a.Downloads.FileOf(t.VideoId);
            if (local != null) path = local;
            else
            {
                var r = await _a.Resolver.ResolveAsync(_a.Overrides.Target(t.VideoId), null, ct).ConfigureAwait(false);
                path = r.FilePath; kbps = r.BitrateKbps;
            }
            if (token != Volatile.Read(ref _loadToken)) return;
            await Task.Run(() => OpenAndPlay(path, kbps, token), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (token != Volatile.Read(ref _loadToken)) return;
            Diag.Log($"Non riesco a suonare {t.VideoId} «{t.Title}»: {e}");
            _a.RunUi(() =>
            {
                Loading = false; Playing = false;
                Error?.Invoke($"Non riesco a suonare «{t.Title}»: {Short(e)}");
                StateChanged?.Invoke();
                // se un brano non parte salto al successivo
                if (_pos + 1 < _order.Count) Next();
            });
        }
    }

    private static string Short(Exception e) => e.Message.Length > 120 ? e.Message[..120] : e.Message;

    private void OpenAndPlay(string path, int kbps, long token)
    {
        MediaFoundationApi.Startup();
        var reader = new MediaFoundationReader(path);
        ISampleProvider sp = reader.ToSampleProvider();
        if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
        else if (sp.WaveFormat.Channels > 2) sp = new MultiplexingSampleProvider(new[] { sp }, 2);
        _chain.Configure(sp.WaveFormat.SampleRate, sp.WaveFormat.Channels, _a.Dsp);
        var dsp = new DspProvider(sp, _chain);
        var vol = new VolumeSampleProvider(dsp) { Volume = (float)_a.Settings.Volume };
        IWavePlayer output = CreateOutput();
        try { output.Init(vol); }
        catch (Exception) when (output is WasapiOut)
        {
            output.Dispose();
            output = new WasapiOut(AudioClientShareMode.Shared, 100);
            output.Init(vol);
        }
        if (token != Volatile.Read(ref _loadToken)) { output.Dispose(); reader.Dispose(); return; }
        output.PlaybackStopped += (_, e) => OnStopped(reader, e);
        var wf = reader.WaveFormat;
        var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        var fmt = ext is "M4A" or "MP4" ? "AAC" : ext is "WEBM" ? "Opus" : ext;
        var q = $"{fmt} · {(kbps > 0 ? kbps + " kbps · " : "")}{wf.SampleRate / 1000.0:0.#} kHz";
        _a.RunUi(() =>
        {
            if (token != Volatile.Read(ref _loadToken)) { output.Dispose(); reader.Dispose(); return; }
            _reader = reader; _vol = vol; _dsp = dsp; _out = output;
            QualityText = q;
            output.Play();
            Playing = true; Loading = false;
            _timer.Start();
            StateChanged?.Invoke();
            PrefetchNext();
            MaybeExtendRadio();
        });
    }

    private IWavePlayer CreateOutput()
    {
        if (_a.Settings.ExclusiveOutput) return new WasapiOut(AudioClientShareMode.Exclusive, 100);
        return new WasapiOut(AudioClientShareMode.Shared, 100);
    }

    private void OnStopped(MediaFoundationReader reader, StoppedEventArgs e)
    {
        if (_userStop) return;
        // se il flusso è finito davvero passo al prossimo brano
        _a.RunUi(() =>
        {
            if (_userStop || reader != _reader) return;
            if (e.Exception != null) Error?.Invoke("Errore di riproduzione: " + Short(e.Exception));
            Next(auto: true);
        });
    }

    private string? _radioFor;

    /// <summary>Radio infinita: quando mancano 2 brani alla fine, accoda quelli che YouTube Music propone dopo l'ultimo.</summary>
    private async void MaybeExtendRadio()
    {
        if (!_a.Settings.InfiniteRadio || Shuffle || Repeat != RepeatMode.Off) return;
        if (Current is not Track t || _order.Count - _pos > 2 || _radioFor == t.VideoId) return;
        _radioFor = t.VideoId;
        try
        {
            var list = await YtApi.RadioAsync(t.VideoId).ConfigureAwait(false);
            _a.RunUi(() =>
            {
                var st = _a.Store;
                var bad = Taste.BuildProfile(st.Plays, st.History, st.Favorites, st.Skips, st.Dislikes).Bad;
                var have = _items.Select(i => i.VideoId).ToHashSet();
                var more = list.Where(x => !have.Contains(x.VideoId) && !bad(x)).Take(25).ToList();
                if (more.Count > 0) AddRange(more);
            });
        }
        catch (Exception) { _radioFor = null; }
    }

    // ------------------------------------------------------------------ timer di spegnimento

    private DateTime? _sleepAt;
    /// <summary>Fine del timer di spegnimento (null = spento).</summary>
    public DateTime? SleepAt => _sleepAt;
    /// <summary>Si ferma alla fine del brano in corso.</summary>
    public bool SleepAtEnd { get; private set; }

    public void SetSleep(int? minutes, bool atEnd = false)
    {
        _sleepAt = minutes is > 0 ? DateTime.Now.AddMinutes(minutes.Value) : null;
        SleepAtEnd = atEnd;
        StateChanged?.Invoke();
    }

    private void PrefetchNext()
    {
        if (_pos + 1 >= _order.Count) return;
        var next = _a.Resolve(_items[_order[_pos + 1]]);
        if (_a.Downloads.IsDownloaded(next.VideoId)) return;
        _ = Task.Run(async () =>
        {
            try { await _a.Resolver.ResolveAsync(_a.Overrides.Target(next.VideoId)).ConfigureAwait(false); } catch (Exception) { }
        });
    }

    private void Tick()
    {
        if (_sleepAt is DateTime due && DateTime.Now >= due)
        {
            _sleepAt = null;
            if (Playing) TogglePlay();
            StateChanged?.Invoke();
        }
        if (_reader == null) return;
        if (Playing)
        {
            _playedSec += 0.25;
            if (!_counted && _playedSec >= 30 && Current is Track t)
            {
                _counted = true;
                _a.Store.RecordPlay(t);
            }
        }
        PositionChanged?.Invoke();
    }

    /// <summary>Riapplica le impostazioni DSP al brano in corso.</summary>
    public void ApplyDsp() => _chain.Update(_a.Dsp);

    public void Dispose() { Stop(); }
}
