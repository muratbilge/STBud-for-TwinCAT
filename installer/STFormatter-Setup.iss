; TwinCAT ST Formatter - Inno Setup Installer
; Version: 1.0.0
;
; Prerequisites for BUILDING:
;   - Inno Setup (ISCC.exe on PATH) - https://jrsoftware.org/isdl.php
;   - Run build-installer.ps1 first to populate installer/files/
;
; This installer deploys:
;   [x] CLI Tool (stfmt) - requires .NET 8 runtime
;   [x] VS 2022 Extension - requires VS 2022
;   [x] TcXaeShell Host - requires Beckhoff TcXaeShell

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
LicenseFile=..\LICENSE
OutputDir=..\publish
OutputBaseFilename=STFormatter-Setup-{#AppVersion}
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
Name: "cli"; Description: "CLI Tool (stfmt)"; Types: full custom; Flags: checkable
Name: "vsix"; Description: "VS 2022 Extension"; Types: full custom; Flags: checkable
Name: "host"; Description: "TcXaeShell Host (requires TcXaeShell)"; Types: full custom; Flags: checkable

[Tasks]
Name: "hostautostart"; Description: "Start STFormatter Host automatically on login"; Flags: unchecked; Components: host
Name: "starthost"; Description: "Start STFormatter Host after installation"; Flags: unchecked; Components: host
Name: "addtopath"; Description: "Add stfmt to system PATH"; Flags: checkedonce; Components: cli

[Files]
; CLI Tool (net8.0, framework-dependent)
Components: cli; Flags: ignoreversion recursesubdirs; DestDir: "{app}\CLI"; Source: "files\cli\*"; Excludes: "*.pdb"

; VS 2022 Extension
Components: vsix; Flags: ignoreversion; DestDir: "{app}\VSIX"; Source: "files\vsix\*.vsix"

; TcXaeShell Host - net48 (for .NET 4.8+, TcXaeShell Build 4024+)
Components: host; Flags: ignoreversion; DestDir: "{app}\Host-net48"; Source: "files\host-net48\STFormatter.Host.exe"
Components: host; Flags: ignoreversion; DestDir: "{app}\Host-net48"; Source: "files\host-net48\STFormatter.Core.dll"
Components: host; Flags: ignoreversion; DestDir: "{app}\Host-net48"; Source: "files\host-net48\STFormatter.UI.dll"
Components: host; Flags: ignoreversion; DestDir: "{app}\Host-net48"; Source: "files\host-net48\Microsoft.VisualStudio.Interop.dll"

; TcXaeShell Host - net462 (for .NET 4.6.2, older TcXaeShell)
Components: host; Flags: ignoreversion recursesubdirs; DestDir: "{app}\Host-net462"; Source: "files\host-net462\*.dll"
Components: host; Flags: ignoreversion; DestDir: "{app}\Host-net462"; Source: "files\host-net462\STFormatter.Host.exe"
Components: host; Flags: ignoreversion; DestDir: "{app}\Host-net462"; Source: "files\host-net462\STFormatter.Core.dll"
Components: host; Flags: ignoreversion; DestDir: "{app}\Host-net462"; Source: "files\host-net462\STFormatter.UI.dll"
Components: host; Flags: ignoreversion; DestDir: "{app}\Host-net462"; Source: "files\host-net462\Microsoft.VisualStudio.Interop.dll"

; EditorConfig presets
Components: cli; Flags: ignoreversion; DestDir: "{app}\presets"; Source: "files\editorconfig-templates\*"

[Icons]
Name: "{group}\STFormatter CLI"; Filename: "{cmd}"; Parameters: "/k ""{app}\CLI\{#CliExeName}"""; Components: cli
Name: "{group}\STFormatter Host (TcXaeShell)"; Filename: "{app}\Host-net48\{#AppExeName}"; Components: host
Name: "{group}\Uninstall STFormatter"; Filename: "{uninstallexe}"

[Run]
; Register CLI in PATH
Components: cli; Filename: "{app}\CLI\{#CliExeName}"; Parameters: "--help"; Flags: runhidden nowait postinstall skipifsilent; Description: "Verify stfmt CLI"

; Install VSIX silently
Components: vsix; Filename: "{code:FindVSIXInstaller}"; Parameters: "/q ""{app}\VSIX\TwinCAT.STFormatter.{#AppVersion}.vsix"""; Flags: skipifsilent runhidden; StatusMsg: "Installing VS 2022 extension..."; Check:VSIXInstallerAvailable

; Deploy Host to TcXaeShell
Components: host; Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\deploy-host.ps1"""; Flags: runhidden; StatusMsg: "Deploying Host to TcXaeShell..."; Check:TcXaeShellInstalled

; Create auto-start shortcut
Components: host; Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -Command ""$ws = New-Object -ComObject WScript.Shell; $sc = $ws.CreateShortcut(''{autopf}\STFormatter Host.lnk''); $sc.TargetPath = ''{app}\Host-net48\{#AppExeName}''; $sc.WindowStyle = 7; $sc.Save()"""; Flags: runhidden; Check:AutoStartHost

; Copy auto-start shortcut to Startup folder
Components: host; Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -Command ""Copy-Item ''{autopf}\STFormatter Host.lnk'' ''$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\STFormatter Host.lnk'' -Force"""; Flags: runhidden; Check:AutoStartHost

; Start Host immediately
Components: host; Filename: "{app}\Host-net48\{#AppExeName}"; Flags: nowait postinstall unchecked; Description: "Start STFormatter Host now"; Check:TcXaeShellInstalled

[UninstallRun]
; Stop Host process if running
Filename: "taskkill.exe"; Parameters: "/f /im STFormatter.Host.exe"; Flags: runhidden; RunOnceId: "kill_host"

; Uninstall VSIX
Filename: "{code:FindVSIXInstaller}"; Parameters: "/u:{#SetupSetting("AppId")}"; Flags: skipifsilent runhidden; RunOnceId: "uninstall_vsix"

[UninstallDelete]
Type: filesanddirs; Name: "{app}\CLI"
Type: filesanddirs; Name: "{app}\VSIX"
Type: filesanddirs; Name: "{app}\Host-net48"
Type: filesanddirs; Name: "{app}\Host-net462"
Type: filesanddirs; Name: "{app}\presets"
Type: files; Name: "{app}\deploy-host.ps1"
Type: files; Name: "{localappdata}\STFormatter\settings.json"

[Registry]
; Add CLI to PATH (user level)
Components: cli; Root: HKCU; Subkey: "Environment"; ValueType: string; ValueName: "Path"; ValueData: "{reg:HKCU\Environment\Path|};{app}\CLI"; Flags: preservestringtype dontcreatekey uninsdeletevalue

[Code]
var
  TcXaeShellPath: string;
  DotNet48Installed: Boolean;
  DotNet462Installed: Boolean;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  TcXaeShellPath := '';

  RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Beckhoff\TcXaeShell\15.0',
    'InstallDir', TcXaeShellPath);
  if TcXaeShellPath = '' then
    RegQueryStringValue(HKLM, 'SOFTWARE\Beckhoff\TcXaeShell\15.0',
      'InstallDir', TcXaeShellPath);

  DotNet48Installed := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\528040');
  DotNet462Installed := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\394802');
end;

function TcXaeShellInstalled(): Boolean;
begin
  Result := (TcXaeShellPath <> '') or FileExists(ExpandConstant('{pf32}\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe'));
end;

function AutoStartHost(): Boolean;
begin
  Result := IsTaskSelected('hostautostart');
end;

function FindVSIXInstaller(Param: string): string;
var
  VSPath: string;
begin
  Result := '';

  VSPath := ExpandConstant('{pf32}\Microsoft Visual Studio\2022\Community\Common7\IDE\VSIXInstaller.exe');
  if FileExists(VSPath) then begin
    Result := VSPath;
    Exit;
  end;

  VSPath := ExpandConstant('{pf32}\Microsoft Visual Studio\2022\Professional\Common7\IDE\VSIXInstaller.exe');
  if FileExists(VSPath) then begin
    Result := VSPath;
    Exit;
  end;

  VSPath := ExpandConstant('{pf32}\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\VSIXInstaller.exe');
  if FileExists(VSPath) then begin
    Result := VSPath;
    Exit;
  end;

  VSPath := ExpandConstant('{pf}\Microsoft Visual Studio\2022\Community\Common7\IDE\VSIXInstaller.exe');
  if FileExists(VSPath) then begin
    Result := VSPath;
    Exit;
  end;

  VSPath := ExpandConstant('{pf}\Microsoft Visual Studio\2022\Professional\Common7\IDE\VSIXInstaller.exe');
  if FileExists(VSPath) then begin
    Result := VSPath;
    Exit;
  end;

  VSPath := ExpandConstant('{pf}\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\VSIXInstaller.exe');
  if FileExists(VSPath) then begin
    Result := VSPath;
    Exit;
  end;
end;

function VSIXInstallerAvailable(): Boolean;
begin
  Result := (FindVSIXInstaller('') <> '');
end;

function GetTcXaeShellExtensionsPath(Param: string): string;
begin
  if TcXaeShellPath <> '' then
    Result := TcXaeShellPath + 'Common7\IDE\Extensions\STFormatter'
  else
    Result := ExpandConstant('{pf32}\Beckhoff\TcXaeShell\Common7\IDE\Extensions\STFormatter');
end;

function ShouldUseNet462(): Boolean;
begin
  Result := DotNet462Installed and not DotNet48Installed;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  HostSrcDir: string;
  DstDir: string;
  deployScript: string;
begin
  if CurStep = ssPostInstall then
  begin
    if IsComponentSelected('host') then
    begin
      DstDir := GetTcXaeShellExtensionsPath('');

      deployScript :=
        '$ErrorActionPreference = "Stop"' + #10 +
        '$dst = "' + DstDir + '"' + #10 +
        'New-Item -ItemType Directory -Path $dst -Force | Out-Null' + #10 +
        'if (' + BoolToStr(ShouldUseNet462(), True) + ') {' + #10 +
        '  $src = "' + ExpandConstant('{app}\Host-net462') + '"' + #10 +
        '} else {' + #10 +
        '  $src = "' + ExpandConstant('{app}\Host-net48') + '"' + #10 +
        '}' + #10 +
        'Copy-Item "$src\*" $dst -Force' + #10 +
        'Write-Host "Deployed Host to $dst"';

      SaveStringToFile(ExpandConstant('{app}\deploy-host.ps1'), deployScript, False);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DstDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if IsComponentSelected('host') then
    begin
      DstDir := GetTcXaeShellExtensionsPath('');
      if DirExists(DstDir) then
        DelTree(DstDir, True, True, True);

      DeleteFile(ExpandConstant('{userappdata}\Microsoft\Windows\Start Menu\Programs\Startup\STFormatter Host.lnk'));
    end;
  end;
end;