using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Sonara.Core.Yt;

/// <summary>Trova il flusso audio di un video e lo porta in un file nella cache (YoutubeExplode).</summary>
public sealed class StreamResolver
{
    private readonly YoutubeClient _yt = new();
    private readonly string _cacheDir;
    // un solo scaricamento alla volta per brano: lettore, precaricamento e testo con AI non si pestano i piedi sullo stesso file
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public StreamResolver(string cacheDir)
    {
        _cacheDir = cacheDir;
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Qualità: 0 = massima, 1 = alta (fino a 160 kbps), 2 = risparmio.</summary>
    public int Quality { get; set; }

    public sealed record Resolved(string FilePath, string Container, int BitrateKbps);

    /// <summary>Quale flusso scegliere tra quelli con questi bitrate (kbps), come in Sonara per Android.</summary>
    public static int PickIndex(IReadOnlyList<int> kbps, int quality)
    {
        if (kbps.Count == 0) return -1;
        var lightest = Enumerable.Range(0, kbps.Count).MinBy(i => kbps[i]);
        switch (quality)
        {
            case 2: return lightest;
            case 1:
                var ok = Enumerable.Range(0, kbps.Count).Where(i => kbps[i] <= 160).ToList();
                return ok.Count > 0 ? ok.MaxBy(i => kbps[i]) : lightest;
            default: return Enumerable.Range(0, kbps.Count).MaxBy(i => kbps[i]);
        }
    }

    private string PathFor(string videoId, string container) => Path.Combine(_cacheDir, $"{videoId}.{container}");

    public string? CachedFile(string videoId)
    {
        foreach (var ext in new[] { "mp4", "m4a", "webm" })
        {
            var p = PathFor(videoId, ext);
            if (File.Exists(p) && new FileInfo(p).Length > 0) return p;
        }
        return null;
    }

    /// <summary>Toglie dalla cache i file di un video (per riscaricarlo se era rimasto incompleto o rovinato).</summary>
    public void Invalidate(string videoId)
    {
        foreach (var ext in new[] { "mp4", "m4a", "webm", "m4a.part", "mp4.part", "webm.part" })
            try { File.Delete(PathFor(videoId, ext)); } catch (IOException) { }
    }

    /// <summary>Scarica (se serve) l'audio del video nella cache e ne dà il percorso. Preferisce l'AAC (m4a), che Windows legge di serie.</summary>
    public async Task<Resolved> ResolveAsync(string videoId, IProgress<double>? progress = null, CancellationToken ct = default, bool preferMp4 = true)
    {
        var cached = CachedFile(videoId);
        if (cached != null) return new Resolved(cached, Path.GetExtension(cached).TrimStart('.'), 0);

        var gate = _locks.GetOrAdd(videoId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // intanto un altro lo ha già scaricato?
            cached = CachedFile(videoId);
            if (cached != null) return new Resolved(cached, Path.GetExtension(cached).TrimStart('.'), 0);
            return await DownloadAsync(videoId, progress, ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    /// <summary>Se per tanto tempo non arriva nemmeno un byte consideriamo il download bloccato (YouTube a volte «strozza» la connessione).</summary>
    private static readonly TimeSpan Stall = TimeSpan.FromSeconds(20);

    private async Task<Resolved> DownloadAsync(string videoId, IProgress<double>? progress, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(ct);
            guard.CancelAfter(Stall);
            var tmp = "";
            try
            {
                var manifest = await _yt.Videos.Streams.GetManifestAsync(videoId, guard.Token).ConfigureAwait(false);
                var audio = manifest.GetAudioOnlyStreams().ToList();
                var mp4 = audio.Where(s => s.Container == Container.Mp4).ToList();
                var pool = mp4.Count > 0 ? mp4 : audio;
                // ai tentativi successivi si prova il flusso più leggero: si scarica prima e passa meglio
                var idx = PickIndex(pool.Select(s => (int)(s.Bitrate.BitsPerSecond / 1000)).ToList(), attempt == 0 ? Quality : 2);
                if (idx < 0) throw new IOException($"YouTube: nessun flusso audio per {videoId}");
                var info = pool[idx];

                var ext = info.Container == Container.Mp4 ? "m4a" : info.Container.Name;
                var path = PathFor(videoId, ext);
                tmp = path + ".part";
                try { File.Delete(tmp); } catch (IOException) { }   // avanzo di un tentativo interrotto
                guard.CancelAfter(Stall);
                var watch = new Progress<double>(f => { try { guard.CancelAfter(Stall); } catch (ObjectDisposedException) { } progress?.Report(f); });
                await _yt.Videos.Streams.DownloadAsync(info, tmp, watch, guard.Token).ConfigureAwait(false);
                File.Move(tmp, path, true);
                return new Resolved(path, ext, (int)(info.Bitrate.BitsPerSecond / 1000));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                last = new TimeoutException("Il download si è bloccato");
                Diag.Log($"Download {videoId}: bloccato (tentativo {attempt + 1})");
            }
            catch (Exception e) when (e is not OperationCanceledException && attempt < 2)
            {
                last = e;
                Diag.Log($"Download {videoId}: {e.GetType().Name}: {e.Message} (tentativo {attempt + 1})");
            }
            if (tmp.Length > 0) try { File.Delete(tmp); } catch (IOException) { }
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        throw last ?? new IOException("Download non riuscito");
    }

    /// <summary>Cancella i file della cache più vecchi quando supera <paramref name="maxBytes"/>.</summary>
    public void Trim(long maxBytes)
    {
        var files = new DirectoryInfo(_cacheDir).GetFiles().OrderBy(f => f.LastAccessTimeUtc).ToList();
        var total = files.Sum(f => f.Length);
        foreach (var f in files)
        {
            if (total <= maxBytes) break;
            try { total -= f.Length; f.Delete(); } catch (IOException) { }
        }
    }
}
