; TwinCAT ST Formatter - Inno Setup Installer
; Version: 1.0.0
;
; Prerequisites for BUILDING:
;   - Inno Setup (ISCC.exe on PATH) - https://jrsoftware.org/isdl.php
;   - Run build-installer.ps1 first to populate installer/files/
;
; This installer deploys:
;   [x] TcXaeShell Host - external COM DTE integration for TwinCAT XAE Shell
;   [x] CLI Tool (optional) - requires .NET 8 runtime

#define AppName "TwinCAT ST Formatter"
#define AppVersion "1.0.0"
#define AppPublisher "TwinCAT ST Formatter Project"
#define AppURL "https://github.com/anomalyco/opencode"
#define AppExeName "STFormatter.Host.exe"
#define CliExeName "STFormatter.CLI.exe"

[Setup]
AppId={{8d2e3a4f-b5c1-4a7e-9f3d-2c1e5b6a9d4e}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
DefaultDirName={autopf}\STFormatter
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\publish
OutputBaseFilename=STFormatter-Setup-{#AppVersion}
SetupIconFile=..\assets\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x86compatible x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupLogging=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Types]
Name: "full"; Description: "Full installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "host"; Description: "TcXaeShell Host"; Types: full custom
Name: "cli"; Description: "CLI Tool (stfmt)"; Types: full custom

[Tasks]
Name: "desktopshortcut"; Description: "Create a desktop shortcut for STFormatter Host"; Flags: checkedonce; Components: host
Name: "hostautostart"; Description: "Start STFormatter Host automatically on login"; Flags: unchecked; Components: host
Name: "starthost"; Description: "Start STFormatter Host after installation"; Flags: unchecked; Components: host
Name: "addtopath"; Description: "Add stfmt to user PATH"; Flags: checkedonce; Components: cli

[Files]
; CLI Tool (net8.0, framework-dependent)
Components: cli; Flags: ignoreversion recursesubdirs; DestDir: "{app}\CLI"; Source: "files\cli\*"; Excludes: "*.pdb"

; TcXaeShell Host - deploy directly to Beckhoff's extension folder.
Components: host; Flags: ignoreversion recursesubdirs; DestDir: "{code:GetTcXaeShellExtensionsPath}"; Source: "files\host-net48\*"; Check: ShouldUseNet48
Components: host; Flags: ignoreversion recursesubdirs; DestDir: "{code:GetTcXaeShellExtensionsPath}"; Source: "files\host-net462\*"; Check: ShouldUseNet462

; EditorConfig presets
Components: cli; Flags: ignoreversion; DestDir: "{app}\presets"; Source: "files\editorconfig-templates\*"

[Icons]
Name: "{group}\STFormatter Host (TcXaeShell)"; Filename: "{code:GetTcXaeShellHostPath}"; Components: host
Name: "{group}\STFormatter CLI"; Filename: "{cmd}"; Parameters: "/k ""{app}\CLI\{#CliExeName}"""; Components: cli
Name: "{group}\Uninstall STFormatter"; Filename: "{uninstallexe}"
Name: "{userdesktop}\STFormatter Host"; Filename: "{code:GetTcXaeShellHostPath}"; Tasks: desktopshortcut; Components: host
Name: "{userstartup}\STFormatter Host"; Filename: "{code:GetTcXaeShellHostPath}"; Tasks: hostautostart; Components: host

[Run]
; Verify CLI only when explicitly selected in the final wizard page.
Components: cli; Filename: "{app}\CLI\{#CliExeName}"; Parameters: "--help"; Flags: runhidden nowait postinstall skipifsilent unchecked; Description: "Verify stfmt CLI"

; Use explorer.exe so the Host starts non-elevated from an elevated installer.
Components: host; Filename: "{win}\explorer.exe"; Parameters: """{code:GetTcXaeShellHostPath}"""; Flags: nowait postinstall skipifsilent; Description: "Start STFormatter Host now"; Tasks: starthost

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/f /im STFormatter.Host.exe"; Flags: runhidden; RunOnceId: "kill_host"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\CLI"
Type: filesandordirs; Name: "{app}\presets"
Type: files; Name: "{localappdata}\STFormatter\settings.json"
Type: files; Name: "{userdesktop}\STFormatter Host.lnk"
Type: files; Name: "{userstartup}\STFormatter Host.lnk"

[Code]
var
  TcXaeShellPath: string;
  DotNet48Installed: Boolean;
  DotNet462Installed: Boolean;

const
  AppRegistryKey = 'Software\STFormatter';

function GetDotNetRelease(): Cardinal;
var
  Release: Cardinal;
begin
  Release := 0;
  RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release);
  Result := Release;
end;

