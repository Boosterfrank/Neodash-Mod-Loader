[Setup]
; Basic Application Information
AppName=Neodash Mod Loader
AppVersion=1.4.0.0
VersionInfoVersion={#SetupSetting("AppVersion")}
AppPublisher=Boosterfrank
DefaultDirName={autopf}\Neodash Mod Loader
DefaultGroupName=Neodash Mod Loader
UninstallDisplayIcon={app}\Neodash Mod Loader.exe
SetupIconFile=src\Neodash.ModLoader\Assets\Branding\AppIcon.ico
Compression=lzma2
SolidCompression=yes
OutputDir=Output
OutputBaseFilename=ModLoaderSetup
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Auto-close the app if it's running during an update installation
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; This bundles the self-contained win-x64 publish folder into the installer
Source: "artifacts\publish\Neodash Mod Loader.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Neodash Mod Loader"; Filename: "{app}\Neodash Mod Loader.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Neodash Mod Loader.exe"
Name: "{autodesktop}\Neodash Mod Loader"; Filename: "{app}\Neodash Mod Loader.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Neodash Mod Loader.exe"; Tasks: desktopicon

[Run]
; Launch the app automatically after installation finishes
Filename: "{app}\Neodash Mod Loader.exe"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,Neodash Mod Loader}"; Flags: nowait postinstall skipifsilent

[Code]
#include "eng\LegacyRuntimeCleanup.iss"
#include "eng\LegacyRuntimeDirectories.iss"

function GetFileAttributes(lpFileName: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function IsReparsePoint(const Path: String): Boolean;
var
  Attributes: Cardinal;
begin
  Attributes := GetFileAttributes(Path);
  Result := (Attributes <> $FFFFFFFF) and
    ((Attributes and $00000400) <> 0);
end;

function ValidateLegacyCleanupPath(const Path: String): String;
var
  PathToCheck: String;
  ParentPath: String;
  RootPath: String;
begin
  Result := '';
  PathToCheck := RemoveBackslashUnlessRoot(Path);
  RootPath := AddBackslash(ExtractFileDrive(PathToCheck));

  while True do
  begin
    if IsReparsePoint(PathToCheck) then
    begin
      Result := 'Setup cannot remove legacy runtime files because this path is a linked directory: ' + PathToCheck;
      Exit;
    end;

    if CompareText(PathToCheck, RootPath) = 0 then
      Exit;

    ParentPath := ExtractFileDir(PathToCheck);
    if (ParentPath = '') or (CompareText(ParentPath, PathToCheck) = 0) then
      Exit;

    PathToCheck := ParentPath;
  end;
end;

function ValidateLegacyCleanupPaths(): String;
var
  AppDirectory: String;
  DirectoryIndex: Integer;
  LegacyRuntimeDirectories: TArrayOfString;
begin
  Result := '';
  AppDirectory := ExpandConstant('{app}');

  Result := ValidateLegacyCleanupPath(AppDirectory);
  if Result <> '' then
    Exit;

  LegacyRuntimeDirectories := GetLegacyRuntimeDirectories();
  for DirectoryIndex := 0 to LegacyRuntimeDirectoryCount - 1 do
  begin
    Result := ValidateLegacyCleanupPath(AddBackslash(AppDirectory) + LegacyRuntimeDirectories[DirectoryIndex]);
    if Result <> '' then
      Exit;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := ValidateLegacyCleanupPaths();
end;

procedure RemoveLegacyRuntimeFiles();
var
  AppDirectory: String;
  FileIndex: Integer;
  LegacyFile: String;
  LegacyRuntimeFiles: TArrayOfString;
begin
  AppDirectory := ExpandConstant('{app}');
  LegacyRuntimeFiles := GetLegacyRuntimeFiles();
  for FileIndex := 0 to LegacyRuntimeFileCount - 1 do
  begin
    LegacyFile := AddBackslash(AppDirectory) + LegacyRuntimeFiles[FileIndex];
    if FileExists(LegacyFile) and not DeleteFile(LegacyFile) then
      Log('Could not remove legacy runtime file: ' + LegacyFile);
  end;
end;

procedure RemoveEmptyLegacyRuntimeDirectories();
var
  AppDirectory: String;
  CleanupError: String;
  DirectoryIndex: Integer;
  LegacyDirectory: String;
  LegacyRuntimeDirectories: TArrayOfString;
begin
  AppDirectory := ExpandConstant('{app}');
  LegacyRuntimeDirectories := GetLegacyRuntimeDirectories();

  { The generated manifest is parent-first, so process it in reverse. This
    removes a child before its parent and never removes a non-empty folder. }
  for DirectoryIndex := LegacyRuntimeDirectoryCount - 1 downto 0 do
  begin
    LegacyDirectory := AddBackslash(AppDirectory) + LegacyRuntimeDirectories[DirectoryIndex];

    { Revalidate each path immediately before RemoveDir to avoid following a
      directory link created after the post-install validation. }
    CleanupError := ValidateLegacyCleanupPath(LegacyDirectory);
    if CleanupError <> '' then
      RaiseException(CleanupError);

    if DirExists(LegacyDirectory) and not RemoveDir(LegacyDirectory) then
      Log('Preserved non-empty legacy runtime directory: ' + LegacyDirectory);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  CleanupError: String;
begin
  if CurStep = ssPostInstall then
  begin
    { Recheck immediately before deletion so no linked path is traversed. }
    CleanupError := ValidateLegacyCleanupPaths();
    if CleanupError <> '' then
      RaiseException(CleanupError);

    RemoveLegacyRuntimeFiles();
    RemoveEmptyLegacyRuntimeDirectories();
  end;
end;
