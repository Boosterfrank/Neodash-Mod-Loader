[Setup]
; Basic Application Information
AppName=Neodash Mod Loader
AppVersion=1.3.0.0
AppPublisher=Boosterfrank
DefaultDirName={autopf}\Neodash Mod Loader
DefaultGroupName=Neodash Mod Loader
UninstallDisplayIcon={app}\Neodash Mod Loader.exe
SetupIconFile=Neodash Mod Loader\neodash mod loader logo no bg.ico
Compression=lzma2
SolidCompression=yes
OutputDir=Output
OutputBaseFilename=ModLoaderSetup
ArchitecturesInstallIn64BitMode=x64

; Auto-close the app if it's running during an update installation
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; This bundles the built win-x64 folder into the installer
Source: "Neodash Mod Loader\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Neodash Mod Loader\neodash mod loader logo no bg.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Neodash Mod Loader"; Filename: "{app}\Neodash Mod Loader.exe"; WorkingDir: "{app}"; IconFilename: "{app}\neodash mod loader logo no bg.ico"
Name: "{autodesktop}\Neodash Mod Loader"; Filename: "{app}\Neodash Mod Loader.exe"; WorkingDir: "{app}"; IconFilename: "{app}\neodash mod loader logo no bg.ico"; Tasks: desktopicon

[Run]
; Launch the app automatically after installation finishes
Filename: "{app}\Neodash Mod Loader.exe"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,Neodash Mod Loader}"; Flags: nowait postinstall skipifsilent
