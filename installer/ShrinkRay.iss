; Inno Setup script for ShrinkRay. tools\build-release.ps1 -Installer compiles it.
; Installs for the current user, with no admin prompt, into the folder tools\install-local.ps1
; uses. Settings and the swap journal live in %LOCALAPPDATA%\ShrinkRay, which neither the
; installer nor the uninstaller touches.

#ifndef AppVersion
  #error Pass the version with /DAppVersion=x.y.z
#endif

[Setup]
AppId={{4B9DA49F-F6B5-4FEB-A202-2F07493BF3DA}
AppName=ShrinkRay
AppVersion={#AppVersion}
AppPublisher=Jacob Poteet
AppPublisherURL=https://github.com/JacobPoteet/ShrinkRay
DefaultDirName={localappdata}\Programs\ShrinkRay
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; A running copy may be partway through a swap. Setup and uninstall wait for the user to close
; it, and never close it themselves.
AppMutex=ShrinkRay.Running
CloseApplications=no
RestartApplications=no
OutputDir=..\artifacts\release
OutputBaseFilename=ShrinkRay-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\ShrinkRay.App\Assets\Icon\ShrinkRay.ico
UninstallDisplayIcon={app}\ShrinkRay.exe

[InstallDelete]
; Left by a local install of a checkout, and wrong once a release replaces it.
Type: files; Name: "{app}\install.txt"

[Files]
Source: "..\artifacts\release\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\ShrinkRay"; Filename: "{app}\ShrinkRay.exe"

[Run]
Filename: "{app}\ShrinkRay.exe"; Description: "Launch ShrinkRay"; Flags: nowait postinstall skipifsilent
