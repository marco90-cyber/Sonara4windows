using System.Text.Json;

namespace Sonara.Core.Data;

/// <summary>Impostazioni di Sonara per Windows (file settings.json).</summary>
public sealed class AppSettings
{
    public bool WelcomeDone { get; set; }
    /// <summary>Paese dei consigliati; vuoto = automatico (quello del sistema).</summary>
    public string Region { get; set; } = "";
    /// <summary>Qualità dello streaming: 0 massima, 1 alta, 2 risparmio.</summary>
    public int Quality { get; set; }
    public int CacheMaxMb { get; set; } = 1024;
    public string FavGenres { get; set; } = "";
    public List<Yt.Entry> FavArtists { get; set; } = new();
    /// <summary>Gli artisti scelti sono già stati messi anche in Raccolta › Salvati.</summary>
    public bool FavArtistsSynced { get; set; }
    public bool ImportAutoRefresh { get; set; }
    public int ImportRefreshHours { get; set; } = 24;
    public bool ImportNotify { get; set; } = true;
    public string DownloadFolder { get; set; } = "";
    public bool AutoFix { get; set; } = true;
    public int SettingsVersion { get; set; }
    public bool LightTheme { get; set; }
    public bool CheckUpdates { get; set; } = true;
    public bool ExclusiveOutput { get; set; }
    public bool Normalize { get; set; }
    /// <summary>Radio infinita: a fine coda accoda brani simili.</summary>
    public bool InfiniteRadio { get; set; } = true;
    /// <summary>Mostra «mi piace» e «non mi piace» sulla copertina del lettore.</summary>
    public bool CoverActions { get; set; } = true;
    /// <summary>Doppio tocco sulla copertina = mi piace.</summary>
    public bool DoubleTapLike { get; set; } = true;
    public double Volume { get; set; } = 0.8;
    /// <summary>Dissolvenza tra i brani, in millisecondi (0 = spenta).</summary>
    public int CrossfadeMs { get; set; }

    private static string PathOf => Path.Combine(Persist.Root, "settings.json");
    private static readonly object Gate = new();

    public event Action? Changed;

    public static AppSettings Load()
    {
        var s = Persist.Read<AppSettings>(PathOf) ?? new AppSettings();
        if (s.SettingsVersion < 1) { s.AutoFix = true; s.SettingsVersion = 1; }
        if (s.SettingsVersion < 2)
        {
            // prima i generi erano etichette separate da «|»: ora sono gli id di FavGenres separati da virgola
            s.FavGenres = Yt.FavGenres.MigrateLegacy(s.FavGenres);
            s.SettingsVersion = 2;
        }
        return s;
    }

    public void Save()
    {
        lock (Gate) Persist.WriteAtomic(PathOf, JsonSerializer.Serialize(this, Persist.Options));
        Changed?.Invoke();
    }

    /// <summary>Interruttori e scelte tornano ai valori di partenza; restano cartella dei download, paese, benvenuto e gusti scelti.</summary>
    public void ResetPreferences()
    {
        var d = new AppSettings
        {
            DownloadFolder = DownloadFolder, Region = Region, WelcomeDone = WelcomeDone, FavGenres = FavGenres, FavArtists = FavArtists,
            FavArtistsSynced = FavArtistsSynced, SettingsVersion = 2,
        };
        foreach (var p in typeof(AppSettings).GetProperties().Where(p => p.CanWrite && p.GetIndexParameters().Length == 0))
            p.SetValue(this, p.GetValue(d));
        Save();
    }

    /// <summary>Applica le impostazioni di un backup (testo JSON) a queste.</summary>
    public void ImportFrom(string json)
    {
        var o = JsonSerializer.Deserialize<AppSettings>(json, Persist.Options);
        if (o == null) return;
        foreach (var p in typeof(AppSettings).GetProperties().Where(p => p.CanWrite && p.GetIndexParameters().Length == 0))
            p.SetValue(this, p.GetValue(o));
        Save();
    }

    public string EffectiveCountry()
    {
        if (!string.IsNullOrEmpty(Region) && Countries.Codes.Contains(Region)) return Region;
        return Countries.Detect();
    }
}

public static class Countries
{
    public static readonly string[] Codes =
    {
        "IT","US","GB","IE","DE","AT","CH","FR","BE","NL","LU","ES","PT","SE","NO","DK","FI","IS",
        "PL","CZ","SK","HU","RO","BG","GR","HR","SI","RS","UA","TR","CY","MT","EE","LV","LT",
        "CA","MX","BR","AR","CL","CO","PE","UY","EC","CR","PA","DO",
        "AU","NZ","JP","KR","IN","ID","TH","VN","PH","MY","SG","TW","HK",
        "ZA","EG","MA","DZ","TN","NG","KE","SA","AE","IL","QA","KW","JO",
    };

    public static string Name(string code)
    {
        try { return new System.Globalization.RegionInfo(code).DisplayName; }
        catch (Exception) { return code; }
    }

    public static string EnglishName(string code)
    {
        try { return new System.Globalization.RegionInfo(code).EnglishName; }
        catch (Exception) { return code; }
    }

    /// <summary>Il paese del sistema se è tra quelli disponibili, altrimenti l'Italia.</summary>
    public static string Detect()
    {
        try
        {
            var c = new System.Globalization.RegionInfo(System.Globalization.CultureInfo.CurrentCulture.Name).TwoLetterISORegionName.ToUpperInvariant();
            return Codes.Contains(c) ? c : "IT";
        }
        catch (Exception) { return "IT"; }
    }
}
