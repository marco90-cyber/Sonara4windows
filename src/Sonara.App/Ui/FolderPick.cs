using Windows.Storage.Pickers;
using WinRT.Interop;
using Sonara.App.Services;

namespace Sonara.App.Ui;

public static class FolderPick
{
    public static IntPtr Hwnd { get; set; }

    /// <summary>Fa scegliere la cartella dei download, la salva e rilegge indice e copia di riserva.</summary>
    public static async Task<bool> PickDownloadFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
        InitializeWithWindow.Initialize(picker, Hwnd);
        var f = await picker.PickSingleFolderAsync();
        if (f == null) return false;
        var a = AppServices.I;
        a.Settings.DownloadFolder = f.Path;
        a.Settings.Save();
        a.Downloads.LoadIndex();
        a.Downloads.RestoreBackup(a.Store, a.Imports);
        a.Downloads.WriteBackup(a.Store, a.Imports);
        return true;
    }
}
