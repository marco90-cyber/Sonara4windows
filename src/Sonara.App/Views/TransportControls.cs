using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Sonara.App.Services;
using Sonara.App.Ui;

namespace Sonara.App.Views;

/// <summary>Casuale, indietro, play/pausa, avanti, ripeti.</summary>
public sealed class TransportControls : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private readonly FontIcon _shuffle, _repeat, _play;
    private readonly Border _shuffleB, _repeatB;

    public TransportControls(double playSize = 52)
    {
        var p = _a.Player;
        _shuffleB = K.IconBtn(K.Shuffle, () => p.SetShuffle(!p.Shuffle), 16, "Casuale");
        _shuffle = (FontIcon)_shuffleB.Child;
        var prev = K.IconBtn(K.Prev, () => p.Previous(), 18, "Indietro");
        var next = K.IconBtn(K.Next, () => p.Next(), 18, "Avanti");
        _play = K.Icon(K.Play, playSize * 0.38, Pal.Text);
        var playB = new Border
        {
            Width = playSize, Height = playSize, CornerRadius = new CornerRadius(playSize / 2), BorderThickness = new Thickness(2),
            BorderBrush = Pal.Text, Background = Pal.Transparent, Child = _play,
        };
        playB.Tapped += (_, _) => p.TogglePlay();
        playB.PointerEntered += (_, _) => playB.Background = Pal.Hover;
        playB.PointerExited += (_, _) => playB.Background = Pal.Transparent;
        _repeatB = K.IconBtn(K.Repeat, () => p.CycleRepeat(), 16, "Ripeti");
        _repeat = (FontIcon)_repeatB.Child;
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, Children = { _shuffleB, prev, playB, next, _repeatB } };
        void Upd() => Update();
        p.StateChanged += Upd;
        Unloaded += (_, _) => p.StateChanged -= Upd;
        Update();
    }

    private void Update()
    {
        var p = _a.Player;
        _play.Glyph = p.Playing ? K.Pause : K.Play;
        _shuffle.Foreground = p.Shuffle ? Pal.Accent : Pal.Text;
        _repeat.Glyph = p.Repeat == RepeatMode.One ? K.RepeatOne : K.Repeat;
        _repeat.Foreground = p.Repeat == RepeatMode.Off ? Pal.Text : Pal.Accent;
    }
}

/// <summary>Barra di avanzamento con i tempi.</summary>
public sealed class SeekBar : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private readonly Slider _s = new() { Minimum = 0, Maximum = 1, Foreground = Pal.Accent, StepFrequency = 0.1, IsThumbToolTipEnabled = false };
    private readonly TextBlock _l = K.T("0:00", 11, Pal.Sub), _r = K.T("0:00", 11, Pal.Sub);
    private bool _setting;

    public SeekBar()
    {
        var g = new Grid { ColumnSpacing = 10 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(_l);
        Grid.SetColumn(_s, 1); g.Children.Add(_s);
        Grid.SetColumn(_r, 2); g.Children.Add(_r);
        Content = g;
        _s.ValueChanged += (_, e) =>
        {
            if (_setting) return;
            _a.Player.Seek(TimeSpan.FromSeconds(e.NewValue));
        };
        void Upd() => Update();
        _a.Player.PositionChanged += Upd;
        _a.Player.StateChanged += Upd;
        Unloaded += (_, _) => { _a.Player.PositionChanged -= Upd; _a.Player.StateChanged -= Upd; };
        Update();
    }

    private void Update()
    {
        var p = _a.Player;
        var dur = Math.Max(1, p.Duration.TotalSeconds);
        var pos = p.Position.TotalSeconds;
        _setting = true;
        _s.Maximum = dur;
        _s.Value = Math.Min(pos, dur);
        _setting = false;
        _l.Text = K.Dur(p.Position);
        _r.Text = p.Current == null ? "0:00" : K.Dur(p.Duration);
    }
}
