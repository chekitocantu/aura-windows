; Aura for Windows installer (Inno Setup 6). Per-user install: no admin prompt.
; Build: iscc installer\aura.iss /DAppVersion=1.0.0   (expects the app in ..\publish)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{B6F0C2A4-3E1D-4F7A-9C55-6A8E1D2F4B71}
AppName=Aura
AppVersion={#AppVersion}
AppVerName=Aura {#AppVersion}
AppPublisher=Chekito
DefaultDirName={localappdata}\Programs\Aura
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
OutputDir=..\out
OutputBaseFilename=Aura-Setup-{#AppVersion}
SetupIconFile=..\src\Aura\Assets\aura.ico
UninstallDisplayIcon={app}\Aura.exe
UninstallDisplayName=Aura
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
ShowLanguageDialog=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Aura"; Filename: "{app}\Aura.exe"
Name: "{userdesktop}\Aura"; Filename: "{app}\Aura.exe"; Tasks: desktopicon

[Registry]
; Aura writes this itself when "open with Windows" is on; remove it on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Aura"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Aura.exe"; Description: "{cm:LaunchProgram,Aura}"; Flags: nowait postinstall

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM Aura.exe"; Flags: runhidden; RunOnceId: "StopAura"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Aura\cache"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  // An update must replace Aura.exe, so close the running copy (its videos close with it).
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Aura.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;
