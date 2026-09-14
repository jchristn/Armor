; Inno Setup script template for Armor. Placeholders in {{...}} are replaced by InnoChannel
; at build time. This installs the self-contained, single-file agent payload for one runtime.

#define MyAppName "{{AppName}}"
#define MyAppVersion "{{AppVersion}}"
#define MyAppPublisher "{{AppPublisher}}"
#define MyAppURL "{{AppUrl}}"
#define MyAppExeName "{{ExeName}}"

[Setup]
; Stable upgrade GUID for Armor. Keep this constant across releases so upgrades replace in place.
AppId={{7C4F1E2A-9B3D-4A6E-8C1F-2D5A7B9E0C34}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={{OutputDir}}
OutputBaseFilename={{OutputBaseName}}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed={{ArchitecturesAllowed}}
ArchitecturesInstallIn64BitMode={{ArchitecturesInstallIn64BitMode}}
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startuptask"; Description: "Start {#MyAppName} automatically at logon"; GroupDescription: "Startup:"

[Files]
; Recursively install the entire published payload directory.
Source: "{{PayloadDir}}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
; Register a Scheduled Task that launches the agent at logon with highest privileges. This is the
; daemon/tray startup registration the Installers spec requires for Windows.
Filename: "{sys}\schtasks.exe"; \
  Parameters: "/Create /F /RL HIGHEST /SC ONLOGON /TN ""{#MyAppName}"" /TR ""'{app}\{#MyAppExeName}'"""; \
  Flags: runhidden; Tasks: startuptask
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""{#MyAppName}"""; Flags: runhidden; RunOnceId: "DelArmorTask"
