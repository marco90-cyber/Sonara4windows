using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.Core;

namespace Sonara.App.Ui;

/// <summary>Le playlist create dall'utente: nome, aggiunta di brani.</summary>
public static class MyPlaylists
{
    /// <summary>Chiede un nome (finestra con una casella di testo); null se annulla.</summary>
    public static async Task<string?> PromptName(XamlRoot root, string title, string initial = "", string ok = "Salva")
    {
        var box = new TextBox { Text = initial, PlaceholderText = "Nome", MinWidth = 320 };
        box.SelectAll();
        var d = new ContentDialog { XamlRoot = root, Title = title, Content = box, PrimaryButtonText = ok, CloseButtonText = "Annulla", DefaultButton = ContentDialogButton.Primary };
        return await d.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
    }

    /// <summary>Aggiunge il brano alla playlist; dice com'è andata in una finestra breve.</summary>
    public static async void AddTo(string playlistId, Track t, XamlRoot? root)
    {
        var added = AppServices.I.Store.AddToPlaylist(playlistId, t);
        var name = AppServices.I.Store.MyPlaylists.FirstOrDefault(p => p.Id == playlistId)?.Name ?? "";
        if (root != null) await K.Info(root, added ? "Aggiunto" : "Già presente", added ? $"«{t.Title}» è in «{name}»." : $"«{t.Title}» c'era già in «{name}».");
    }

    public static async void AddToNew(Track t, XamlRoot? root)
    {
        if (root == null) return;
        var name = await PromptName(root, "Nuova playlist", "", "Crea");
        if (name == null) return;
        var p = AppServices.I.Store.CreatePlaylist(name);
        AppServices.I.Store.AddToPlaylist(p.Id, t);
    }
}
