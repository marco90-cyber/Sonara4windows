using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Yt;

namespace Sonara.App.Views;

/// <summary>Esplora: generi e atmosfere, nuove uscite, classifiche.</summary>
public sealed class ExploreView : UserControl
{
    private readonly StackPanel _body = new() { Padding = new Thickness(28, 16, 28, 40) };
    private readonly CancellationTokenSource _cts = new();

    public ExploreView()
    {
        Content = new ScrollViewer { Content = _body };
        _body.Children.Add(K.T("Esplora", 30, Pal.Text, true));
        Unloaded += (_, _) => _cts.Cancel();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var ct = _cts.Token;
        try
        {
            var moods = await YtApi.MoodsAndGenresAsync(ct);
            if (ct.IsCancellationRequested) return;
            if (moods.Count > 0)
            {
                _body.Children.Add(K.Header("Generi e atmosfere"));
                var wrap = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 200, ItemHeight = 56 };
                var i = 0;
                foreach (var m in moods)
                {
                    var hue = new[] { "#1E3A5F", "#3B1F5C", "#14532D", "#5C1F2E", "#5C4A1F", "#1F4F5C" }[i++ % 6];
                    var b = K.Clickable(K.T(m.Title, 14, K.Brush("#FFFFFF"), true), () => Actions.OpenEntry(m), K.Brush(hue), K.Brush("#34495E"), 10, new Thickness(14, 0, 8, 0));
                    b.Margin = new Thickness(0, 0, 10, 10); b.Height = 46; b.Width = 190;
                    wrap.Children.Add(b);
                }
                _body.Children.Add(wrap);
            }
            var news = await YtApi.NewReleasesAsync(ct);
            if (news != null && !ct.IsCancellationRequested) _body.Children.Add(Shelves.Make(news));
            var charts = await YtApi.ChartsAsync(ct);
            foreach (var c in charts) if (!ct.IsCancellationRequested) _body.Children.Add(Shelves.Make(c));
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { _body.Children.Add(K.T("Non riesco a caricare: " + e.Message, 13, Pal.Sub, 0)); }
    }
}
