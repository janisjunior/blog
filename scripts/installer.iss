#define AppName "Wheel Content Manager"
#define AppVersion "0.1.0"
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
OutputBaseFilename=WheelContentManager-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\WheelContentManager.Desktop.exe
[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
[Files]
Source: "..\artifacts\WheelContentManager\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{userprograms}\Wheel Content Manager"; Filename: "{app}\WheelContentManager.Desktop.exe"
Name: "{userdesktop}\Wheel Content Manager"; Filename: "{app}\WheelContentManager.Desktop.exe"; Tasks: desktopicon
[Tasks]
Name: desktopicon; Description: "Utwórz skrót na pulpicie"; Flags: unchecked
[Run]
Filename: "{app}\WheelContentManager.Desktop.exe"; Description: "Uruchom Wheel Content Manager"; Flags: nowait postinstall skipifsilent
; Baza, eksport i sekrety w AppData\Local\WheelContentManager pozostają przy odinstalowaniu.
