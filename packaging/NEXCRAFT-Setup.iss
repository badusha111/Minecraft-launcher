#define AppName "NEXCRAFT"
#define AppVersion "0.1.0"
#define AppPublisher "NEXCRAFT"
#define AppExeName "NEXCRAFT.exe"
#define PublishDir "..\\artifacts\\NEXCRAFT"

[Setup]
AppId={{C7C0B6D1-9C1A-4F73-A15B-1D4D5A0B7E91}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\\Programs\\NEXCRAFT
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\\artifacts\\installer
OutputBaseFilename=NEXCRAFT-Setup
SetupIconFile=..\\src\\NexcraftLauncher\\Nexcraft.ico
UninstallDisplayIcon={app}\\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
CloseApplications=force
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные задачи:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\\NEXCRAFT"; Filename: "{app}\\{#AppExeName}"
Name: "{autodesktop}\\NEXCRAFT"; Filename: "{app}\\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\\{#AppExeName}"; Description: "Запустить NEXCRAFT"; Flags: postinstall nowait skipifsilent
