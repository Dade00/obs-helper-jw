; OBS Helper JW — script Inno Setup.
; Compilare con installer\build-installer.cmd (pubblica + compila).
; Non include config.json né baseline.png: restano quelli dell'utente.

#define MyAppName "OBS Helper JW"
#define MyAppVersion "0.2.0"
#define MyAppExeName "WinRtMediaProbe.exe"

[Setup]
AppId={{30EE99DF-579A-4882-9EB1-535B9604A564}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\obs-studio\obs-helper-jw
DisableDirPage=no
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=OBS-Helper-JW-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
WizardStyle=modern
UninstallDisplayName={#MyAppName}

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "startup"; Description: "Avvia con Windows"; Flags: unchecked

[Files]
Source: "..\bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Excludes: "config.json,baseline.png"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\{#MyAppName} (impostazioni)"; Filename: "{app}\{#MyAppExeName}"; Comment: "Apri dal tray dopo l'avvio"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OBS Helper JW"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia ora {#MyAppName}"; Flags: nowait postinstall skipifsilent
