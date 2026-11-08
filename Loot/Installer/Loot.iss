#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\bin\Release\net11.0\win-x64\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

#ifndef OutputBaseName
  #define OutputBaseName "loot-setup"
#endif

#ifndef SetupIcon
  #define SetupIcon "..\Assets\loot.ico"
#endif

#define MyAppName "千机"
#define MyAppPublisher "Loot"
#define MyAppExeName "Loot.exe"

[Setup]
AppId={{799FB6B9-ABF5-4394-8301-9522BB0030F0}}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Loot
DefaultGroupName=千机
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
SetupIconFile={#SetupIcon}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=千机
CloseApplications=yes

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Icons]
Name: "{autoprograms}\千机"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\千机"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动千机"; Flags: nowait postinstall skipifsilent
