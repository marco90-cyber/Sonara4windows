using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;

namespace Sonara.App;

/// <summary>Una riga di una lista di brani (per il modello in Templates.xaml).</summary>
public sealed class TrackRowVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public Track Track { get; }
    public string Number { get; }
    public string Title => Track.Title;
    public string Artist => Track.Artist;
    public string Duration => Track.DurationText;
    public ImageSource? Cover { get; }
    public ICommand FavCmd { get; }
    public ICommand MoreCmd { get; }
    public Action<TrackRowVm, FrameworkElement?>? ShowMenu { get; set; }

    private bool _current, _fav, _down;

    public TrackRowVm(Track t, int number, Action<TrackRowVm>? onMore = null)
    {
        Track = t;
        Number = number > 0 ? number.ToString() : "";
        Cover = K.Bmp(t.Cover, 96);
        var a = AppServices.I;
        _fav = a.Store.IsFavorite(t.VideoId);
        _down = a.Downloads.IsDownloaded(t.VideoId);
        FavCmd = new RelayCommand(() => { a.Store.ToggleFavorite(t); _fav = a.Store.IsFavorite(t.VideoId); Raise(nameof(FavGlyph)); Raise(nameof(FavBrush)); });
        MoreCmd = new RelayCommand(() => onMore?.Invoke(this));
    }

    public bool IsCurrent
    {
        get => _current;
        set { if (_current == value) return; _current = value; Raise(nameof(TitleBrush)); Raise(nameof(PlayingVis)); Raise(nameof(NumberVis)); }
    }

    public Brush TitleBrush => _current ? Pal.Accent : Pal.Text;
    public Visibility PlayingVis => _current ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NumberVis => _current ? Visibility.Collapsed : Visibility.Visible;
    public string FavGlyph => _fav ? K.HeartFill : K.Heart;
    public Brush FavBrush => _fav ? Pal.Accent : Pal.Sub;
    public Visibility DownVis => _down ? Visibility.Visible : Visibility.Collapsed;

    public void RefreshFlags()
    {
        var a = AppServices.I;
        _fav = a.Store.IsFavorite(Track.VideoId); _down = a.Downloads.IsDownloaded(Track.VideoId);
        Raise(nameof(FavGlyph)); Raise(nameof(FavBrush)); Raise(nameof(DownVis));
    }
}
