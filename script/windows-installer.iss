#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef OutputDirPath
  #error OutputDirPath is required
#endif

[Setup]
AppId={{1B59A6AF-CE94-4CB1-AC80-91622A1891C3}
AppName=Gacha Calendar
AppVersion={#AppVersion}
AppPublisher=Gacha Calendar contributors
AppPublisherURL=https://github.com/kkraymei/gacha-calendar
DefaultDirName={localappdata}\Programs\GachaCalendar
DefaultGroupName=Gacha Calendar
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDirPath}
OutputBaseFilename=GachaCalendar-{#AppVersion}-win-x64-Setup
SetupIconFile=..\src\GachaCalendar.App\Assets\app.ico
UninstallDisplayIcon={app}\GachaCalendar.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Gacha Calendar"; Filename: "{app}\GachaCalendar.exe"

[Run]
Filename: "{app}\GachaCalendar.exe"; Description: "Launch Gacha Calendar"; Flags: nowait postinstall skipifsilent
