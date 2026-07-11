; DarkTaskMgr one-click installer for Inno Setup (https://jrsoftware.org/isinfo.php).
;
; Produces DarkTaskMgrSetup.exe. DarkTaskMgr is a user-mode app that writes only HKCU and
; %APPDATA%, so the installer runs without administrator rights (PrivilegesRequired=lowest). It
; installs the executable, creates a Start Menu entry and an optional Desktop shortcut so it
; launches like any app, and starts it. Uninstall stops the app and removes the shortcuts.
;
; To build: open DarkTaskMgr.sln in Visual Studio and build (Release|x64 preferred, Debug|x64 also
; works) so DarkTaskMgr.exe exists, then open this file in Inno Setup and press Compile.

#define MyAppName "DarkTaskMgr"
#define MyAppVersion "1.0.4"
#define MyAppPublisher "PlexPixel"
#define MyAppExe "DarkTaskMgr.exe"

#if FileExists(AddBackslash(SourcePath) + "DarkTaskMgr\bin\x64\Release\" + MyAppExe)
  #define Config "Release"
#elif FileExists(AddBackslash(SourcePath) + "DarkTaskMgr\bin\x64\Debug\" + MyAppExe)
  #define Config "Debug"
#else
  #define Config "Release"
#endif

[Setup]
AppId={{D9F3B1A0-6C2E-4B7D-9E10-3A5C7F2D4B60}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\{#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=DarkTaskMgrSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
CloseApplications=yes
RestartApplications=no
SetupIconFile=DarkTaskMgr\darktaskmgr.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "DarkTaskMgr\bin\x64\{#Config}\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch DarkTaskMgr"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /im {#MyAppExe} /f"; Flags: runhidden; RunOnceId: "StopDarkTaskMgr"
