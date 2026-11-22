# Sonara per Windows

Porting di [Sonara](https://github.com/marco90-cyber/Sonara) (client YouTube Music solo audio) per Windows, in C# con WinUI 3.

- `src/Sonara.Core`: logica (YouTube Music, libreria, testi, importazione, DSP)
- `src/Sonara.App`: interfaccia WinUI 3 e riproduzione audio
- `installer/`: script Inno Setup; l'installer esce dalla build di GitHub Actions (Release)

Derivato da codice GPL-3.0: vedi `LICENSE` e `NOTICE`.
