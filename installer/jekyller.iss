; Inno Setup 6 script for Jekyller
; Build via scripts/publish.ps1 (auto) or:
;   ISCC installer\jekyller.iss /DMyAppVersion=1.2.6 /DMyPublishDir=..\dist\publish\win-x64

#ifndef MyAppVersion
  #define MyAppVersion "1.2.6"
#endif
#ifndef MyPublishDir
  #define MyPublishDir "..\dist\publish\win-x64"
#endif

#define MyAppName "Jekyller"
#define MyAppPublisher "Jekyller"
#define MyAppURL "https://github.com/huang1988pioneer/Jekyller"
#define MyAppExeName "Jekyller.exe"

[Setup]
AppId={{B7F8251B-99F1-4F19-89E7-6D95D4D5E90B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist\releases\inno
OutputBaseFilename=Jekyller-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\Assets\avalonia-logo.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
