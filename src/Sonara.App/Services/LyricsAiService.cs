using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using NAudio.MediaFoundation;
using NAudio.Wave;
using Sonara.Core;
using Sonara.Core.Data;
using Sonara.Core.Lyrics;

namespace Sonara.App.Services;

/// <summary>
/// Scrive il testo di un brano che non ce l'ha: ascolta l'audio con Whisper (offline, sul PC) e ricava le righe con i tempi delle parole.
/// Al primo uso l'app scarica il programma ufficiale whisper.cpp (MIT) e il modello scelto; l'installer resta leggero.
/// </summary>
public sealed class LyricsAiService
{
    public const string EngineVersion = "v1.8.7";
    private const string EngineUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/" + EngineVersion + "/whisper-bin-x64.zip";

    public sealed record Model(string Id, string Label, string File, int Mb, string Note)
    {
        public string Url => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + File;
    }

    public static readonly Model[] Models =
    {
        new("Base", "Base", "ggml-base-q5_1.bin", 60, "più veloce"),
        new("Small", "Small", "ggml-small-q5_1.bin", 190, "più preciso col canto"),
    };

    /// <summary>Lingue tra cui scegliere: codice Whisper → nome.</summary>
    public static readonly (string Code, string Name)[] Langs =
        { ("auto", "Automatica"), ("it", "Italiano"), ("en", "Inglese"), ("es", "Spagnolo"), ("fr", "Francese"), ("de", "Tedesco"), ("pt", "Portoghese") };

    public enum Phase { Idle, Working, Failed, Done }
    public sealed record State(Phase Phase, string TrackId, string Stage, int Percent, string Message);

