using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Lyrics;

namespace Sonara.App.Views;

/// <summary>Il testo del brano a tutta pagina; se sincronizzato evidenzia la riga e si scorre da solo.</summary>
public sealed class LyricsView : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private readonly StackPanel _lines = new() { Padding = new Thickness(40, 24, 40, 200), Spacing = 10 };
    private readonly ScrollViewer _sv = new();
    private readonly List<TextBlock> _tbs = new();
    private int _active = -2;

    public LyricsView()
    {
        _sv.Content = _lines;
        Content = _sv;
        void OnLy() => Rebuild();
        void OnPos() => Highlight();
        _a.Lyrics.Changed += OnLy;
        _a.Player.PositionChanged += OnPos;
        Unloaded += (_, _) => { _a.Lyrics.Changed -= OnLy; _a.Player.PositionChanged -= OnPos; };
        Rebuild();
    }

    private void Rebuild()
    {
        _lines.Children.Clear(); _tbs.Clear(); _active = -2;
        var t = _a.Player.Current;
        _lines.Children.Add(K.T(t?.Title ?? "Testo", 26, Pal.Text, true, 2));
        if (t != null) _lines.Children.Add(K.T(t.Artist, 14, Pal.Sub));
        var ly = _a.Lyrics.Current;
        if (t == null) { _lines.Children.Add(K.T("Nessun brano in riproduzione.", 14, Pal.Sub)); return; }
        if (_a.Lyrics.Loading) { _lines.Children.Add(K.Spinner()); return; }
        if (ly == null)
        {
            _lines.Children.Add(K.T("Testo non trovato per questo brano.", 14, Pal.Sub));
            _lines.Children.Add(K.Pill("Riprova", async () => await _a.Lyrics.ReloadAsync(), false, K.Refresh));
            return;
        }
        _lines.Children.Add(K.T($"Fonte: {ly.Source} · {ly.Kind switch { SyncKind.Words => "sync parole", SyncKind.Lines => "sync righe", _ => "solo testo" }}", 12, Pal.Sub));
        for (var i = 0; i < ly.Lines.Count; i++)
        {
            var l = ly.Lines[i];
            var tb = new TextBlock { Text = l.Text.Length == 0 ? "♪" : l.Text, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = ly.Synced ? Pal.Sub : Pal.Text, Opacity = ly.Synced ? 0.55 : 1 };
            _tbs.Add(tb);
            if (ly.Synced && l.Start >= 0)
            {
                var start = l.Start;
                _lines.Children.Add(K.Clickable(tb, () => _a.Player.Seek(TimeSpan.FromMilliseconds(start)), radius: 8, pad: new Thickness(6, 2, 6, 2)));
            }
            else _lines.Children.Add(tb);
        }
        Highlight();
    }

    private void Highlight()
    {
        var ly = _a.Lyrics.Current;
        if (ly == null || !ly.Synced || _tbs.Count != ly.Lines.Count) return;
        var idx = ly.IndexAt((long)_a.Player.Position.TotalMilliseconds);
        if (idx == _active) return;
        if (_active >= 0 && _active < _tbs.Count) { _tbs[_active].Foreground = Pal.Sub; _tbs[_active].Opacity = 0.55; }
        _active = idx;
        if (idx >= 0)
        {
            _tbs[idx].Foreground = Pal.Text; _tbs[idx].Opacity = 1;
            var pt = _tbs[idx].TransformToVisual(_lines).TransformPoint(new Windows.Foundation.Point(0, 0));
            _sv.ChangeView(null, Math.Max(0, pt.Y - _sv.ViewportHeight / 3), null);
        }
    }
}
