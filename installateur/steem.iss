; Steem — installateur par utilisateur (aucun droit administrateur), à partager.
; Construit par construire.ps1 en deux versions :
;  - complète (exe autonome, .NET inclus, ~65 Mo) : publication\n;  - légère (/DLeger, ~200 Ko) : publication-leger\, demande .NET 8 Desktop Runtime s'il manque.

#ifndef Version
  #define Version "0.12.4"
#endif

[Setup]
AppId={{7C3E9A52-1B6D-4F80-A2E4-9D5B13C8F061}
AppName=Steem
AppVersion={#Version}
AppVerName=Steem {#Version}
AppPublisher=kowboy2k
DefaultDirName={autopf}\Steem
DefaultGroupName=Steem
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=Sortie
#ifdef Leger
OutputBaseFilename=Steem_Setup_{#Version}_leger
#else
OutputBaseFilename=Steem_Setup_{#Version}
#endif
SetupIconFile=..\Steem.ico
UninstallDisplayIcon={app}\Steem.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
InfoAfterFile=apres.txt

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "demarrage"; Description: "Lancer Steem avec Windows"

[Files]
#ifdef Leger
Source: "publication-leger\Steem.exe"; DestDir: "{app}"; Flags: ignoreversion
#else
Source: "publication\Steem.exe"; DestDir: "{app}"; Flags: ignoreversion
#endif

[Icons]
Name: "{group}\Steem"; Filename: "{app}\Steem.exe"

[Registry]
; Même valeur que l'interrupteur « Lancer avec Windows » du panneau.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Steem"; ValueData: """{app}\Steem.exe"""; Tasks: demarrage; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Steem"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\Steem.exe"; Flags: nowait runasoriginaluser skipifsilent postinstall; Description: "Lancer Steem"

[UninstallRun]
; Fermeture propre : la liste de contacts reprend sa place et Steam sa taille.
Filename: "{app}\Steem.exe"; Parameters: "--quitter"; Flags: runhidden waituntilterminated; RunOnceId: "QuitterSteem"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  // Mise à jour : ferme proprement l'instance installée avant de remplacer l'exe.
  if FileExists(ExpandConstant('{app}\Steem.exe')) then
    Exec(ExpandConstant('{app}\Steem.exe'), '--quitter', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

#ifdef Leger
// Version légère : Steem a besoin du « .NET 8 Desktop Runtime » de Microsoft.
function DotNet8Present: Boolean;
var
  F: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), F);
  if Result then FindClose(F);
end;

function InitializeSetup: Boolean;
var
  Code: Integer;
begin
  Result := True;
  if not DotNet8Present then
    if MsgBox('Steem a besoin de .NET 8 (Desktop Runtime), un composant gratuit de Microsoft d''environ 55 Mo, absent de ce PC.' + #13#10#13#10 +
              'Ouvrir son téléchargement maintenant ? Il suffit de l''installer avant de lancer Steem.' + #13#10#13#10 +
              '(Sinon, la version complète de l''installateur de Steem contient déjà tout.)',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe', '', '', SW_SHOWNORMAL, ewNoWait, Code);
end;
#endif
