using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sonara.Core.Data;

/// <summary>Cartelle e opzioni JSON comuni; i file stanno in %AppData%\Sonara.</summary>
public static class Persist
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static string? _root;

    /// <summary>Cartella dei dati dell'app (si può cambiare, ad esempio nei test).</summary>
    public static string Root
    {
        get
        {
            if (_root != null) return _root;
            _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sonara");
            Directory.CreateDirectory(_root);
            return _root;
        }
        set { _root = value; Directory.CreateDirectory(value); }
    }

    public static string CacheDir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sonara", "cache");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>Scrive il file in modo sicuro (prima un file temporaneo, poi lo sostituisce): non resta mai a metà.</summary>
    public static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, true);
    }

    public static T? Read<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : default; }
        catch (Exception) { return default; }
    }
}
