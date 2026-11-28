using Microsoft.UI.Dispatching;
using Sonara.Core;
using Sonara.Core.Audio;
using Sonara.Core.Data;
using Sonara.Core.Yt;

namespace Sonara.App.Services;

/// <summary>Tutto ciò che vive per l'intera durata dell'app.</summary>
public sealed class AppServices
{
    public static AppServices I { get; private set; } = null!;

    public AppSettings Settings { get; }
    public YtStore Store { get; }
    public ImportStore Imports { get; }
    public ImportRunner Runner { get; }
    public YtOverrides Overrides { get; }
    public AutoFix AutoFix { get; }
    public StreamResolver Resolver { get; }
    public DownloadManager Downloads { get; }
    public DspSettings Dsp { get; set; }
    public PlayerService Player { get; }
    public LyricsService Lyrics { get; }
    public LyricsAiService LyricsAi { get; }
    public DispatcherQueue UI { get; }

    public AppServices(DispatcherQueue ui)
    {
        UI = ui;
        Settings = AppSettings.Load();
        YtApi.Country = Settings.EffectiveCountry();
        Store = new YtStore();
        Imports = new ImportStore();
        Runner = new ImportRunner(Imports);
        Overrides = new YtOverrides();
        AutoFix = new AutoFix(Overrides) { Enabled = () => Settings.AutoFix };
        Resolver = new StreamResolver(Persist.CacheDir) { Quality = Settings.Quality };
        _ = Task.Run(() => { try { Resolver.Trim(Settings.CacheMaxMb * 1024L * 1024L); } catch { } });
        Downloads = new DownloadManager(Settings, Resolver);
        Dsp = DspSettings.Load();
        Player = new PlayerService(this);
        Lyrics = new LyricsService(this);
        LyricsAi = new LyricsAiService(this);
        I = this;

        // copia di riserva nella cartella dei download a ogni cambiamento
        void Backup() { try { Downloads.WriteBackup(Store, Imports); } catch (Exception) { } }
        Store.Changed += Backup;
        Imports.Changed += Backup;
    }

    public static void Init(DispatcherQueue ui) => new AppServices(ui);

    /// <summary>Il brano come va mostrato e suonato (con l'eventuale «Cambia sorgente»).</summary>
    public Track Resolve(Track t) => Overrides.Apply(t);

    public void RunUi(Action a)
    {
        if (UI.HasThreadAccess) a(); else UI.TryEnqueue(() => a());
    }
}
