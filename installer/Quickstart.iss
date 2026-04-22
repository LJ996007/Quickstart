#define MyAppName "Quickstart"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef MyPublishDir
  #define MyPublishDir "..\\Quickstart\\bin\\Release\\net10.0-windows\\win-x64\\publish"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "output"
#endif
#ifndef MyOutputBaseFilename
  #define MyOutputBaseFilename "Quickstart-Setup-x64"
#endif

[Setup]
AppId={{6D7F4A64-82F7-4C3E-A683-53D68A63C54A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Quickstart
DefaultDirName={localappdata}\Programs\Quickstart
DefaultGroupName=Quickstart
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyOutputBaseFilename}
SetupIconFile=..\Quickstart\Resources\app.ico
UninstallDisplayIcon={app}\Quickstart.exe
AppMutex=Quickstart_SingleInstance_Mutex
CloseApplications=yes
CloseApplicationsFilter=Quickstart.exe

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional tasks:"

[Files]
Source: "{#MyPublishDir}\Quickstart.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Quickstart"; Filename: "{app}\Quickstart.exe"
Name: "{autodesktop}\Quickstart"; Filename: "{app}\Quickstart.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Quickstart.exe"; Description: "Launch Quickstart after setup"; Flags: nowait postinstall skipifsilent
