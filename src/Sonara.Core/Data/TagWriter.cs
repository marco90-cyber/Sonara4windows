namespace Sonara.Core.Data;

/// <summary>Scrive titolo, artista e copertina nei file scaricati (m4a/mp4/webm).</summary>
public static class TagWriter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task WriteAsync(string file, Track t, CancellationToken ct = default)
    {
        byte[]? cover = null;
        try
        {
            if (!string.IsNullOrEmpty(t.Cover)) cover = await Http.GetByteArrayAsync(t.Cover, ct).ConfigureAwait(false);
        }
        catch (Exception) { }
        try
        {
            using var f = TagLib.File.Create(file);
            f.Tag.Title = t.Title;
            f.Tag.Performers = new[] { t.Artist };
            f.Tag.AlbumArtists = new[] { t.Artist };
            if (cover is { Length: > 0 })
            {
                var mime = cover.Length > 3 && cover[0] == 0x89 ? "image/png" : "image/jpeg";
                f.Tag.Pictures = new TagLib.IPicture[] { new TagLib.Picture(new TagLib.ByteVector(cover)) { Type = TagLib.PictureType.FrontCover, MimeType = mime } };
            }
            f.Save();
        }
        catch (Exception) { /* i tag sono un di più: se il formato non li accetta il brano resta valido */ }
    }
}
