using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace Sonara.App.Ui;

/// <summary>Piccoli costruttori di elementi grafici (l'interfaccia è scritta in codice, senza XAML).</summary>
public static class K
{
    // glifi Segoe Fluent Icons / MDL2
    public const string Home = "", Search = "", Library = "", Heart = "", HeartFill = "", Play = "", Pause = "",
        Next = "", Prev = "", Shuffle = "", Repeat = "", RepeatOne = "", Volume = "", Mute = "",
        Back = "", Forward = "", Bell = "", Settings = "", Download = "", DownDone = "", More = "", Add = "",
        ThumbUp = "", ThumbDown = "", Queue = "", Lyrics = "\uE90B", Eq = "", Mic = "", Bookmark = "",
        BookmarkFill = "\uE8A4", Refresh = "", Close = "", Folder = "", Person = "", Album = "", Music = "",
        Compass = "", Chevron = "", Check = "", Delete = "", Radio = "", Swap = "", Timer = "", Full = "", History = "\uE81C";

    public static Brush Brush(string hex)
    {
        var h = hex.TrimStart('#');
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, Convert.ToByte(h[..2], 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16)));
    }

    public static TextBlock T(string text, double size = 14, Brush? fg = null, bool bold = false, int lines = 1, double? w = null)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = size, Foreground = fg ?? Pal.Text, TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center,
        };
        if (lines > 1) { t.TextWrapping = TextWrapping.Wrap; t.MaxLines = lines; } else if (lines == 0) t.TextWrapping = TextWrapping.Wrap;
        if (w != null) t.Width = w.Value;
        return t;
    }

    public static FontIcon Icon(string glyph, double size = 16, Brush? fg = null) =>
        new() { Glyph = glyph, FontSize = size, Foreground = fg ?? Pal.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public static Uri? U(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https") ? u : null;
    }

    public static BitmapImage? Bmp(string? url, int decode = 0)
    {
        var u = U(url);
        if (u == null) return null;
        var b = new BitmapImage();
        if (decode > 0) b.DecodePixelWidth = decode;
        b.UriSource = u;
        return b;
    }

    /// <summary>Copertina quadrata (o tonda) con angoli arrotondati.</summary>
    public static Border Cover(string? url, double size, double radius = 8, bool round = false, bool fixedSize = true)
    {
        var b = new Border { Background = Pal.Card, CornerRadius = new CornerRadius(round ? size / 2 : radius) };
        if (fixedSize) { b.Width = size; b.Height = size; }
        var img = Bmp(url, (int)Math.Max(64, size * 1.5));
        if (img != null) b.Background = new ImageBrush { ImageSource = img, Stretch = Stretch.UniformToFill };
        else b.Child = Icon(Music, size * 0.35, Pal.Sub);
        return b;
    }

    public static void SetCover(Border b, string? url, int decode = 300)
    {
        var img = Bmp(url, decode);
        b.Background = img != null ? new ImageBrush { ImageSource = img, Stretch = Stretch.UniformToFill } : Pal.Card;
    }

    /// <summary>Un elemento cliccabile con bordo, riempimento e un colore al passaggio del mouse.</summary>
    public static Border Clickable(UIElement content, Action onClick, Brush? bg = null, Brush? hover = null, double radius = 8, Thickness? pad = null)
    {
        var normal = bg ?? Pal.Transparent;
        var over = hover ?? Pal.Hover;
        var b = new Border { Background = normal, CornerRadius = new CornerRadius(radius), Child = content, Padding = pad ?? new Thickness(0) };
        b.PointerEntered += (_, _) => b.Background = over;
        b.PointerExited += (_, _) => b.Background = normal;
        b.Tapped += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    public static Border IconBtn(string glyph, Action click, double size = 16, string? tip = null, Brush? fg = null, double box = 36)
    {
        var b = Clickable(Icon(glyph, size, fg), click, radius: box / 2);
        b.Width = box; b.Height = box;
        if (tip != null) ToolTipService.SetToolTip(b, tip);
        return b;
    }

    public static Border Pill(string text, Action click, bool primary = true, string? glyph = null, double height = 40)
    {
        var fg = primary ? Pal.OnAccent : Pal.Text;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (glyph != null) row.Children.Add(Icon(glyph, 14, fg));
        row.Children.Add(T(text, 14, fg, true));
        var b = Clickable(row, click, primary ? Pal.Accent : Pal.Card, primary ? K.Brush("#3DE87A") : Pal.Hover, height / 2, new Thickness(20, 0, 20, 0));
        b.Height = height;
        return b;
    }

    /// <summary>Una «pastiglia» selezionabile (filtro).</summary>
    public static Border Chip(string text, bool on, Action click)
    {
        var fg = on ? Pal.OnAccent : Pal.Text;
        var b = Clickable(T(text, 13, fg, on), click, on ? Pal.Accent : Pal.Card, on ? Pal.Accent : Pal.Hover, 16, new Thickness(14, 6, 14, 6));
        return b;
    }

    public static Border Sep() => new() { Height = 1, Background = Pal.Line, HorizontalAlignment = HorizontalAlignment.Stretch };

    public static Grid Header(string title, string? more = null, Action? onMore = null)
    {
        var g = new Grid { Margin = new Thickness(0, 24, 0, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(T(title, 20, Pal.Text, true));
        if (more != null && onMore != null)
        {
            var m = Clickable(T(more, 12, Pal.Sub), onMore, pad: new Thickness(8, 4, 8, 4));
            Grid.SetColumn(m, 1);
            g.Children.Add(m);
        }
        return g;
    }

    /// <summary>Scheda di uno scaffale: copertina, titolo e sottotitolo.</summary>
    public static UIElement CardTile(string? thumb, string title, string subtitle, Action click, double size = 150, bool round = false)
    {
        var sp = new StackPanel { Width = size, Spacing = 2 };
        var cover = Cover(thumb, size, 10, round);
        cover.Margin = new Thickness(0, 0, 0, 8);
        sp.Children.Add(cover);
        var t = T(title, 13, Pal.Text, true); sp.Children.Add(t);
        if (subtitle.Length > 0) sp.Children.Add(T(subtitle, 12, Pal.Sub));
        if (round) { t.HorizontalAlignment = HorizontalAlignment.Center; t.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center; }
        var b = Clickable(sp, click, radius: 12, pad: new Thickness(8));
        return b;
    }

    public static ScrollViewer HRow(IEnumerable<UIElement> items, double spacing = 8)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var i in items) sp.Children.Add(i);
        return new ScrollViewer
        {
            Content = sp, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
        };
    }

    public static ProgressRing Spinner(double size = 28) => new() { IsActive = true, Width = size, Height = size, Foreground = Pal.Accent, HorizontalAlignment = HorizontalAlignment.Center };

    public static string Dur(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    /// <summary>Un cursore con i colori dell'app.</summary>
    public static Slider Sld(double min, double max, double value, double step = 1)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = value, StepFrequency = step, Foreground = Pal.Accent };
        return s;
    }

    public static ToggleSwitch Tgl(bool on, Action<bool> changed, string? header = null)
    {
        var t = new ToggleSwitch { IsOn = on, OnContent = "", OffContent = "", MinWidth = 0 };
        if (header != null) t.Header = header;
        t.Toggled += (_, _) => changed(t.IsOn);
        return t;
    }

    public static async Task<bool> Confirm(XamlRoot root, string title, string body, string ok = "Conferma")
    {
        var d = new ContentDialog { XamlRoot = root, Title = title, Content = body, PrimaryButtonText = ok, CloseButtonText = "Annulla", DefaultButton = ContentDialogButton.Close };
        return await d.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task Info(XamlRoot root, string title, string body)
    {
        var d = new ContentDialog { XamlRoot = root, Title = title, Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap }, CloseButtonText = "Ok" };
        await d.ShowAsync();
    }
}
