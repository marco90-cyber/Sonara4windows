namespace Sonara.Core.Yt;

public enum Kind { Songs, Artists, Albums, Playlists }

public static class KindInfo
{
    public static string Label(this Kind k) => k switch
    {
        Kind.Songs => "Brani",
        Kind.Artists => "Artisti",
        Kind.Albums => "Album",
        _ => "Playlist",
    };
}

/// <summary>Un risultato che non è un brano: artista, album o playlist (si apre per vederne i brani).</summary>
public sealed record Entry(Kind Kind, string BrowseId, string Title, string Subtitle, string Thumb);

/// <summary>Una scheda della home: un brano da suonare oppure un album/playlist/artista da aprire.</summary>
public sealed record Card(Track? Song, Entry? Entry)
{
    public string Id => Song?.VideoId ?? Entry?.BrowseId ?? "";
    public string Title => Song?.Title ?? Entry?.Title ?? "";
    public string Subtitle => Song?.Artist ?? Entry?.Subtitle ?? "";
    public string Thumb => Song?.Cover ?? YtApi.BigThumb(Entry?.Thumb ?? "");
}

public sealed record Shelf(string Title, List<Card> Cards);

public sealed record Detail(List<Track> Tracks, List<Shelf> Shelves);

public sealed record Chip(string Title, string Params);
