; PP274: the installer, pointed at what this tree actually produces.
;
; This started as upstream's wizard output and kept naming upstream's world: MyAppPath was
; ..\chiaki-ng-Win, which resolves to the REPOSITORY ROOT rather than into build\ and is a
; directory no build here has ever created, and MyAppExeName was chiaki.exe, the Qt client
; PP21 turned off by default. Its [Files] section then copied that directory whole, which
; would have carried 34 Qt DLLs, windeployqt's plugin trees and a chiaki.exe into an
; installer for an application that loads none of them.
;
; PP273 is what makes this file short. build\chiaki-ng-package holds the published host,
; the three native libraries the resolver loads and the closure of what those import - and
; nothing else, because package.cmd wipes the directory and walks the imports rather than
; copying whatever was last deployed. So the recursive glob below is honest here in a way
; it was not before: the payload directory IS the file list.
;
; Compiled by package.cmd, which publishes and stages first. Compiling it by hand against a
; stale or absent payload is what the #error guards.

#define MyAppName "chiaki-ng"
#define MyAppPublisher "Street Pea"
#define MyAppURL "https://streetpea.github.io/chiaki-ng/"
#define MyAppExeName "ChiakiNg.exe"
#define MyAppPath "..\build\chiaki-ng-package"

; The payload is read at COMPILE time, twice: once for the version below and once for the
; file list. Without this, an absent payload produces a version of "0.0.0" and an installer
; carrying nothing, and both of those look like a successful build.
#if !FileExists(AddBackslash(SourcePath) + MyAppPath + "\" + MyAppExeName)
#error No payload at MyAppPath. Run package.cmd from the repository root - it publishes the host, stages the native libraries and then compiles this script.
#endif

; Read off the packaged exe rather than written here. app\ChiakiNg.csproj sets Version to
; 1.10.0 to match CMakeLists' CHIAKI_VERSION - one version for the two executables - and
; keeps IncludeSourceRevisionInInformationalVersion off precisely so an installer can reuse
; it verbatim. A literal in this file would be a second place to update and the one nobody
; remembers.
#define MyAppVersion() \
  GetVersionComponents(AddBackslash(SourcePath) + MyAppPath + "\" + MyAppExeName, Local[0], Local[1], Local[2], Local[3]), \
  Str(Local[0]) + "." + Str(Local[1]) + "." + Str(Local[2])

[Setup]
; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
AppId={{A329DCDE-074D-4C82-959A-3CFAC9A26B1F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
; "ArchitecturesAllowed=x64compatible" specifies that Setup cannot run
; on anything but x64 and Windows 11 on Arm.
ArchitecturesAllowed=x64compatible
; "ArchitecturesInstallIn64BitMode=x64compatible" requests that the
; install be done in "64-bit mode" on x64 or Windows 11 on Arm,
; meaning it should use the native 64-bit Program Files directory and
; the 64-bit view of the registry.
ArchitecturesInstallIn64BitMode=x64compatible
DisableDirPage=no
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSES\AGPL-3.0-only-OpenSSL.txt
; Uncomment the following line to run in non administrative install mode (install for current user only.)
;PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputBaseFilename=chiaki-ng-windows-installer
; Into build\, which is ignored, because an installer is output and not source. Upstream's
; OutputDir was the repository root, which is how a 90 MB artefact ends up in a diff.
OutputDir=..\build
SetupIconFile=..\gui\chiaking.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The whole payload directory, and that is the point: package.cmd wipes it and rebuilds it
; from the resolver's own table, so a file being in there is the statement that the host
; loads it. Listing DLLs by name here would be a second inventory to keep in step.
Source: "{#MyAppPath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
