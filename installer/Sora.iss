; Build through scripts/Build-Installer.ps1 from a qualified folder publish.
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PayloadInclude
  #error PayloadInclude is required
#endif
#ifndef InstallerOutput
  #error InstallerOutput is required
#endif

[Setup]
AppId={{C29647D7-C220-477C-83B6-4118690E9CF8}
AppName=Sora
AppVersion={#AppVersion}
AppVerName=Sora {#AppVersion}
AppPublisher=Sora
AppPublisherURL=https://sora.portways.app
AppSupportURL=https://github.com/gauravxsuvo/Sora/issues
AppUpdatesURL=https://sora.portways.app/#download
#ifdef BrandIcon
SetupIconFile={#BrandIcon}
#endif
VersionInfoVersion={#AppVersion}.0
DefaultDirName={autopf}\Sora
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
OutputDir={#InstallerOutput}
OutputBaseFilename=Sora-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallLogging=yes
UninstallDisplayIcon={app}\Assets\sora-brand-{#AppVersion}.ico
CloseApplications=no
RestartApplications=no
AllowCancelDuringInstall=no
AppMutex=Local\Sora.LD360.Desktop
Uninstallable=yes
UninstallDisplayName=Sora
ChangesAssociations=no
ChangesEnvironment=no
WizardSizePercent=115
DisableWelcomePage=no

[Messages]
WelcomeLabel2=Setup will install Sora on this computer.%n%nBefore continuing, exit Sora from its tray menu so it can release hardware controls. Your profiles and startup preference will be kept during an upgrade.%n%nSora needs the .NET 8 Windows Desktop Runtime (x64). It does not install hardware drivers or change firmware.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
#include PayloadInclude

[Dirs]
; A reinstall can reuse the directory left by a keep-data uninstall.
; Remove it when empty even when this setup did not originally create it.
Name: "{app}"; Flags: uninsalwaysuninstall

[Icons]
Name: "{commonprograms}\Sora\Sora"; Filename: "{app}\Sora.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\sora-brand-{#AppVersion}.ico"; AppUserModelID: "Sora.Desktop"; Comment: "Hardware controls and maintenance"
Name: "{commondesktop}\Sora"; Filename: "{app}\Sora.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\sora-brand-{#AppVersion}.ico"; AppUserModelID: "Sora.Desktop"; Tasks: desktopicon; Comment: "Hardware controls and maintenance"

; Hardware cleanup requires the app to exit normally. Setup never force-closes it or launches hardware controls.
[Code]
var
  PurgeData: Boolean;

procedure RefreshShell(EventID: LongWord; Flags: LongWord; Item1: LongWord; Item2: LongWord);
  external 'SHChangeNotify@shell32.dll stdcall';
function NativeGetFileAttributes(Name: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function NormalPath(Path: String): String;
begin
  Result := RemoveBackslashUnlessRoot(ExpandFileName(RemoveQuotes(Path)));
end;

function RunningSoraError: String;
var
  Locator, Wmi, Processes: Variant;
begin
  Result := '';
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('.', 'root\cimv2');
    Processes := Wmi.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''Sora.exe''');
    if (Processes.Count > 0) or CheckForMutexes('Local\Sora.LD360.Desktop') then
      Result := 'Exit Sora from its tray menu and wait for hardware cleanup before continuing.';
  except
    Result := 'Windows could not verify that Sora is closed. Setup cannot continue safely.';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Error: String;
  RepairCode: Integer;
begin
  if CurStep = ssInstall then begin
    Error := RunningSoraError;
    if Error <> '' then RaiseException(Error);
  end;
  if CurStep = ssPostInstall then begin
    if not Exec(ExpandConstant('{app}\UpdateRunner\Sora.UpdateRunner.exe'), '--refresh-shortcuts',
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, RepairCode) then Log('Shortcut refresh could not start')
    else if RepairCode <> 0 then Log('Some existing shortcuts could not be refreshed');
    RefreshShell($08000000, 0, 0, 0);
  end;
end;

function HasRuntime(Name: String): Boolean;
var
  Names: TArrayOfString;
  I, J: Integer;
  Installed: Cardinal;
  Root, Patch, AssemblyPath: String;
  StableVersion: Boolean;
begin
  Result := False;
  { Microsoft's x64 runtime MSI registers sharedfx in the 32-bit registry view. }
  if not RegQueryStringValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', Root) then Exit;
  if RegGetValueNames(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\' + Name, Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Pos('8.0.', Names[I]) = 1 then begin
        Patch := Copy(Names[I], 5, Length(Names[I]));
        StableVersion := Length(Patch) > 0;
        for J := 1 to Length(Patch) do
          if (Patch[J] < '0') or (Patch[J] > '9') then StableVersion := False;
        if StableVersion and RegQueryDWordValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\' + Name, Names[I], Installed) then
          if Installed = 1 then begin
            AssemblyPath := AddBackslash(Root) + 'shared\' + Name + '\' + Names[I] + '\';
            if Name = 'Microsoft.NETCore.App' then Result := FileExists(AssemblyPath + 'System.Private.CoreLib.dll')
            else Result := FileExists(AssemblyPath + 'PresentationFramework.dll');
            if Result then Exit;
          end;
      end;
end;

function DirectoryError: String;
var
  Path, Parent: String;
  Attributes: LongWord;
  Entry: TFindRec;
  RetainedDataOnly, FoundData: Boolean;
begin
  Result := '';
  Path := NormalPath(ExpandConstant('{app}'));
  if CompareText(Path, NormalPath(ExpandConstant('{autopf}\Sora'))) <> 0 then begin
    Result := 'Install Sora in the default Program Files\Sora directory.';
    Exit;
  end;
  while Path <> '' do begin
    Attributes := NativeGetFileAttributes(Path);
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then begin
      Result := 'The installation directory or an ancestor is a link. Use a regular Program Files directory.';
      Exit;
    end;
    Parent := ExtractFileDir(Path);
    if CompareText(Path, Parent) = 0 then Break;
    Path := Parent;
  end;
  if DirExists(ExpandConstant('{app}')) and not FileExists(ExpandConstant('{app}\sora-package.json')) then begin
    { Keeping legacy installed data leaves only this directory after uninstall.
      Permit reinstall without overwriting it; continue refusing any other unowned content. }
    RetainedDataOnly := True;
    FoundData := False;
    if FindFirst(ExpandConstant('{app}\*'), Entry) then begin
      try
        repeat
          if (Entry.Name <> '.') and (Entry.Name <> '..') then begin
            if (CompareText(Entry.Name, 'data') = 0) and ((Entry.Attributes and $10) <> 0) and ((Entry.Attributes and $400) = 0) then
              FoundData := True
            else RetainedDataOnly := False;
          end;
        until not FindNext(Entry);
      finally
        FindClose(Entry);
      end;
    end;
    if not RetainedDataOnly or not FoundData then
      Result := 'The destination already exists without a Sora installer manifest. Inspect it before installing.';
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingVersion, PackageVersion: Int64;
begin
  Result := DirectoryError;
  if Result <> '' then Exit;
  Result := RunningSoraError;
  if Result <> '' then Exit;
  if not HasRuntime('Microsoft.NETCore.App') or not HasRuntime('Microsoft.WindowsDesktop.App') then begin
    Result := 'Install Microsoft .NET 8 Windows Desktop Runtime (x64) from dotnet.microsoft.com, then run Setup again.';
    Exit;
  end;
  if FileExists(ExpandConstant('{app}\Sora.exe')) then begin
    if not GetPackedVersion(ExpandConstant('{app}\Sora.exe'), ExistingVersion) then begin
      Result := 'The installed Sora version could not be read. Inspect the existing installation.';
      Exit;
    end;
    PackageVersion := PackVersionComponents({#StringChange(AppVersion, '.', ',')}, 0);
    if ComparePackedVersion(ExistingVersion, PackageVersion) > 0 then
      Result := 'A newer Sora is installed. Downgrades need a separate settings and rollback review.';
  end;
end;

function StartupTaskError: String;
var
  Scheduler, Tasks, Task, Actions, Action: Variant;
  I, J: Integer;
begin
  Result := '';
  try
    Scheduler := CreateOleObject('Schedule.Service');
    Scheduler.Connect;
    Tasks := Scheduler.GetFolder('\').GetTasks(1);
    for I := 1 to Tasks.Count do begin
      Task := Tasks.Item(I);
      if CompareText(Task.Name, 'Sora') = 0 then begin
        Actions := Task.Definition.Actions;
        for J := 1 to Actions.Count do begin
          Action := Actions.Item(J);
          { Sora registers only Exec actions. An unexpected action without Path fails closed. }
          if CompareText(NormalPath(Action.Path), NormalPath(ExpandConstant('{app}\Sora.exe'))) = 0 then begin
            Result := 'Turn off Start with Windows in the installed Sora, exit it through its tray menu, then uninstall.';
            Exit;
          end;
        end;
      end;
    end;
  except
    Result := 'Windows could not inspect the Sora startup task. Uninstall stopped to avoid leaving a broken task.';
  end;
end;

function InitializeUninstall: Boolean;
var
  Error: String;
begin
  Error := DirectoryError;
  if Error = '' then Error := RunningSoraError;
  Result := Error = '';
  if not Result then begin
    Log(Error);
    SuppressibleMsgBox(Error, mbError, MB_OK, IDOK);
  end;
  if Result then begin
    if UninstallSilent then
      PurgeData := ExpandConstant('{param:PURGEDATA|0}') = '1'
    else begin
      case MsgBox('Remove Sora completely?' + #13#10 + #13#10 +
        'Yes: remove saved profiles, settings, logs, hardware recovery records and downloaded update cache for all Windows users.' + #13#10 +
        'No: uninstall the application but keep saved data for a later reinstall.' + #13#10 + #13#10 +
        'Sora shortcuts and its startup task will be removed. Shared .NET and PawnIO installations will be kept.',
        mbConfirmation, MB_YESNOCANCEL or MB_DEFBUTTON1) of
        IDYES: PurgeData := True;
        IDNO: PurgeData := False;
        IDCANCEL: Result := False;
      end;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Error, Parameters: String;
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then begin
    Error := RunningSoraError;
    if Error <> '' then RaiseException(Error);
    Parameters := '--uninstall-cleanup';
    if PurgeData then Parameters := Parameters + ' --purge-data';
    if not Exec(ExpandConstant('{app}\UpdateRunner\Sora.UpdateRunner.exe'), Parameters,
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('Sora cleanup could not start. The application has not been uninstalled.');
    if ResultCode <> 0 then RaiseException('Sora cleanup did not finish. Resolve the reported issue, then run uninstall again.');
  end;
  if CurUninstallStep = usPostUninstall then RefreshShell($08000000, 0, 0, 0);
end;
