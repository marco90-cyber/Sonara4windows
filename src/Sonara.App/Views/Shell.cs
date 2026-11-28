using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core.Yt;

namespace Sonara.App.Views;

/// <summary>Barra laterale, barra superiore, area centrale, pannello destro e barra del player.</summary>
public sealed class Shell : UserControl
{
    private readonly AppServices _a = AppServices.I;
    private readonly ContentControl _center = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly StackPanel _navBox = new() { Spacing = 2 };
    private readonly StackPanel _lists = new() { Spacing = 2 };
    private readonly FontIcon _back, _fwd;
    private readonly Border _backB, _fwdB;
    private readonly Border _bell;
    private readonly Grid _root = new() { Background = Pal.Bg };
    private CancellationTokenSource? _suggest;

    public Shell()
    {
        _root.RowDefinitions.Add(new RowDefinition());
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        _root.ColumnDefinitions.Add(new ColumnDefinition());
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });

        // sidebar
        var side = new Grid { Background = Pal.Side };
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.RowDefinitions.Add(new RowDefinition());
        var top = new StackPanel { Padding = new Thickness(12, 12, 12, 4), Spacing = 6 };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0, 0, 8) };
        brand.Children.Add(K.Icon(K.Music, 22, Pal.Accent));
        brand.Children.Add(K.T("Sonara", 20, Pal.Text, true));
        top.Children.Add(brand);
        top.Children.Add(_navBox);
        top.Children.Add(K.Sep());
        var libHead = new Grid { Margin = new Thickness(8, 8, 0, 0) };
        libHead.Children.Add(K.T("La tua libreria", 13, Pal.Sub, true));
        top.Children.Add(libHead);
        side.Children.Add(top);
        var sv = new ScrollViewer { Content = new StackPanel { Padding = new Thickness(12, 0, 12, 12), Children = { _lists } } };
        Grid.SetRow(sv, 1); side.Children.Add(sv);
        _root.Children.Add(side);
        Grid.SetRowSpan(side, 1);

        // centro
        var mid = new Grid();
        mid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        mid.RowDefinitions.Add(new RowDefinition());
        mid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bar = new Grid { Padding = new Thickness(16, 0, 16, 0), ColumnSpacing = 8 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _backB = K.IconBtn(K.Back, Nav.Back, 16, "Indietro"); _fwdB = K.IconBtn(K.Forward, Nav.Forward, 16, "Avanti");
        _back = (FontIcon)_backB.Child; _fwd = (FontIcon)_fwdB.Child;
        var arrows = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { _backB, _fwdB } };
        bar.Children.Add(arrows);
        var box = new AutoSuggestBox { PlaceholderText = "Cerca brano, album, playlist", MaxWidth = 520, VerticalAlignment = VerticalAlignment.Center, QueryIcon = new SymbolIcon(Symbol.Find) };
        box.TextChanged += async (s, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            _suggest?.Cancel(); _suggest = new CancellationTokenSource(); var ct = _suggest.Token;
            var q = s.Text.Trim();
            if (q.Length < 2) { s.ItemsSource = _a.Store.Searches.Take(8).ToList(); return; }
            try { await Task.Delay(200, ct); var l = await YtApi.SuggestAsync(q, ct); if (!ct.IsCancellationRequested) s.ItemsSource = l; } catch { }
        };
        box.GotFocus += (s, _) => { if (string.IsNullOrWhiteSpace(box.Text)) box.ItemsSource = _a.Store.Searches.Take(8).ToList(); };
        box.QuerySubmitted += (s, e) =>
        {
            var q = (e.ChosenSuggestion as string ?? e.QueryText ?? "").Trim();
            if (q.Length == 0) return;
            s.Text = q;
            Nav.Go("search:" + q, () => new SearchView(q));
        };
        Grid.SetColumn(box, 1); bar.Children.Add(box);
        _bell = K.IconBtn(K.Bell, () => ShowActivity(), 16, "Attività");
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { _bell, K.IconBtn(K.Settings, () => Nav.Go("settings", () => new SettingsView()), 16, "Impostazioni") } };
        Grid.SetColumn(right, 2); bar.Children.Add(right);
        mid.Children.Add(bar);
        Grid.SetRow(_center, 1); mid.Children.Add(_center);
        var act = new ActivityBar(); Grid.SetRow(act, 2); mid.Children.Add(act);
        Grid.SetColumn(mid, 1); _root.Children.Add(mid);

        // pannello destro
        var rp = new Border { Background = Pal.Panel, Child = new NowPlayingPanel() };
        Grid.SetColumn(rp, 2); _root.Children.Add(rp);

        // barra player
        var pb = new PlayerBar();
        Grid.SetRow(pb, 1); Grid.SetColumnSpan(pb, 3); _root.Children.Add(pb);

        Content = _root;

        Nav.Navigated += OnNav;
        _a.Store.Changed += BuildLists; _a.Imports.Changed += BuildLists;
        Loaded += (_, _) =>
        {
            _a.Player.Error += m => _a.RunUi(() => _ = K.Info(XamlRoot, "Riproduzione", m));
            if (Nav.CurrentKey == "") Nav.Go("home", () => new HomeView());
        };
        BuildNav(); BuildLists();
        StartAutoRefresh();
    }

    private async void AutoRefreshTick()
    {
        try
        {
            var st = _a.Settings;
            if (!st.ImportAutoRefresh || _a.Runner.Active || _a.Imports.All.Count == 0) return;
            var age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _a.Imports.LastCheck;
            if (age < st.ImportRefreshHours * 3600_000L) return;
            var changes = await _a.Runner.RefreshAllAsync();
            var any = changes.Where(c => c.Any).ToList();
            if (st.ImportNotify && any.Count > 0 && XamlRoot != null)
                _a.RunUi(() => _ = K.Info(XamlRoot, "Playlist aggiornate", string.Join("\n", any.Select(c => $"{c.Title}: +{c.Added} −{c.Removed}"))));
        }
        catch { }
    }

    private async void CheckUpdate()
    {
        try
        {
            if (!_a.Settings.CheckUpdates) return;
            using var h = new System.Net.Http.HttpClient();
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Sonara");
            var js = await h.GetStringAsync("https://api.github.com/repos/marco90-cyber/Sonara4windows/releases/latest");
            var n = System.Text.Json.Nodes.JsonNode.Parse(js);
            var tag = n?["tag_name"]?.GetValue<string>()?.TrimStart('v');
            var cur = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (tag == null || !Version.TryParse(tag, out var nv) || cur == null) return;
            if (nv.Revision < 0 && nv.Build >= 0) nv = new Version(nv.Major, nv.Minor, nv.Build, 0);
            if (nv <= cur) return;
            _a.RunUi(async () =>
            {
                if (await K.Confirm(XamlRoot, "Aggiornamento disponibile", $"È uscita la versione {tag}. Vuoi scaricarla?", "Scarica"))
                    await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/marco90-cyber/Sonara4windows/releases/latest"));
            });
        }
        catch { }
    }

    private void StartAutoRefresh()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        t.Tick += (_, _) => AutoRefreshTick();
        Loaded += (_, _) => { t.Start(); AutoRefreshTick(); CheckUpdate(); };
    }

    private void OnNav(UIElement view, string key)
    {
        _a.RunUi(() =>
        {
            _center.Content = view;
            _back.Foreground = Nav.CanBack ? Pal.Text : Pal.Sub;
            _fwd.Foreground = Nav.CanForward ? Pal.Text : Pal.Sub;
            BuildNav();
        });
    }

    private void BuildNav()
    {
        _navBox.Children.Clear();
        void Item(string glyph, string label, string key, Func<UIElement> make)
        {
            var on = Nav.CurrentKey == key;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            row.Children.Add(K.Icon(glyph, 18, on ? Pal.Accent : Pal.Text));
            row.Children.Add(K.T(label, 14, on ? Pal.Text : Pal.Sub, on));
            _navBox.Children.Add(K.Clickable(row, () => Nav.Go(key, make), on ? Pal.Card : null, null, 8, new Thickness(12, 10, 12, 10)));
        }
        Item(K.Home, "Home", "home", () => new HomeView());
        Item(K.Compass, "Esplora", "explore", () => new ExploreView());
        Item(K.Library, "Raccolta", "library", () => new LibraryView());
        Item(K.Shuffle, "Generator", "generator", () => new GeneratorView());
    }

    private void BuildLists()
    {
        _a.RunUi(() =>
        {
            _lists.Children.Clear();
            void Row(Entry e)
            {
                var g = new Grid { ColumnSpacing = 10 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(K.Cover(e.Thumb, 36, 6, e.Kind == Kind.Artists));
                var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                tx.Children.Add(K.T(e.Title, 13, Pal.Text)); tx.Children.Add(K.T(e.Subtitle, 11, Pal.Sub));
                Grid.SetColumn(tx, 1); g.Children.Add(tx);
                _lists.Children.Add(K.Clickable(g, () => Actions.OpenEntry(e), null, null, 8, new Thickness(6)));
            }
            _lists.Children.Add(K.Clickable(new TextBlock { Text = $"♥  Brani preferiti ({_a.Store.Favorites.Count})", FontSize = 13, Foreground = Pal.Text }, () => Nav.Go("library", () => new LibraryView()), null, null, 8, new Thickness(8, 10, 8, 10)));
            foreach (var p in _a.Store.MyPlaylists)
            {
                var pp = p;
                var g = new Grid { ColumnSpacing = 10 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(K.Cover(pp.Items.FirstOrDefault()?.Cover, 36, 6));
                var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                tx.Children.Add(K.T(pp.Name, 13, Pal.Text)); tx.Children.Add(K.T($"Playlist tua · {pp.Items.Count} brani", 11, Pal.Sub));
                Grid.SetColumn(tx, 1); g.Children.Add(tx);
                _lists.Children.Add(K.Clickable(g, () => Nav.Go("library:" + pp.Id, () => new LibraryView("Playlist", pp.Id)), null, null, 8, new Thickness(6)));
            }
            foreach (var i in _a.Imports.All) Row(i.Entry);
            foreach (var e in _a.Store.Saved) Row(e);
        });
    }

    private void ShowActivity()
    {
        var r = _a.Runner.Current;
        var sp = new StackPanel { Spacing = 8, MinWidth = 260 };
        if (_a.Runner.Active) { var pr = new ImportProgress(); pr.Set(r.Name, r.Done, r.Total); sp.Children.Add(pr); }
        else sp.Children.Add(K.T("Nessuna attività in corso", 13, Pal.Sub));
        sp.Children.Add(K.Pill("Importa playlist", () => ImportDialog.Show(XamlRoot), false, K.Add));
        var fl = new Flyout { Content = sp };
        fl.ShowAt(_bell);
    }
}
