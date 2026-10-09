#define AppName "WT - Blog Generator"
#define AppVersion "0.4.0"
[Setup]
AppId={{F33F7BB7-0279-493A-950B-95471820CD71}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Wheeltrade
DefaultDirName={localappdata}\Programs\WheelContentManager
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=WT-Blog-Generator-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\WheelContentManager.Desktop.exe
[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
[Files]
Source: "..\artifacts\WheelContentManager\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[InstallDelete]
Type: files; Name: "{userprograms}\Wheel Content Manager.lnk"
Type: files; Name: "{userdesktop}\Wheel Content Manager.lnk"
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WT-Blog-Generator"; ValueData: """{app}\WheelContentManager.Desktop.exe"" --background"; Flags: uninsdeletevalue; Tasks: autostart
[Icons]
Name: "{userprograms}\WT - Blog Generator"; Filename: "{app}\WheelContentManager.Desktop.exe"
Name: "{userdesktop}\WT - Blog Generator"; Filename: "{app}\WheelContentManager.Desktop.exe"; Tasks: desktopicon
[Tasks]
Name: autostart; Description: "Uruchamiaj program w tle po zalogowaniu do Windows"
Name: desktopicon; Description: "Utwórz skrót na pulpicie"; Flags: unchecked
[Run]
Filename: "{app}\WheelContentManager.Desktop.exe"; Description: "Uruchom WT - Blog Generator"; Flags: nowait postinstall skipifsilent
; Baza, eksport i sekrety w AppData\Local\WheelContentManager pozostają przy odinstalowaniu.
