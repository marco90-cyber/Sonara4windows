using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sonara.Core.Data;

/// <summary>
/// Tutto in un solo file per cambiare computer: libreria e gusti, playlist importate, sorgenti cambiate e impostazioni.
/// I brani scaricati restano fuori: si ritrovano scegliendo la stessa cartella dei download. Porting di DataTransfer.kt.
/// </summary>
public static class DataTransfer
{
    public const string FileName = "sonara-backup.json";
    private const string Kind = "sonara-backup";

    public static string Export(YtStore store, ImportStore imports, YtOverrides overrides, AppSettings settings) =>
        new JsonObject
        {
            ["kind"] = Kind,
            ["version"] = 1,
            ["library"] = store.ExportJson(),
            ["imports"] = imports.ExportJson(),
            ["sources"] = JsonSerializer.Serialize(overrides.All, Persist.Options),
            ["settings"] = JsonNode.Parse(JsonSerializer.Serialize(settings, Persist.Options)),
        }.ToJsonString();

    /// <summary>Unisce il file a quanto c'è già. Restituisce il messaggio da mostrare, o null se il file non è un backup di Sonara.</summary>
    public static string? Import(YtStore store, ImportStore imports, YtOverrides overrides, AppSettings settings, string text)
    {
        JsonObject? o;
        try { o = JsonNode.Parse(text) as JsonObject; } catch (JsonException) { return null; }
        if (o == null || o["kind"]?.GetValue<string>() != Kind) return null;
        var n = 0;
        var lib = o["library"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(lib)) n += store.MergeFrom(lib);
        var imp = o["imports"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(imp)) n += imports.MergeFrom(imp);
        var src = o["sources"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(src)) n += overrides.MergeFrom(src);
        if (o["settings"] is JsonObject so) settings.ImportFrom(so.ToJsonString());
        return n > 0 ? $"Importati {n} elementi e le impostazioni" : "Impostazioni importate (il resto c'era già)";
    }
}
