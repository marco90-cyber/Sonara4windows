using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.Core;
using Sonara.Core.Data;
using Sonara.Core.Yt;

namespace Sonara.App.Ui;

/// <summary>Scelta di generi e artisti preferiti, uguale in Benvenuto e Impostazioni.</summary>
public static class Pickers
{
    /// <summary>Le «pastiglie» dei generi (a righe); ogni tocco cambia la scelta e la salva.</summary>
    public static UIElement Genres(AppSettings s, bool save, int perRow = 4)
    {
        var wrap = new StackPanel { Spacing = 8 };
        void Build()
        {
            wrap.Children.Clear();
            var all = FavGenres.All;
            for (var i = 0; i < all.Count; i += perRow)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var g in all.Skip(i).Take(perRow))
                {
                    var id = g.Id;
                    row.Children.Add(K.Chip(g.Label, FavGenres.Parse(s.FavGenres).Any(x => x.Id == id), () =>
                    {
                        s.FavGenres = FavGenres.Toggle(s.FavGenres, id);
                        if (save) s.Save();
                        Build();
                    }));
                }
                wrap.Children.Add(row);
            }
        }
        Build();
        return wrap;
    }

    /// <summary>Ricerca e scelta degli artisti preferiti; quelli scelti finiscono anche tra i salvati della Raccolta.</summary>
    public static UIElement Artists(AppSettings s, bool save)
    {
        var root = new StackPanel { Spacing = 6 };
        var chips = new StackPanel { Spacing = 6 };
        var res = new StackPanel { Spacing = 4 };
        void Rebuild()
        {
            chips.Children.Clear();
            foreach (var e in s.FavArtists.ToList())
            {
                var ee = e;
                chips.Children.Add(K.Chip("✓ " + ee.Title, true, () => { s.FavArtists.RemoveAll(x => x.BrowseId == ee.BrowseId); if (save) s.Save(); Rebuild(); }));
            }
        }
        var box = new AutoSuggestBox { PlaceholderText = "Cerca un artista da aggiungere", Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
        box.QuerySubmitted += async (b, _) =>
        {
            res.Children.Clear();
            try
            {
                var l = await YtApi.SearchEntriesAsync(b.Text, Kind.Artists, 1);
                foreach (var a in l.Take(6))
                {
                    var aa = a;
                    res.Children.Add(K.Clickable(K.T(aa.Title, 13, Pal.Text), () =>
                    {
                        if (!s.FavArtists.Any(x => x.BrowseId == aa.BrowseId))
                        {
                            s.FavArtists.Add(aa);
                            AppServices.I.Store.AddSaved(aa);
                            if (save) s.Save();
                        }
                        res.Children.Clear(); Rebuild();
                    }, null, null, 8, new Thickness(8)));
                }
            }
            catch (Exception) { res.Children.Add(K.T("Ricerca non riuscita", 12, Pal.Sub)); }
        };
        Rebuild();
        root.Children.Add(box); root.Children.Add(res); root.Children.Add(chips);
        return root;
    }
}
