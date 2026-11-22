using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Yt;

namespace Sonara.App.Views;

/// <summary>«Controlla durate»: trova i brani con l'audio fuori misura e propone l'alternativa.</summary>
public static class DurationDialog
{
    public static async Task ShowAsync(IEnumerable<Track> tracks, XamlRoot root)
    {
        var a = AppServices.I;
        var list = tracks.ToList();
        var status = K.T($"Controllo {list.Count} brani…", 13, Pal.Sub);
        var body = new StackPanel { Spacing = 6 };
        var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, list.Count) };
        var host = new StackPanel { Spacing = 8, MinWidth = 520, Children = { status, bar, new ScrollViewer { Content = body, MaxHeight = 380 } } };
        var d = new ContentDialog { XamlRoot = root, Title = "Controlla durate", Content = host, CloseButtonText = "Chiudi" };
        var cts = new CancellationTokenSource();
        d.Closed += (_, _) => cts.Cancel();
        _ = Task.Run(async () =>
        {
            try
            {
                var rows = await DurationCheck.ScanAsync(list, a.Overrides, (n, t) => a.RunUi(() => bar.Value = n), cts.Token);
                a.RunUi(() => Fill(rows, status, body, bar, a));
            }
            catch (Exception e) { a.RunUi(() => status.Text = "Controllo non riuscito: " + e.Message); }
        });
        await d.ShowAsync();
    }

    private static void Fill(List<DurationCheck.Row> rows, TextBlock status, StackPanel body, ProgressBar bar, AppServices a)
    {
        bar.Visibility = Visibility.Collapsed;
        var off = rows.Where(r => r.Status == DurationCheck.Status.Off).ToList();
        status.Text = off.Count == 0 ? $"Tutto a posto: {rows.Count} brani controllati." : $"{off.Count} brani fuori misura su {rows.Count} ({rows.Count(r => r.Status == DurationCheck.Status.NoRef)} senza riferimento).";
        foreach (var r in off)
        {
            var g = new Grid { ColumnSpacing = 10, Padding = new Thickness(4) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tx = new StackPanel();
            tx.Children.Add(K.T($"{r.Item.Artist} — {r.Item.Title}", 13, Pal.Text, true));
            var sub = $"ora {K.Dur(TimeSpan.FromSeconds(r.CurrentSec))}, ufficiale {K.Dur(TimeSpan.FromSeconds(r.Ref!.Seconds))} ({r.Ref.Source})";
            if (r.Pick != null) sub += $" · proposta {K.Dur(TimeSpan.FromSeconds(r.Pick.Source.Item.DurationSec))}{(r.Pick.Source.IsVideo ? " (video)" : "")}";
            else sub += " · nessuna alternativa";
            tx.Children.Add(K.T(sub, 11, Pal.Sub, false, 2));
            g.Children.Add(tx);
            if (r.Pick != null)
            {
                var b = new Button { Content = "Sostituisci" };
                var pick = r.Pick; var item = r.Item;
                b.Click += (_, _) => { a.Overrides.Set(item.VideoId, pick.Source.Item, false); b.Content = "Fatto"; b.IsEnabled = false; };
                Grid.SetColumn(b, 1); g.Children.Add(b);
            }
            body.Children.Add(g);
        }
    }
}
