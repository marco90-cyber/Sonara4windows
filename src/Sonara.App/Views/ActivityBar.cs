using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;

namespace Sonara.App.Views;

/// <summary>Striscia sempre visibile (qualunque pagina) con l'avanzamento di importazioni e download. Il lavoro continua anche se cambi pagina.</summary>
public sealed class ActivityBar : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private readonly StackPanel _box = new() { Spacing = 6 };

    public ActivityBar()
    {
        Content = _box;
        void U() => _a.RunUi(Update);
        _a.Runner.Progress += U;
        _a.Downloads.Changed += U;
        Unloaded += (_, _) => { _a.Runner.Progress -= U; _a.Downloads.Changed -= U; };
        Update();
    }

    private UIElement Line(string text, double done, int total, Action cancel, string cancelTip)
    {
        var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(14, 8, 8, 8), Background = Pal.Card };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(K.T(text, 12, Pal.Text));
        var bar = new ProgressBar { Foreground = Pal.Accent, VerticalAlignment = VerticalAlignment.Center };
        if (total <= 0 || done <= 0) bar.IsIndeterminate = true; else { bar.Maximum = total; bar.Value = Math.Min(done, total); }
        Grid.SetColumn(bar, 1); g.Children.Add(bar);
        var c = K.IconBtn(K.Close, cancel, 14, cancelTip, null, 30);
        Grid.SetColumn(c, 2); g.Children.Add(c);
        return g;
    }

    private void Update()
    {
        _box.Children.Clear();
        if (_a.Runner.Active)
        {
            var r = _a.Runner.Current;
            var name = string.IsNullOrEmpty(r.Name) ? "playlist" : r.Name;
            var more = r.Queued > 1 ? $" (+{r.Queued - 1} in coda)" : "";
            _box.Children.Add(Line($"Abbino «{name}»: {r.Done} di {r.Total} brani{more}", r.Done, r.Total, () => _a.Runner.CancelAll(), "Annulla importazione"));
        }
        if (_a.Downloads.Active)
        {
            var prog = _a.Downloads.Progress.Where(p => p.Value.Done < p.Value.Total).ToList();
            var done = prog.Sum(p => p.Value.Done); var total = prog.Sum(p => p.Value.Total);
            var cur = _a.Downloads.Current;
            var text = total > 0 ? $"Scarico {done} di {total} brani" + (cur.Length > 0 ? $" — {cur}" : "") : "Scarico…";
            _box.Children.Add(Line(text, done + _a.Downloads.Fraction, total, () => _a.Downloads.CancelAll(), "Ferma i download"));
        }
        Visibility = _box.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
