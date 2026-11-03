#define MyAppName "快剪"
#ifndef MyAppVersion
  #define MyAppVersion "0.5.1"
#endif
#ifndef MyAppOutputBaseFilename
  #define MyAppOutputBaseFilename "KuaiJian-0.5.1-Windows-x64-Setup"
#endif
#define MyAppPublisher "泾融號"
#define MyAppExeName "KuaiJian.WinUI.exe"

[Setup]
AppId={{B9CEB2E2-6A1A-4CFB-9A8A-4C8B9E7E0F40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\KuaiJian
DefaultGroupName={#MyAppName}
OutputDir=installer
OutputBaseFilename={#MyAppOutputBaseFilename}
; Fast LZMA keeps the installer compact while avoiding very long CI compression runs.
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=Assets\KuaiJian.ico

[Files]
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动{#MyAppName}"; Flags: nowait postinstall skipifsilent
