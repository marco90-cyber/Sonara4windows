#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={{6C0F6A4E-5B8D-4B7A-9D55-2F1E3A7C9B01}
AppName=Sonara
AppVersion={#AppVersion}
AppPublisher=Marco
DefaultDirName={autopf}\Sonara
DefaultGroupName=Sonara
OutputBaseFilename=Sonara-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Sonara.exe
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "Crea un'icona sul desktop"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Sonara"; Filename: "{app}\Sonara.exe"
Name: "{autodesktop}\Sonara"; Filename: "{app}\Sonara.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Sonara.exe"; Description: "Avvia Sonara"; Flags: nowait postinstall skipifsilent
