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
    public bool ImportAutoRefresh { get; set; }
    public int ImportRefreshHours { get; set; } = 24;
    public bool ImportNotify { get; set; } = true;
    public string DownloadFolder { get; set; } = "";
    public bool AutoFix { get; set; }
    public bool LightTheme { get; set; }
    public bool ExclusiveOutput { get; set; }
    public bool Normalize { get; set; }
    public double Volume { get; set; } = 0.8;
    /// <summary>Dissolvenza tra i brani, in millisecondi (0 = spenta).</summary>
    public int CrossfadeMs { get; set; }

    private static string PathOf => Path.Combine(Persist.Root, "settings.json");
    private static readonly object Gate = new();

    public event Action? Changed;

    public static AppSettings Load() => Persist.Read<AppSettings>(PathOf) ?? new AppSettings();

    public void Save()
    {
        lock (Gate) Persist.WriteAtomic(PathOf, JsonSerializer.Serialize(this, Persist.Options));
        Changed?.Invoke();
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
