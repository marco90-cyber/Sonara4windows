using Windows.Media;

namespace Sonara.App.Services;

/// <summary>Tasti multimediali e controlli di sistema (SMTC).</summary>
public sealed class MediaKeys
{
    private readonly SystemMediaTransportControls _s;
    private readonly AppServices _a = AppServices.I;

    public MediaKeys(IntPtr hwnd)
    {
        _s = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
        _s.IsEnabled = true;
        _s.IsPlayEnabled = _s.IsPauseEnabled = _s.IsNextEnabled = _s.IsPreviousEnabled = true;
        _s.ButtonPressed += (_, e) => _a.RunUi(() =>
        {
            switch (e.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                case SystemMediaTransportControlsButton.Pause: _a.Player.TogglePlay(); break;
                case SystemMediaTransportControlsButton.Next: _a.Player.Next(); break;
                case SystemMediaTransportControlsButton.Previous: _a.Player.Previous(); break;
            }
        });
        _a.Player.StateChanged += Update;
        Update();
    }

    private void Update()
    {
        _a.RunUi(() =>
        {
            var p = _a.Player;
            _s.PlaybackStatus = p.Current == null ? MediaPlaybackStatus.Closed : p.Playing ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
            var u = _s.DisplayUpdater;
            u.Type = MediaPlaybackType.Music;
            u.MusicProperties.Title = p.Current?.Title ?? "";
            u.MusicProperties.Artist = p.Current?.Artist ?? "";
            u.Update();
        });
    }
}
