using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace Sonara.Core.Yt;

/// <summary>Trova il flusso audio di un video e lo porta in un file nella cache (YoutubeExplode).</summary>
public sealed class StreamResolver
{
    private readonly YoutubeClient _yt = new();
    private readonly string _cacheDir;

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

    /// <summary>Scarica (se serve) l'audio del video nella cache e ne dà il percorso. Preferisce l'AAC (m4a), che Windows legge di serie.</summary>
    public async Task<Resolved> ResolveAsync(string videoId, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var cached = CachedFile(videoId);
        if (cached != null) return new Resolved(cached, Path.GetExtension(cached).TrimStart('.'), 0);

        var manifest = await _yt.Videos.Streams.GetManifestAsync(videoId, ct).ConfigureAwait(false);
        var audio = manifest.GetAudioOnlyStreams().ToList();
        var mp4 = audio.Where(s => s.Container == Container.Mp4).ToList();
        var pool = mp4.Count > 0 ? mp4 : audio;
        var idx = PickIndex(pool.Select(s => (int)(s.Bitrate.BitsPerSecond / 1000)).ToList(), Quality);
        if (idx < 0) throw new IOException($"YouTube: nessun flusso audio per {videoId}");
        var info = pool[idx];

        var ext = info.Container == Container.Mp4 ? "m4a" : info.Container.Name;
        var path = PathFor(videoId, ext);
        var tmp = path + ".part";
        await _yt.Videos.Streams.DownloadAsync(info, tmp, progress, ct).ConfigureAwait(false);
        File.Move(tmp, path, true);
        return new Resolved(path, ext, (int)(info.Bitrate.BitsPerSecond / 1000));
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
