using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;

namespace Sonara.App.Views;

/// <summary>La barra in basso: brano in corso, controlli, avanzamento, volume e scorciatoie.</summary>
public sealed class PlayerBar : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private readonly Border _cover = new() { Width = 56, Height = 56, CornerRadius = new CornerRadius(8), Background = Pal.Card };
    private readonly TextBlock _title = K.T("", 14, Pal.Text, true), _artist = K.T("", 12, Pal.Sub);
    private readonly FontIcon _heart, _vol;
    private readonly Slider _volume;
    private readonly Border _meter = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = Pal.Accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly FontIcon _sleep;
    private string _coverUrl = "";

    /// <summary>Timer di spegnimento: ferma la musica dopo un tempo scelto o alla fine del brano in corso.</summary>
    private void ShowSleep(FrameworkElement anchor)
    {
        var p = _a.Player;
        var sp = new StackPanel { Spacing = 2, MinWidth = 220 };
        var fl = new Flyout { Content = sp };
        if (p.SleepAt != null || p.SleepAtEnd)
            sp.Children.Add(K.T(p.SleepAtEnd ? "Si ferma alla fine di questo brano" : $"Si ferma tra circa {Math.Max(1, (int)Math.Ceiling((p.SleepAt!.Value - DateTime.Now).TotalMinutes))} min", 12, Pal.Accent, true));
        foreach (var (label, mins, end) in new[] { ("15 minuti", 15, false), ("30 minuti", 30, false), ("45 minuti", 45, false), ("1 ora", 60, false), ("Fine del brano", 0, true) })
        {
            var m = mins; var e = end;
            sp.Children.Add(K.Clickable(K.T(label, 14, Pal.Text), () => { p.SetSleep(m > 0 ? m : null, e); fl.Hide(); }, pad: new Thickness(10, 9, 10, 9)));
        }
        if (p.SleepAt != null || p.SleepAtEnd)
            sp.Children.Add(K.Clickable(K.T("Annulla il timer", 14, Pal.Red, true), () => { p.SetSleep(null); fl.Hide(); }, pad: new Thickness(10, 9, 10, 9)));
        fl.ShowAt(anchor);
    }

    public PlayerBar()
    {
        var g = new Grid { Padding = new Thickness(16, 0, 16, 0), Background = Pal.Panel, BorderBrush = Pal.Line, BorderThickness = new Thickness(0, 1, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star), MinWidth = 320 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });

        var left = new Grid { ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center };
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        left.ColumnDefinitions.Add(new ColumnDefinition());
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        left.Children.Add(_cover);
        var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        tx.Children.Add(_title); tx.Children.Add(_artist);
        Grid.SetColumn(tx, 1); left.Children.Add(tx);
        var heartB = K.IconBtn(K.Heart, () => { if (_a.Player.Current is Track t) _a.Store.ToggleFavorite(t); }, 16, "Preferito");
        _heart = (FontIcon)heartB.Child;
        Grid.SetColumn(heartB, 2); left.Children.Add(heartB);
        g.Children.Add(left);

        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Margin = new Thickness(12, 0, 12, 0) };
        center.Children.Add(new TransportControls(40));
        center.Children.Add(new SeekBar());
        Grid.SetColumn(center, 1); g.Children.Add(center);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(K.IconBtn(K.Eq, () => Nav.Go("eq", () => new EqView()), 16, "Equalizzatore"));
        right.Children.Add(K.IconBtn(K.Lyrics, () => Nav.Go("lyrics", () => new LyricsView()), 16, "Testo"));
        Border? sleepB = null;
        sleepB = K.IconBtn(K.Timer, () => ShowSleep(sleepB!), 16, "Timer di spegnimento");
        _sleep = (FontIcon)sleepB.Child;
        right.Children.Add(sleepB);
        right.Children.Add(K.IconBtn(K.Queue, () => Nav.Go("queue", () => new QueueView()), 16, "Coda"));
        var volB = K.IconBtn(K.Volume, () => { var p = _a.Player; p.Volume = p.Volume > 0 ? 0 : 0.8; _volume!.Value = p.Volume * 100; }, 16, "Volume");
        _vol = (FontIcon)volB.Child;
        right.Children.Add(volB);
        _volume = new Slider { Minimum = 0, Maximum = 100, Value = _a.Settings.Volume * 100, Width = 110, Foreground = Pal.Accent, VerticalAlignment = VerticalAlignment.Center, IsThumbToolTipEnabled = false };
        _volume.ValueChanged += (_, e) => { _a.Player.Volume = e.NewValue / 100; _vol.Glyph = e.NewValue <= 0 ? K.Mute : K.Volume; };
        right.Children.Add(_volume);
        Grid.SetColumn(right, 2); g.Children.Add(right);
        Content = g;

        var p = _a.Player;
        void S() => Update();
        p.StateChanged += S; _a.Store.Changed += S;
        Unloaded += (_, _) => { p.StateChanged -= S; _a.Store.Changed -= S; };
        Update();
    }

    private void Update()
    {
        var t = _a.Player.Current;
        _title.Text = t?.Title ?? "Sonara";
        _artist.Text = t?.Artist ?? "Cerca un brano per iniziare";
        var url = t?.Cover ?? "";
        if (url != _coverUrl) { _coverUrl = url; K.SetCover(_cover, url, 160); }
        var fav = t != null && _a.Store.IsFavorite(t.VideoId);
        _sleep.Foreground = _a.Player.SleepAt != null || _a.Player.SleepAtEnd ? Pal.Accent : Pal.Text;
        _heart.Glyph = fav ? K.HeartFill : K.Heart;
        _heart.Foreground = fav ? Pal.Accent : Pal.Text;
    }
}
