using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;

namespace Sonara.App.Views;

/// <summary>Il pannello a destra: copertina, controlli, testo e prossimi brani.</summary>
public sealed class NowPlayingPanel : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private readonly Border _cover = new() { CornerRadius = new CornerRadius(14), Background = Pal.Card, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _title = K.T("Niente in riproduzione", 20, Pal.Text, true);
    private readonly TextBlock _artist = K.T("", 13, Pal.Sub);
    private readonly TextBlock _quality = K.T("", 11, Pal.Sub);
    private readonly FontIcon _up, _down;
    private readonly StackPanel _lyrics = new() { Spacing = 6 };
    private readonly StackPanel _next = new() { Spacing = 2 };
    private string _coverUrl = "";
    private string _lyricVideo = "";
    private int _lyricIdx = -3;

    public NowPlayingPanel()
    {
        var root = new StackPanel { Padding = new Thickness(16, 8, 16, 16), Spacing = 10 };
        _cover.SizeChanged += (_, e) => _cover.Height = e.NewSize.Width;
        root.Children.Add(_cover);

        var tg = new Grid { ColumnSpacing = 4 };
        tg.ColumnDefinitions.Add(new ColumnDefinition());
        tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        tt.Children.Add(_title); tt.Children.Add(_artist);
        tg.Children.Add(tt);
        var upB = K.IconBtn(K.ThumbUp, () => { if (_a.Player.Current is Track t) _a.Store.ToggleFavorite(t); }, 17, "Mi piace");
        var downB = K.IconBtn(K.ThumbDown, () => { if (_a.Player.Current is Track t) _a.Store.ToggleDislike(t); }, 17, "Non mi piace");
        _up = (FontIcon)upB.Child; _down = (FontIcon)downB.Child;
        Grid.SetColumn(upB, 1); Grid.SetColumn(downB, 2);
        tg.Children.Add(upB); tg.Children.Add(downB);
        root.Children.Add(tg);
        root.Children.Add(_quality);
        root.Children.Add(new SeekBar());
        root.Children.Add(new TransportControls(56));

        var lyCard = new Border { Background = Pal.Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Margin = new Thickness(0, 8, 0, 0) };
        var lyStack = new StackPanel { Spacing = 8 };
        lyStack.Children.Add(K.T("Testo", 14, Pal.Text, true));
        lyStack.Children.Add(_lyrics);
        var more = K.Clickable(K.T("Mostra tutto  ›", 12, Pal.Sub), () => Nav.Go("lyrics", () => new LyricsView()), pad: new Thickness(4));
        more.HorizontalAlignment = HorizontalAlignment.Right;
        lyStack.Children.Add(more);
        lyCard.Child = lyStack;
        root.Children.Add(lyCard);

        root.Children.Add(K.T("Prossimi brani", 14, Pal.Text, true));
        root.Children.Add(_next);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var p = _a.Player;
        void S() { UpdateTrack(); UpdateNext(); }
        void Q() => UpdateNext();
        void L() { _lyricVideo = ""; UpdateLyrics(); }
        void P() => UpdateLyrics();
        void St() => UpdateFlags();
        p.StateChanged += S; p.QueueChanged += Q; p.PositionChanged += P; _a.Lyrics.Changed += L; _a.Store.Changed += St;
        Unloaded += (_, _) => { p.StateChanged -= S; p.QueueChanged -= Q; p.PositionChanged -= P; _a.Lyrics.Changed -= L; _a.Store.Changed -= St; };
        UpdateTrack(); UpdateNext(); UpdateLyrics();
    }

    private void UpdateFlags()
    {
        var t = _a.Player.Current;
        var fav = t != null && _a.Store.IsFavorite(t.VideoId);
        var dis = t != null && _a.Store.IsDisliked(t.VideoId);
        _up.Foreground = fav ? Pal.Accent : Pal.Text;
        _down.Foreground = dis ? Pal.Red : Pal.Text;
    }

    private void UpdateTrack()
    {
        var t = _a.Player.Current;
        _title.Text = t?.Title ?? "Niente in riproduzione";
        _artist.Text = t?.Artist ?? "";
        _quality.Text = _a.Player.Loading ? "Caricamento…" : _a.Player.QualityText;
        var url = t?.Cover ?? "";
        if (url != _coverUrl) { _coverUrl = url; K.SetCover(_cover, url, 600); }
        UpdateFlags();
    }

    private void UpdateNext()
    {
        _next.Children.Clear();
        var up = _a.Player.Upcoming.Take(5).ToList();
        if (up.Count == 0) { _next.Children.Add(K.T("La coda è vuota", 12, Pal.Sub)); return; }
        var first = _a.Player.Index + 1;   // posizione nell'ordine di riproduzione (non per VideoId: i doppioni confondono)
        var n = 0;
        foreach (var t in up)
        {
            var idx = first + n++;
            var row = new Grid { ColumnSpacing = 10, Height = 50 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(K.Cover(t.Cover, 40, 6));
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            tx.Children.Add(K.T(t.Title, 13, Pal.Text, true)); tx.Children.Add(K.T(t.Artist, 11, Pal.Sub));
            Grid.SetColumn(tx, 1); row.Children.Add(tx);
            _next.Children.Add(K.Clickable(row, () => _a.Player.JumpTo(idx), radius: 8, pad: new Thickness(4, 0, 4, 0)));
        }
    }

    private void UpdateLyrics()
    {
        var t = _a.Player.Current;
        var ly = _a.Lyrics.Current;
        var idx = ly != null && ly.Synced ? ly.IndexAt((long)_a.Player.Position.TotalMilliseconds) : -2;
        var vid = t?.VideoId ?? "";
        if (vid == _lyricVideo && idx == _lyricIdx) return;
        _lyricVideo = vid; _lyricIdx = idx;
        _lyrics.Children.Clear();
        if (t == null) { _lyrics.Children.Add(K.T("—", 12, Pal.Sub)); return; }
        if (_a.Lyrics.Loading) { _lyrics.Children.Add(K.T("Cerco il testo…", 12, Pal.Sub)); return; }
        if (ly == null) { _lyrics.Children.Add(K.T("Testo non trovato", 12, Pal.Sub)); return; }
        var from = ly.Synced ? Math.Max(0, idx - 1) : 0;
        var to = Math.Min(ly.Lines.Count, from + (ly.Synced ? 6 : 8));
        for (var i = from; i < to; i++)
        {
            var cur = ly.Synced && i == idx;
            _lyrics.Children.Add(K.T(ly.Lines[i].Text.Length == 0 ? "♪" : ly.Lines[i].Text, 13, cur ? Pal.Accent : Pal.Sub, cur, 2));
        }
    }
}
