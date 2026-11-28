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
    private readonly Border _panel = new() { Background = Pal.Panel, BorderBrush = Pal.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 10, 16, 12), MinWidth = 380 };
    private readonly List<List<Microsoft.UI.Xaml.Documents.Run>?> _runs = new();
    private readonly Microsoft.UI.Xaml.DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private int _active = -2;

    private static string Fmt(long ms) => (ms > 0 ? "+" : ms < 0 ? "−" : "") + (Math.Abs(ms) / 1000.0).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("it-IT")) + " s";

    /// <summary>Ritardo di sincronizzazione: pannello compatto sul testo, così si vede subito la riga evidenziata spostarsi.</summary>
    private void BuildPanel()
    {
        var t = _a.Player.Current;
        if (t == null) { _panel.Visibility = Visibility.Collapsed; return; }
        var v = _a.Lyrics.Offset(t.VideoId);
        var sp = new StackPanel { Spacing = 8 };
        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(K.Icon(K.Timer, 16, Pal.Sub));
        var val = K.T("Ritardo  " + Fmt(v), 16, Pal.Text, true); Grid.SetColumn(val, 1); head.Children.Add(val);
        var reset = K.Clickable(K.T("Azzera", 13, Pal.Accent, true), () => _a.Lyrics.SetOffset(t.VideoId, 0), pad: new Thickness(8, 4, 8, 4)); Grid.SetColumn(reset, 2); head.Children.Add(reset);
        var done = K.Clickable(K.T("Fatto", 13, Pal.Accent, true), () => _panel.Visibility = Visibility.Collapsed, pad: new Thickness(8, 4, 8, 4)); Grid.SetColumn(done, 3); head.Children.Add(done);
        sp.Children.Add(head);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var d in new long[] { -500, -100, 100, 500 })
        {
            var dd = d;
            row.Children.Add(K.Pill((dd > 0 ? "+" : "−") + (Math.Abs(dd) / 1000.0).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("it-IT")), () => _a.Lyrics.SetOffset(t.VideoId, _a.Lyrics.Offset(t.VideoId) + dd), false, null, 34));
        }
        sp.Children.Add(row);
        sp.Children.Add(K.T("Positivo: il testo arriva prima · negativo: dopo. Vale solo per questo brano.", 11, Pal.Sub, false, 2));
        _panel.Child = sp;
    }

    public LyricsView()
    {
        _sv.Content = _lines;
        var host = new Grid();
        host.Children.Add(_sv);
        _panel.Visibility = Visibility.Collapsed;
        _panel.VerticalAlignment = VerticalAlignment.Bottom; _panel.HorizontalAlignment = HorizontalAlignment.Center; _panel.Margin = new Thickness(12);
        host.Children.Add(_panel);
        Content = host;
        void OnLy() => Rebuild();
        void OnPos() => Highlight();
        void OnOff() { BuildPanel(); _active = -2; Highlight(); }
        _a.Lyrics.Changed += OnLy;
        _a.LyricsAi.Changed += OnLy;
        _a.Lyrics.OffsetChanged += OnOff;
        _a.Player.PositionChanged += OnPos;
        Unloaded += (_, _) => { _a.Lyrics.Changed -= OnLy; _a.LyricsAi.Changed -= OnLy; _a.Lyrics.OffsetChanged -= OnOff; _a.Player.PositionChanged -= OnPos; };
        // con i tempi per parola l'evidenziazione scorre parola per parola: serve un battito più fitto di quello del lettore
        _tick.Tick += (_, _) => { if (_runs.Any(r => r != null)) Highlight(); };
        Loaded += (_, _) => _tick.Start();
        Unloaded += (_, _) => _tick.Stop();
        Rebuild();
    }

    private void Rebuild()
    {
        _lines.Children.Clear(); _tbs.Clear(); _runs.Clear(); _active = -2;
        var t = _a.Player.Current;
        _lines.Children.Add(K.T(t?.Title ?? "Testo", 26, Pal.Text, true, 2));
        if (t != null) _lines.Children.Add(K.T(t.Artist, 14, Pal.Sub));
        var ly = _a.Lyrics.Current;
        if (t == null) { _lines.Children.Add(K.T("Nessun brano in riproduzione.", 14, Pal.Sub)); return; }
        if (_a.Lyrics.Loading) { _lines.Children.Add(K.Spinner()); return; }
        var ai = _a.LyricsAi;
        if (ai.Busy && ai.Current.TrackId == t.VideoId)
        {
            _lines.Children.Add(K.T("Scrivo il testo con l'AI", 14, Pal.Text, true));
            _lines.Children.Add(K.T(ai.Current.Stage + (ai.Current.Percent > 0 ? $" · {ai.Current.Percent}%" : "…"), 13, Pal.Sub));
            _lines.Children.Add(new ProgressBar { Foreground = Pal.Accent, Maximum = 100, Value = ai.Current.Percent, IsIndeterminate = ai.Current.Percent <= 0, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left });
            _lines.Children.Add(K.Pill("Dettagli…", () => LyricsAiDialog.Show(XamlRoot, t), false, null, 34));
            return;
        }
        if (ly == null)
        {
            _lines.Children.Add(K.T("Testo non trovato per questo brano.", 14, Pal.Sub));
            if (ai.Current.Phase == LyricsAiService.Phase.Failed && ai.Current.TrackId == t.VideoId)
                _lines.Children.Add(K.T("Testo con AI non riuscito: " + ai.Current.Message, 13, K.Brush("#FF6B6B"), false, 0));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(K.Pill("Riprova", async () => await _a.Lyrics.ReloadAsync(), false, K.Refresh));
            row.Children.Add(K.Pill("Genera il testo con AI", () => LyricsAiDialog.Show(XamlRoot, t), true, K.Lyrics));
            _lines.Children.Add(row);
            return;
        }
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tools.Children.Add(K.Pill("Ritardo…", () => { BuildPanel(); _panel.Visibility = _panel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }, false, K.Timer, 32));
        tools.Children.Add(K.Pill("Testo con AI…", () => LyricsAiDialog.Show(XamlRoot, t), false, K.Lyrics, 32));
        _lines.Children.Add(tools);
        _lines.Children.Add(K.T($"Fonte: {ly.Source} · {ly.Kind switch { SyncKind.Words => "sync parole", SyncKind.Lines => "sync righe", _ => "solo testo" }}", 12, Pal.Sub));
        for (var i = 0; i < ly.Lines.Count; i++)
        {
            var l = ly.Lines[i];
            var tb = new TextBlock { Text = l.Text.Length == 0 ? "♪" : l.Text, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = ly.Synced ? Pal.Sub : Pal.Text, Opacity = ly.Synced ? 0.55 : 1 };
            _tbs.Add(tb);
            if (ly.Kind == SyncKind.Words && l.Words.Count > 0)
            {
                tb.Text = "";
                var rs = new List<Microsoft.UI.Xaml.Documents.Run>();
                foreach (var w in l.Words) { var r = new Microsoft.UI.Xaml.Documents.Run { Text = w.Text, Foreground = Pal.Sub }; tb.Inlines.Add(r); rs.Add(r); }
                _runs.Add(rs);
            }
            else _runs.Add(null);
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
        if (ly == null || !ly.Synced || _tbs.Count != ly.Lines.Count || _runs.Count != _tbs.Count) return;
        var idx = _a.Lyrics.IndexNow(ly);
        if (idx != _active)
        {
            if (_active >= 0 && _active < _tbs.Count)
            {
                _tbs[_active].Foreground = Pal.Sub; _tbs[_active].Opacity = 0.55;
                if (_runs[_active] is { } old) foreach (var r in old) r.Foreground = Pal.Sub;
            }
            _active = idx;
            if (idx >= 0)
            {
                _tbs[idx].Foreground = Pal.Text; _tbs[idx].Opacity = 1;
                var pt = _tbs[idx].TransformToVisual(_lines).TransformPoint(new Windows.Foundation.Point(0, 0));
                _sv.ChangeView(null, Math.Max(0, pt.Y - _sv.ViewportHeight / 3), null);
            }
        }
        // parola per parola: quelle già cantate si accendono, le altre restano sbiadite
        if (idx >= 0 && _runs[idx] is { } runs)
        {
            var t = _a.Player.Current;
            var now = (long)_a.Player.Position.TotalMilliseconds + (t == null ? 0 : _a.Lyrics.Offset(t.VideoId));
            var ws = ly.Lines[idx].Words;
            for (var i = 0; i < runs.Count && i < ws.Count; i++)
            {
                var on = now >= ws[i].Start;
                var want = on ? Pal.Text : Pal.Sub;
                if (!ReferenceEquals(runs[i].Foreground, want)) runs[i].Foreground = want;
            }
        }
    }
}
