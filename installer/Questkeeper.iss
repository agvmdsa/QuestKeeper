; Questkeeper installer (Inno Setup 6).
;
; Built by .github/workflows/release.yml after the single-file publish, with the version from the tag:
;   iscc /DMyAppVersion=0.2.0 /DMyAppVersionLabel=0.2.0 installer\Questkeeper.iss
; The publish folder (out\publish) holds Questkeeper.exe, renamed from AqwCompanion.exe by the workflow.
;
; Npcap is NOT bundled and NOT downloaded here: its license forbids redistribution without the Nmap Project's permission.
; The installer only checks for it (the same two files the app checks) and sends the player to the official download page.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef MyAppVersionLabel
  #define MyAppVersionLabel MyAppVersion
#endif
#define MyAppName "Questkeeper"
#define MyAppExe "Questkeeper.exe"
#define MyAppUrl "https://github.com/agvmdsa/QuestKeeper"
#define NpcapUrl "https://npcap.com/#download"
; The folder that already holds the settings and saved data of earlier versions (the assembly kept its first name).
#define MyDataFolder "AqwCompanion"

[Setup]
; Never change this id: it is how Windows knows later versions replace this one.
AppId={{6F3C1A52-8B7E-4D0A-9C41-2E5D7A90B1C3}
AppName={#MyAppName}
AppVersion={#MyAppVersionLabel}
VersionInfoVersion={#MyAppVersion}
AppPublisher=agvmdsa
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; The app asks for administrator rights at every start (the capture driver needs them), so the installer does too.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Questkeeper-Setup-v{#MyAppVersionLabel}
SetupIconFile=..\src\AqwCompanion.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; The app hides in the notification area when its window is closed, so ask the player to exit it from there before updating.
AppMutex=Local\{#MyDataFolder}
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\out\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
; Only offered when Npcap is missing; the player decides by leaving the box checked or not.
Filename: "{#NpcapUrl}"; Description: "Open the Npcap download page (needed to read the game data)"; Flags: shellexec postinstall skipifsilent; Check: not NpcapInstalled
Filename: "{app}\{#MyAppExe}"; Description: "Start {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; "Start with Windows" in the app's settings creates this scheduled task.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""{#MyAppName}"" /F"; Flags: runhidden; RunOnceId: "RemoveQuestkeeperTask"

[Code]
var
  NpcapPage: TWizardPage;
  NpcapStatus: TNewStaticText;
  NpcapOpenButton: TNewButton;
  NpcapCheckButton: TNewButton;

// The same two files the app looks for.
function NpcapInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll')) or FileExists(ExpandConstant('{sys}\wpcap.dll'));
end;

procedure RefreshNpcap;
begin
  if NpcapInstalled then
  begin
    NpcapStatus.Caption := 'Npcap is installed. Questkeeper can read the game data.';
    NpcapOpenButton.Visible := False;
    NpcapCheckButton.Visible := False;
  end
  else
  begin
    NpcapStatus.Caption := 'Npcap was not found on this computer. You can finish this setup now and install Npcap afterwards, ' +
      'but Questkeeper shows nothing until it is installed.';
    NpcapOpenButton.Visible := True;
    NpcapCheckButton.Visible := True;
  end;
end;

procedure OpenNpcapPage(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', '{#NpcapUrl}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure CheckNpcapAgain(Sender: TObject);
begin
  RefreshNpcap;
end;

procedure InitializeWizard;
var
  Intro: TNewStaticText;
begin
  NpcapPage := CreateCustomPage(wpSelectDir, 'Npcap',
    'Questkeeper reads the game data with the Npcap packet capture driver.');

  Intro := TNewStaticText.Create(NpcapPage);
  Intro.Parent := NpcapPage.Surface;
  Intro.WordWrap := True;
  Intro.AutoSize := False;
  Intro.Left := 0;
  Intro.Top := 0;
  Intro.Width := NpcapPage.SurfaceWidth;
  Intro.Height := ScaleY(60);
  Intro.Caption :=
    'Npcap is a separate program with its own license, so it is not included in this setup. ' +
    'You install it yourself from the official site: it is free for personal use on up to five computers.';

  NpcapStatus := TNewStaticText.Create(NpcapPage);
  NpcapStatus.Parent := NpcapPage.Surface;
  NpcapStatus.WordWrap := True;
  NpcapStatus.AutoSize := False;
  NpcapStatus.Left := 0;
  NpcapStatus.Top := Intro.Top + Intro.Height + ScaleY(8);
  NpcapStatus.Width := NpcapPage.SurfaceWidth;
  NpcapStatus.Height := ScaleY(48);
  NpcapStatus.Font.Style := [fsBold];

  NpcapOpenButton := TNewButton.Create(NpcapPage);
  NpcapOpenButton.Parent := NpcapPage.Surface;
  NpcapOpenButton.Left := 0;
  NpcapOpenButton.Top := NpcapStatus.Top + NpcapStatus.Height + ScaleY(8);
  NpcapOpenButton.Width := ScaleX(200);
  NpcapOpenButton.Height := ScaleY(26);
  NpcapOpenButton.Caption := 'Open the Npcap download page';
  NpcapOpenButton.OnClick := @OpenNpcapPage;

  NpcapCheckButton := TNewButton.Create(NpcapPage);
  NpcapCheckButton.Parent := NpcapPage.Surface;
  NpcapCheckButton.Left := NpcapOpenButton.Left + NpcapOpenButton.Width + ScaleX(8);
  NpcapCheckButton.Top := NpcapOpenButton.Top;
  NpcapCheckButton.Width := ScaleX(120);
  NpcapCheckButton.Height := ScaleY(26);
  NpcapCheckButton.Caption := 'Check again';
  NpcapCheckButton.OnClick := @CheckNpcapAgain;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = NpcapPage.ID then
    RefreshNpcap;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\{#MyDataFolder}');
    if (not UninstallSilent) and DirExists(DataDir) then
      if MsgBox('Also remove what Questkeeper saved on this computer (settings, saved characters, logs)?',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
