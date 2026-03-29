#define MyAppName "Androgen RAG"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MCxiaotao"
#define MyAppExeName "launcher.exe"

[Setup]
AppId={{F16781D7-B51D-46F2-B766-88E0C2A7F7E8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\AndrogenRAG
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=androgen-rag-setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl"

[Dirs]
Name: "{localappdata}\AndrogenRAG"
Name: "{localappdata}\AndrogenRAG\cache"
Name: "{localappdata}\AndrogenRAG\config"
Name: "{localappdata}\AndrogenRAG\input"
Name: "{localappdata}\AndrogenRAG\logs"
Name: "{localappdata}\AndrogenRAG\output"
Name: "{localappdata}\AndrogenRAG\packages"
Name: "{localappdata}\AndrogenRAG\runs"

[Files]
Source: "..\dist\launcher\launcher.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\config\launcher.template.json"; DestDir: "{app}"; DestName: "launcher.json"; Flags: ignoreversion
Source: "..\templates\current.json"; DestDir: "{localappdata}\AndrogenRAG"; Flags: onlyifdoesntexist
Source: "..\dist\bundles\{#MyAppVersion}\bundle\*"; DestDir: "{app}\bootstrap\{#MyAppVersion}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
