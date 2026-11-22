using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.Core;

namespace Sonara.App.Ui;

/// <summary>Elenco di brani con righe virtualizzate: tocco = suona dalla riga, menu e cuore su ogni riga.</summary>
public sealed class TrackList : ListView
{
    private static readonly Templates Tpl = new();
    private readonly List<TrackRowVm> _rows = new();
    private readonly List<Track> _tracks;
    private readonly Action<Track>? _onRemove;

    public TrackList(IList<Track> tracks, UIElement? header = null, bool numbered = true, Action<Track>? onRemove = null)
    {
        _tracks = tracks.ToList();
        _onRemove = onRemove;
        ItemTemplate = (DataTemplate)Tpl["TrackRow"];
        SelectionMode = ListViewSelectionMode.None;
        IsItemClickEnabled = true;
        Background = Pal.Transparent;
        Padding = new Thickness(0, 0, 0, 24);
        if (header != null) Header = header;
        for (var i = 0; i < _tracks.Count; i++) _rows.Add(new TrackRowVm(_tracks[i], numbered ? i + 1 : 0, OnMore));
        ItemsSource = _rows;
        ItemClick += (_, e) =>
        {
            var vm = (TrackRowVm)e.ClickedItem;
            AppServices.I.Player.PlayList(_tracks, _tracks.IndexOf(vm.Track));
        };
        var p = AppServices.I.Player;
        void Sync() => MarkCurrent();
        p.StateChanged += Sync;
        Loaded += (_, _) => MarkCurrent();
        Unloaded += (_, _) => p.StateChanged -= Sync;
    }

    public IReadOnlyList<Track> Tracks => _tracks;

    private void MarkCurrent()
    {
        var cur = AppServices.I.Player.Current?.VideoId;
        foreach (var r in _rows) r.IsCurrent = cur != null && r.Track.VideoId == cur;
    }

    private void OnMore(TrackRowVm vm)
    {
        var anchor = ContainerFromItem(vm) as FrameworkElement ?? this;
        TrackMenu.Show(vm.Track, anchor, _tracks, _onRemove == null ? null : () => _onRemove(vm.Track));
    }
}
