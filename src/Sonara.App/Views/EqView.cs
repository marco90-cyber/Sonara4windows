using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core.Audio;

namespace Sonara.App.Views;

/// <summary>Equalizzatore e DSP (grafico a bande, tono, effetti, limiter).</summary>
public sealed class EqView : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private DspSettings D => _a.Dsp;

    public EqView()
    {
        var root = new StackPanel { Margin = new Thickness(28, 16, 28, 28), Spacing = 14 };
        root.Children.Add(K.T("Equalizzatore", 30, Pal.Text, true));
        root.Children.Add(K.Tgl(D.Master, v => Apply(d => d.Master = v), "Elaborazione audio attiva (spenta = audio intatto)"));

        // preset
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var p in GraphicPresets.All)
        {
            var pr = p;
            presets.Children.Add(K.Chip(pr.Name, false, () => { Apply(d => d.ApplyCurve(pr.Curve)); Nav.Refresh(); }));
        }
        root.Children.Add(new ScrollViewer { Content = presets, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled, HorizontalScrollMode = ScrollMode.Enabled });

        // bande
        var bandsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var n in DspSettings.BandOptions)
        {
            var nn = n;
            bandsRow.Children.Add(K.Chip($"{n} bande", D.Bands == n, () => { Apply(d => d.SetBands(nn)); Nav.Refresh(); }));
        }
        root.Children.Add(bandsRow);

        var card = new Border { Background = Pal.Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(12) };
        var g = new Grid { ColumnSpacing = 4, Height = 240 };
        var freqs = D.Freqs;
        for (var i = 0; i < freqs.Count; i++)
        {
            var idx = i;
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var col = new Grid { RowSpacing = 4 };
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            col.RowDefinitions.Add(new RowDefinition());
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var val = K.T($"{D.GainAt(i):0.#}", 10, Pal.Sub);
            val.HorizontalAlignment = HorizontalAlignment.Center;
            var s = new Slider { Orientation = Orientation.Vertical, Minimum = -DspSettings.GainRange, Maximum = DspSettings.GainRange, Value = D.GainAt(i), StepFrequency = 0.5, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Pal.Accent, IsThumbToolTipEnabled = false };
            s.ValueChanged += (_, e) => { val.Text = $"{e.NewValue:0.#}"; Apply(d => { d.SetGain(idx, (float)e.NewValue); d.Master = true; d.Eq = true; d.EqMode = DspSettings.EqGraphic; }); };
            var f = freqs[i];
            var lab = K.T(f >= 1000 ? $"{f / 1000:0.#}k" : $"{f:0.#}", 10, Pal.Sub);
            lab.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(val);
            Grid.SetRow(s, 1); col.Children.Add(s);
            Grid.SetRow(lab, 2); col.Children.Add(lab);
            Grid.SetColumn(col, i); g.Children.Add(col);
        }
        card.Child = g;
        root.Children.Add(card);

        root.Children.Add(Section("Tono"));
        root.Children.Add(Slide("Bassi", D.BassDb, -12, 12, 0.5, v => Apply(d => { d.BassDb = (float)v; d.Master = true; }), "dB"));
        root.Children.Add(Slide("Alti", D.TrebleDb, -12, 12, 0.5, v => Apply(d => { d.TrebleDb = (float)v; d.Master = true; }), "dB"));
        root.Children.Add(Slide("Preamp", D.PreampDb, -12, 12, 0.5, v => Apply(d => { d.PreampOn = Math.Abs(v) > 0.01; d.PreampDb = (float)v; d.Master = true; }), "dB"));

        root.Children.Add(Section("Effetti"));
        root.Children.Add(K.Tgl(D.AnalogBass, v => Apply(d => { d.AnalogBass = v; d.Master = true; }), "Bassi analogici"));
        root.Children.Add(K.Tgl(D.Air, v => Apply(d => { d.Air = v; d.Master = true; }), "Aria (brillantezza)"));
        root.Children.Add(K.Tgl(D.Loudness, v => Apply(d => { d.Loudness = v; d.Master = true; }), "Loudness"));
        root.Children.Add(K.Tgl(D.Clarity, v => Apply(d => { d.Clarity = v; d.Master = true; }), "Chiarezza"));
        root.Children.Add(K.Tgl(D.Stereo, v => Apply(d => { d.Stereo = v; d.Master = true; }), "Stereo (espansione, bilanciamento, mono)"));
        root.Children.Add(Slide("Espansione stereo", D.Expand, 0, 1, 0.05, v => Apply(d => { d.Expand = (float)v; d.Stereo = v > 0; d.Master = true; }), ""));
        root.Children.Add(Slide("Crossfeed", D.Crossfeed, 0, 1, 0.05, v => Apply(d => { d.Crossfeed = (float)v; d.Stereo = true; d.Master = true; }), ""));
        root.Children.Add(K.Tgl(D.Dvc, v => Apply(d => { d.Dvc = v; d.Master = true; }), "Controllo dinamico del volume"));

        root.Children.Add(Section("Protezione"));
        root.Children.Add(K.Tgl(D.Limiter, v => Apply(d => { d.Limiter = v; d.Master = true; }), "Limiter (evita la distorsione)"));
        root.Children.Add(K.Tgl(D.AutoHeadroom, v => Apply(d => d.AutoHeadroom = v), "Margine automatico"));
        root.Children.Add(K.Pill("Azzera tutto", () => { Apply(d => { var f = new DspSettings(); _a.Dsp = f; }); Nav.Refresh(); }, false, K.Refresh));
        Content = new ScrollViewer { Content = root };
    }

    private static TextBlock Section(string t) => K.T(t, 18, Pal.Text, true);

    private UIElement Slide(string label, double v, double min, double max, double step, Action<double> on, string unit)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        g.Children.Add(K.T(label, 13, Pal.Text));
        var val = K.T($"{v:0.##} {unit}".Trim(), 12, Pal.Sub);
        var s = K.Sld(min, max, v, step);
        s.ValueChanged += (_, e) => { val.Text = $"{e.NewValue:0.##} {unit}".Trim(); on(e.NewValue); };
        Grid.SetColumn(s, 1); g.Children.Add(s);
        Grid.SetColumn(val, 2); g.Children.Add(val);
        return g;
    }

    private void Apply(Action<DspSettings> change)
    {
        change(_a.Dsp);
        _a.Dsp.Save();
        _a.Player.ApplyDsp();
    }
}
