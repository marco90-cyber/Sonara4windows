using System.Text.Json.Nodes;

namespace Sonara.Core;

/// <summary>Piccoli aiuti per leggere le risposte JSON di YouTube Music (strutture annidate e instabili).</summary>
public static class J
{
    /// <summary>Tutti gli oggetti sotto una chiave con quel nome, in qualunque punto (senza scendere dentro i trovati).</summary>
    public static List<JsonObject> Collect(JsonNode? root, string name)
    {
        var res = new List<JsonObject>();
        void Walk(JsonNode? n)
        {
            if (n is JsonObject o)
            {
                foreach (var kv in o)
                {
                    if (kv.Key == name && kv.Value is JsonObject f) res.Add(f);
                    else Walk(kv.Value);
                }
            }
            else if (n is JsonArray a)
            {
                foreach (var x in a) Walk(x);
            }
        }
        Walk(root);
        return res;
    }

    public static string Str(JsonNode? n)
    {
        if (n is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        return "";
    }

    /// <summary>Il testo di un oggetto "runs" (pezzi di testo concatenati).</summary>
    public static string Runs(JsonNode? o)
    {
        if (o?["runs"] is not JsonArray a) return "";
        return string.Concat(a.Select(r => Str(r?["text"])));
    }

    public static JsonArray? Arr(JsonNode? n) => n as JsonArray;

    public static JsonNode? Parse(string text) => JsonNode.Parse(text);
}
