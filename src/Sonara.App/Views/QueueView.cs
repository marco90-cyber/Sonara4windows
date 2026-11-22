using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;

namespace Sonara.App.Views;

/// <summary>La coda di riproduzione.</summary>
public sealed class QueueView : UserControl
{
    public QueueView()
    {
        var a = AppServices.I;
        var q = a.Player.Queue.ToList();
        var head = new StackPanel { Margin = new Thickness(28, 16, 28, 8), Spacing = 8 };
        head.Children.Add(K.T("Coda", 30, Pal.Text, true));
        head.Children.Add(K.T($"{q.Count} brani", 13, Pal.Sub));
        if (q.Count > 0) head.Children.Add(K.Pill("Svuota la coda", () => { a.Player.ClearQueue(); Nav.Refresh(); }, false, K.Delete));
        Content = q.Count == 0 ? new Grid { Children = { head } } : new TrackList(q, head);
    }
}
