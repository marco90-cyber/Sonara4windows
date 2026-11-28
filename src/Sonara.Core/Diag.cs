namespace Sonara.Core;

/// <summary>Piccolo registro di bordo (log.txt nella cartella dati): serve a capire perché un brano non parte.</summary>
public static class Diag
{
    private static readonly object Gate = new();
    public static string? Path { get; set; }

    public static void Log(string msg)
    {
        var p = Path;
        if (p == null) return;
        try
        {
            lock (Gate)
            {
                if (File.Exists(p) && new FileInfo(p).Length > 400_000) File.WriteAllText(p, "");
                File.AppendAllText(p, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}");
            }
        }
        catch (Exception) { }
    }
}
