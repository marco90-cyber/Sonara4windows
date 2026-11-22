using Microsoft.UI.Xaml;
using Sonara.App.Services;
using Sonara.App.Views;
using Sonara.Core;
using Sonara.Core.Yt;

namespace Sonara.App.Ui;

/// <summary>Azioni comuni a più pagine.</summary>
public static class Actions
{
    public static void OpenEntry(Entry e) => Nav.Go("entry:" + e.BrowseId, () => new DetailView(e));

    public static async void OpenArtistByName(string name)
    {
        try
        {
            var first = Taste.ArtistNames(name).FirstOrDefault() ?? name;
            var r = await YtApi.SearchEntriesAsync(first, Kind.Artists, 1);
            var hit = r.FirstOrDefault(e => Taste.Norm(e.Title) == Taste.Norm(first)) ?? r.FirstOrDefault();
            if (hit != null) OpenEntry(hit);
        }
        catch (Exception) { }
    }

    public static void OpenCard(Card c)
    {
        if (c.Song != null) AppServices.I.Player.PlayOne(AppServices.I.Resolve(c.Song));
        else if (c.Entry != null) OpenEntry(c.Entry);
    }

    /// <summary>Scarica nella cartella scelta; se non c'è ancora la fa scegliere.</summary>
    public static async void Download(string group, IEnumerable<Track> tracks, XamlRoot? root)
    {
        var a = AppServices.I;
        if (!a.Downloads.HasFolder)
        {
            var ok = await FolderPick.PickDownloadFolderAsync();
            if (!ok) return;
        }
        var list = tracks.ToList();
        _ = Task.Run(() => a.Downloads.DownloadAsync(group, list));
    }

    public static async Task<bool> SaveToggle(Entry e) => await Task.FromResult(AppServices.I.Store.ToggleSaved(e));
}
