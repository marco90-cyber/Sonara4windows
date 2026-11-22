namespace Sonara.Core;

/// <summary>Un brano (id del video, titolo, artista, durata in secondi, miniatura).</summary>
public sealed record Track(string VideoId, string Title, string Artist, long DurationSec, string Thumb)
{
    public string Cover => string.IsNullOrEmpty(Thumb) ? $"https://i.ytimg.com/vi/{VideoId}/mqdefault.jpg" : Yt.YtApi.BigThumb(Thumb);

    public string DurationText => DurationSec <= 0 ? "" : $"{DurationSec / 60}:{DurationSec % 60:00}";
}
