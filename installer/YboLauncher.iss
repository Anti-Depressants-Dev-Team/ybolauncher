; Inno Setup script for YBO Launcher.
;
; Builds the single-file setup .exe that ships alongside the plain zip. The app itself is
; unpackaged and self-contained, so this only has to put a folder somewhere, make a Start
; Menu shortcut and register an uninstaller - there is no runtime to install and nothing
; to register.
;
; Deliberately a per-user install: it needs no administrator rights, so there is no UAC
; prompt, and it matches how the app already stores its own settings and its optional
; "start with Windows" entry.
;
; Build with:
;   ISCC.exe /DAppVersion=0.2.0 /DPublishDir=<published folder> installer\YboLauncher.iss

#define AppName "YBO Launcher"
#define AppExeName "YboLauncher.exe"
#define AppPublisher "YBO"
#define AppUrl "https://github.com/Anti-Depressants-Dev-Team/ybolauncher"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\src\Launcher.App\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish"
#endif

#ifndef OutputDir
  #define OutputDir "output"
#endif

[Setup]
; Never change AppId: it is what lets a new version upgrade an existing install in place
; rather than landing beside it.
AppId={{795A9C8B-874D-4E54-AD64-7761EBA9A60E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; lowest = install for this user only, so no elevation and no UAC prompt. With it,
; {autopf} resolves to %LocalAppData%\Programs.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

LicenseFile=..\LICENSE
SetupIconFile=..\src\Launcher.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

; The app is 64-bit only and needs Windows 10 1809, same as the zip.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

; The payload is a couple of hundred megabytes of runtime, so maximum compression is
; worth the extra minute in CI.
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

OutputDir={#OutputDir}
OutputBaseFilename=YboLauncher-{#AppVersion}-setup

; The app may be sitting in the notification area; let Restart Manager close it rather
; than failing on a locked file.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

  { Matches AppInfo.DataFolderName, which is both the Run value name and the settings
    folder name. }
  ProductName = 'YBO Launcher';

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataFolder: string;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  { "Start with Windows" writes a Run entry pointing at the copy being removed. Leaving it
    behind would fail silently on every sign-in. }
  RegDeleteValue(HKEY_CURRENT_USER, RunKey, ProductName);

  { Tabs, settings and the icon cache are the user's, so removing them is opt-in and
    defaults to No. A silent uninstall never asks and never deletes them. }
  DataFolder := ExpandConstant('{localappdata}\' + ProductName);

  if UninstallSilent or not DirExists(DataFolder) then
    Exit;

  if MsgBox('Also remove your tabs, settings and cached icons?' + #13#10#13#10 + DataFolder,
       mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(DataFolder, True, True, True);
end;