function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := True;
  TcXaeShellPath := '';

  RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Beckhoff\TcXaeShell\15.0', 'InstallDir', TcXaeShellPath);
  if TcXaeShellPath = '' then
    RegQueryStringValue(HKLM, 'SOFTWARE\Beckhoff\TcXaeShell\15.0', 'InstallDir', TcXaeShellPath);

  Release := GetDotNetRelease();
  DotNet48Installed := Release >= 528040;
  DotNet462Installed := Release >= 394802;

  if not DotNet462Installed then
  begin
    MsgBox('TwinCAT ST Formatter Host requires .NET Framework 4.6.2 or newer. Install .NET Framework 4.8 and run this setup again.', mbCriticalError, MB_OK);
    Result := False;
  end;
end;

function TcXaeShellInstalled(): Boolean;
begin
  Result := (TcXaeShellPath <> '') or FileExists(ExpandConstant('{pf32}\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe'));
end;

function GetTcXaeShellIdePath(): string;
begin
  if TcXaeShellPath <> '' then
    Result := AddBackslash(TcXaeShellPath) + 'Common7\IDE'
  else
    Result := ExpandConstant('{pf32}\Beckhoff\TcXaeShell\Common7\IDE');
end;

function GetTcXaeShellExtensionsPath(Param: string): string;
begin
  Result := AddBackslash(GetTcXaeShellIdePath()) + 'Extensions\STFormatter';
end;

function GetTcXaeShellHostPath(Param: string): string;
begin
  Result := AddBackslash(GetTcXaeShellExtensionsPath('')) + '{#AppExeName}';
end;

function ShouldUseNet462(): Boolean;
begin
  Result := DotNet462Installed and not DotNet48Installed;
end;

function ShouldUseNet48(): Boolean;
begin
  Result := not ShouldUseNet462();
end;

function NormalizePathSegment(Value: string): string;
begin
  Result := Lowercase(RemoveBackslash(Trim(Value)));
end;

function PathContainsSegment(PathValue: string; Segment: string): Boolean;
var
  Remaining: string;
  Current: string;
  P: Integer;
  Target: string;
begin
  Result := False;
  Remaining := PathValue;
  Target := NormalizePathSegment(Segment);

  while Remaining <> '' do
  begin
    P := Pos(';', Remaining);
    if P > 0 then
    begin
      Current := Copy(Remaining, 1, P - 1);
      Delete(Remaining, 1, P);
    end
    else
    begin
      Current := Remaining;
      Remaining := '';
    end;

    if NormalizePathSegment(Current) = Target then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure AddCliToUserPath();
var
  CurrentPath: string;
  CliPath: string;
begin
  CliPath := ExpandConstant('{app}\CLI');
  RegQueryStringValue(HKCU, 'Environment', 'Path', CurrentPath);

  if not PathContainsSegment(CurrentPath, CliPath) then
  begin
    if CurrentPath = '' then
      CurrentPath := CliPath
    else
      CurrentPath := CurrentPath + ';' + CliPath;

    RegWriteStringValue(HKCU, 'Environment', 'Path', CurrentPath);
  end;
end;

procedure RemoveCliFromUserPath();
var
  CurrentPath: string;
  NewPath: string;
  Current: string;
  Remaining: string;
  CliPath: string;
  P: Integer;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', CurrentPath) then
    Exit;

  CliPath := NormalizePathSegment(ExpandConstant('{app}\CLI'));
  Remaining := CurrentPath;
  NewPath := '';

  while Remaining <> '' do
  begin
    P := Pos(';', Remaining);
    if P > 0 then
    begin
      Current := Copy(Remaining, 1, P - 1);
      Delete(Remaining, 1, P);
    end
    else
    begin
      Current := Remaining;
      Remaining := '';
    end;

    if (Trim(Current) <> '') and (NormalizePathSegment(Current) <> CliPath) then
    begin
      if NewPath = '' then
        NewPath := Current
      else
        NewPath := NewPath + ';' + Current;
    end;
  end;

  RegWriteStringValue(HKCU, 'Environment', 'Path', NewPath);
end;

procedure StopRunningHost();
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/f /im STFormatter.Host.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    StopRunningHost();

  if CurStep = ssPostInstall then
  begin
    if WizardIsComponentSelected('host') then
      RegWriteStringValue(HKLM, AppRegistryKey, 'HostInstallDir', GetTcXaeShellExtensionsPath(''));

    if WizardIsTaskSelected('addtopath') then
      AddCliToUserPath();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DstDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemoveCliFromUserPath();

    if not RegQueryStringValue(HKLM, AppRegistryKey, 'HostInstallDir', DstDir) then
      DstDir := GetTcXaeShellExtensionsPath('');

    if DirExists(DstDir) then
      DelTree(DstDir, True, True, True);

    RegDeleteValue(HKLM, AppRegistryKey, 'HostInstallDir');
    RegDeleteKeyIfEmpty(HKLM, AppRegistryKey);
  end;
end;
