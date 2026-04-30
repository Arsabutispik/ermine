; Ermine.iss
#define MyAppName "Ermine"
#define MyAppExeName "Ermine.exe"
#define MyAppPublisher "İspik"

[Setup]
AppId={{bfe61419-4d34-4aad-b34f-ede5b310b8c4}}
AppName={#MyAppName}
AppVersion={#AppVersion} 
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
; The output directory for the compiled installer
OutputDir=installer-out 
OutputBaseFilename=Ermine-Windows-Installer
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; This grabs EVERYTHING in your publish output folder
Source: "out\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent