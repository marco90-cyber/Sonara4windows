using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Yt;

namespace Sonara.App.Views;

/// <summary>Scaffali orizzontali di schede (brani, album, playlist, artisti).</summary>
public static class Shelves
{
    public static UIElement Make(Shelf sh, string? more = null, Action? onMore = null) => Make(sh.Title, sh.Cards, more, onMore);

    public static UIElement Make(string title, IList<Card> cards, string? more = null, Action? onMore = null)
    {
        var songs = cards.Where(c => c.Song != null).Select(c => c.Song!).ToList();
        var tiles = cards.Select(c =>
            K.CardTile(c.Thumb, c.Title, c.Subtitle,
                () =>
                {
                    if (c.Song != null) AppServices.I.Player.PlayList(songs.Select(AppServices.I.Resolve).ToList(), songs.FindIndex(s => s.VideoId == c.Song.VideoId));
                    else if (c.Entry != null) Actions.OpenEntry(c.Entry);
                },
                150, c.Entry?.Kind == Kind.Artists));
        var sp = new StackPanel();
        sp.Children.Add(K.Header(title, more, onMore));
        sp.Children.Add(K.HRow(tiles));
        return sp;
    }

    public static UIElement ForEntries(string title, IEnumerable<Entry> entries, string? more = null, Action? onMore = null) =>
        Make(title, entries.Select(e => new Card(null, e)).ToList(), more, onMore);

    public static UIElement ForTracks(string title, IEnumerable<Track> tracks, string? more = null, Action? onMore = null) =>
        Make(title, tracks.Select(t => new Card(t, null)).ToList(), more, onMore);
}