    private static readonly HttpClient Http = MakeHttp();
    private static HttpClient MakeHttp()
    {
        var h = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Sonara/1.0 (+https://github.com/marco90-cyber/Sonara4windows)");
        return h;
    }

    private readonly AppServices _a;
    private CancellationTokenSource? _cts;

    public LyricsAiService(AppServices a) { _a = a; }

    public State Current { get; private set; } = new(Phase.Idle, "", "", 0, "");
    public string Title { get; private set; } = "";
    public bool Busy => Current.Phase == Phase.Working;
    public event Action? Changed;

    private void Set(Phase p, string id, string stage = "", int pct = 0, string msg = "")
    {
        Current = new State(p, id, stage, pct, msg);
        _a.RunUi(() => Changed?.Invoke());
    }

    public static string Dir { get { var d = Path.Combine(Persist.Root, "whisper"); Directory.CreateDirectory(d); return d; } }
    private static string BinDir => Path.Combine(Dir, "bin-" + EngineVersion);
    private static string Exe => Path.Combine(BinDir, "whisper-cli.exe");
    public static string ModelPath(Model m) => Path.Combine(Dir, m.File);
    public static bool HasModel(Model m) => File.Exists(ModelPath(m)) && new FileInfo(ModelPath(m)).Length > m.Mb * 400_000L;
    public static bool HasEngine => File.Exists(Exe);
    public static void DeleteModel(Model m) { try { File.Delete(ModelPath(m)); } catch (IOException) { } }
    public static Model ModelOf(string id) => Models.FirstOrDefault(m => m.Id == id) ?? Models[0];

    public void Cancel() { _cts?.Cancel(); }
    public void Dismiss() { if (!Busy) Set(Phase.Idle, ""); }

    public void Start(Track t, Model model, string lang)
    {
        if (Busy) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Title = t.Title;
        Set(Phase.Working, t.VideoId, "Preparo", 0);
        _ = Task.Run(async () =>
        {
            string? wav = null;
            try
            {
                if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException("Questa funzione richiede Windows a 64 bit");
                if (!HasEngine) await EnsureEngineAsync(t.VideoId, ct);
                if (!HasModel(model))
                {
                    await DownloadAsync(model.Url, ModelPath(model), p => Set(Phase.Working, t.VideoId, $"Scarico il modello {model.Label}", p), ct);
                }

                // audio del brano: scaricato oppure preso da YouTube (m4a, il più semplice da leggere)
                Set(Phase.Working, t.VideoId, "Scarico l'audio", 0);
                var src = _a.Downloads.FileOf(t.VideoId);
                if (src == null)
                {
                    var r = await _a.Resolver.ResolveAsync(_a.Overrides.Target(t.VideoId), new Progress<double>(f => Set(Phase.Working, t.VideoId, "Scarico l'audio", (int)(f * 100))), ct, true);
                    src = r.FilePath;
                }
                ct.ThrowIfCancellationRequested();
                var tmp = Path.Combine(Persist.CacheDir, "testo_ai"); Directory.CreateDirectory(tmp);
                wav = Path.Combine(tmp, "input.wav");
                Set(Phase.Working, t.VideoId, "Leggo l'audio", 0);
                DecodeToWav(src, wav, p => Set(Phase.Working, t.VideoId, "Leggo l'audio", p), ct);
                var seconds = new FileInfo(wav).Length / 32000.0;
                if (seconds < 1) throw new InvalidOperationException("Audio troppo corto");

                // con la lingua «automatica» la si riconosce una volta sola e poi la si fissa per tutto il brano:
                // lasciandola libera Whisper cambia lingua da una finestra all'altra e scrive traduzioni
                var use = lang;
                if (lang == "auto")
                {
                    Set(Phase.Working, t.VideoId, "Riconosco la lingua", 0);
                    use = await DetectLanguageAsync(model, wav, seconds, ct) ?? "auto";
                }

                Set(Phase.Working, t.VideoId, "Ascolto e scrivo il testo", 0);
                var outBase = Path.Combine(Path.GetDirectoryName(wav)!, "out");
                try { File.Delete(outBase + ".json"); } catch (IOException) { }
                var rx = new Regex(@"progress\s*=\s*(\d+)%");
                var (code, err) = await RunAsync(Exe, new[]
                {
                    "-m", ModelPath(model), "-f", wav, "-l", use, "-t", Threads().ToString(), "-ojf", "-of", outBase, "-pp", "-sns", "-mc", "0",
                }, line =>
                {
                    var m = rx.Match(line);
                    if (m.Success) Set(Phase.Working, t.VideoId, "Ascolto e scrivo il testo", int.Parse(m.Groups[1].Value));
                }, ct);
                if (code != 0 || !File.Exists(outBase + ".json"))
                {
                    if (code != 0 && err.Contains("failed to load model", StringComparison.OrdinalIgnoreCase)) DeleteModel(model);
                    throw new InvalidOperationException("Whisper non è riuscito" + (err.Contains("failed to load model") ? ": il modello non si carica, lo riscarico al prossimo tentativo" : ""));
                }
                var lines = AiLyrics.Clean(AiLyrics.ReadWhisperJson(await File.ReadAllTextAsync(outBase + ".json", ct)));
                if (lines.Count == 0) throw new InvalidOperationException("Non ho trovato parole cantate");
                await _a.Lyrics.SaveGeneratedAsync(t, AiLyrics.ToLrc(lines));
                Set(Phase.Done, t.VideoId);
            }
            catch (OperationCanceledException) { Set(Phase.Idle, ""); }
            catch (Exception e)
            {
                Set(Phase.Failed, t.VideoId, "", 0, e is HttpRequestException or IOException ? "Connessione interrotta, riprova" : e.Message);
            }
            finally { if (wav != null) try { File.Delete(wav); } catch (IOException) { } }
        });
    }

    private static int Threads() => Math.Clamp(Environment.ProcessorCount - 1, 2, 8);

    // ---------------------------------------------------------------- programma e modello

    private async Task EnsureEngineAsync(string id, CancellationToken ct)
    {
        var zip = Path.Combine(Dir, "whisper-bin-x64.zip");
        await DownloadAsync(EngineUrl, zip, p => Set(Phase.Working, id, "Scarico il programma Whisper (una volta sola)", p), ct);
        Set(Phase.Working, id, "Installo Whisper", 0);
        var tmp = Path.Combine(Dir, "unzip"); 
        try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (IOException) { }
        ZipFile.ExtractToDirectory(zip, tmp, true);
        var exe = Directory.EnumerateFiles(tmp, "whisper-cli.exe", SearchOption.AllDirectories).FirstOrDefault()
                  ?? throw new InvalidOperationException("Programma Whisper non trovato nel pacchetto scaricato");
        var from = Path.GetDirectoryName(exe)!;
        var to = BinDir + ".new";
        if (Directory.Exists(to)) Directory.Delete(to, true);
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        if (Directory.Exists(BinDir)) Directory.Delete(BinDir, true);
        Directory.Move(to, BinDir);
        try { Directory.Delete(tmp, true); File.Delete(zip); } catch (IOException) { }
    }

    /// <summary>Scarica con ripresa: se la connessione cade, riparte da dove era arrivato (fino a 6 tentativi).</summary>
    private static async Task DownloadAsync(string url, string dest, Action<int> onPct, CancellationToken ct)
    {
        var part = dest + ".part";
        var attempt = 0;
        while (true)
        {
            try
            {
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);
                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { File.Delete(part); continue; }
                resp.EnsureSuccessStatusCode();
                var resumed = resp.StatusCode == HttpStatusCode.PartialContent;
                var baseB = resumed ? have : 0;
                var total = resp.Content.Headers.ContentLength is long cl ? cl + baseB : -1;
                await using var inp = await resp.Content.ReadAsStreamAsync(ct);
                await using (var o = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write))
                {
                    var buf = new byte[64 * 1024];
                    var done = baseB; var last = -1;
                    while (true)
                    {
                        var n = await inp.ReadAsync(buf, ct);
                        if (n <= 0) break;
                        await o.WriteAsync(buf.AsMemory(0, n), ct);
                        done += n;
                        if (total > 0) { var pct = (int)(done * 100 / total); if (pct != last) { last = pct; onPct(pct); } }
                    }
                }
                break;
            }
            catch (Exception e) when ((e is HttpRequestException or IOException) && !ct.IsCancellationRequested)
            {
                if (++attempt >= 6) throw;
                await Task.Delay(2000 * attempt, ct);
            }
        }
        File.Move(part, dest, true);
    }

    // ---------------------------------------------------------------- audio e Whisper

    /// <summary>Audio del brano → WAV a 16 kHz mono 16 bit, il formato che vuole Whisper.</summary>
    private static void DecodeToWav(string src, string wav, Action<int> onPct, CancellationToken ct)
    {
        MediaFoundationApi.Startup();
        using var reader = new MediaFoundationReader(src);
        using var rs = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1)) { ResamplerQuality = 60 };
        using var w = new WaveFileWriter(wav, rs.WaveFormat);
        var buf = new byte[64000];
        var last = -1;
        int n;
        while ((n = rs.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            w.Write(buf, 0, n);
            var p = (int)(reader.Position * 100 / Math.Max(1, reader.Length));
            if (p != last) { last = p; onPct(Math.Clamp(p, 0, 100)); }
        }
    }

    /// <summary>Riconosce la lingua su tre punti del brano e sceglie la più frequente.</summary>
    private async Task<string?> DetectLanguageAsync(Model model, string wav, double seconds, CancellationToken ct)
    {
        var offsets = seconds <= 40 ? new[] { 0.0 } : new[] { 0.2, 0.45, 0.7 }.Select(f => Math.Min(seconds * f, seconds - 31)).ToArray();
        var votes = new List<(string, double)>();
        foreach (var o in offsets)
        {
            var (_, err) = await RunAsync(Exe, new[] { "-m", ModelPath(model), "-f", wav, "-t", Threads().ToString(), "-dl", "-ot", ((long)(Math.Max(0, o) * 1000)).ToString(), "-d", "30000" }, null, ct);
            if (AiLyrics.ParseDetected(err) is { } v) votes.Add(v);
        }
        return AiLyrics.Vote(votes);
    }

    private static async Task<(int Code, string Err)> RunAsync(string exe, IEnumerable<string> args, Action<string>? onErr, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!, StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data == null) return; lock (sb) sb.AppendLine(e.Data); onErr?.Invoke(e.Data); };
        p.OutputDataReceived += (_, _) => { };
        p.Start();
        p.BeginErrorReadLine(); p.BeginOutputReadLine();
        using var reg = ct.Register(() => { try { p.Kill(true); } catch (Exception) { } });
        await p.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        lock (sb) return (p.ExitCode, sb.ToString());
    }
}
