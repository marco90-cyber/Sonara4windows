using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sonara.App.Services;
using Sonara.App.Ui;
using Sonara.Core;
using Sonara.Core.Lyrics;

namespace Sonara.App.Views;

/// <summary>Finestra «Testo con AI»: scelta del modello e della lingua; poi mostra l'avanzamento. Il lavoro continua anche se la chiudi.</summary>
public static class LyricsAiDialog
{
    public static async void Show(XamlRoot root, Track t)
    {
        var a = AppServices.I;
        var s = a.Settings;
        var svc = a.LyricsAi;
        var model = LyricsAiService.ModelOf(s.WhisperModel);
        var lang = LyricsAiService.Langs.Any(l => l.Code == s.WhisperLang) ? s.WhisperLang : "auto";
        var body = new StackPanel { Spacing = 10, MinWidth = 420, MaxWidth = 520 };
        var dlg = new ContentDialog { XamlRoot = root, Title = "Testo con AI", Content = body, CloseButtonText = "Chiudi" };

        void Build()
        {
            body.Children.Clear();
            var st = svc.Current;
            if (svc.Busy)
            {
                body.Children.Add(K.T($"«{svc.Title}»", 14, Pal.Text, true, 2));
                body.Children.Add(K.T(st.Stage + (st.Percent > 0 ? $" · {st.Percent}%" : "…"), 13, Pal.Sub));
                body.Children.Add(new ProgressBar { Foreground = Pal.Accent, Maximum = 100, Value = st.Percent, IsIndeterminate = st.Percent <= 0 });
                body.Children.Add(K.T("Puoi chiudere questa finestra: il lavoro continua e il testo compare da solo.", 12, Pal.Sub, false, 0));
                dlg.PrimaryButtonText = ""; dlg.SecondaryButtonText = "Interrompi";
                return;
            }
            dlg.SecondaryButtonText = "";
            if (st.Phase == LyricsAiService.Phase.Failed)
            {
                body.Children.Add(K.T(st.Message, 13, K.Brush("#FF6B6B"), false, 0));
            }
            body.Children.Add(K.T("Whisper ascolta il brano e scrive il testo con i tempi di ogni parola. Funziona offline sul tuo PC: l'audio non esce mai dal computer.", 13, Pal.Sub, false, 0));
            body.Children.Add(K.T("Modello", 13, Pal.Text, true));
            var mrow = new StackPanel { Spacing = 6 };
            foreach (var m in LyricsAiService.Models)
            {
                var mm = m;
                var state = LyricsAiService.HasModel(m) ? "scaricato" : $"{m.Mb} MB da scaricare";
                mrow.Children.Add(K.Chip($"{m.Label} · {m.Note} · {state}", model.Id == m.Id, () => { model = mm; s.WhisperModel = mm.Id; s.Save(); Build(); }));
            }
            body.Children.Add(mrow);
            body.Children.Add(K.T("Lingua del testo", 13, Pal.Text, true));
            body.Children.Add(K.Wrap(LyricsAiService.Langs.Select(l => (UIElement)K.Chip(l.Name, lang == l.Code, () => { lang = l.Code; s.WhisperLang = l.Code; s.Save(); Build(); }))));
            if (!LyricsAiService.HasEngine)
                body.Children.Add(K.T("Al primo uso scarico anche il programma Whisper ufficiale (circa 5 MB).", 12, Pal.Sub, false, 0));
            body.Children.Add(K.T("Con «Automatica» riconosco la lingua una volta e la uso per tutto il brano. Se il canto è difficile prova Small.", 12, Pal.Sub, false, 0));
            dlg.PrimaryButtonText = "Avvia"; dlg.DefaultButton = ContentDialogButton.Primary;
        }

        void OnChanged() => Build();
        svc.Changed += OnChanged;
        Build();
        var r = await dlg.ShowAsync();
        svc.Changed -= OnChanged;
        if (r == ContentDialogResult.Primary) svc.Start(t, model, lang);
        else if (r == ContentDialogResult.Secondary) svc.Cancel();
    }
}
