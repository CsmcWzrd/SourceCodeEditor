#define AppName "SourceCodeEditor"
#define AppVersion "0.5.5"
[Setup]
AppId={{2D26E54C-A46D-42A3-914A-2C665EE8CE14}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=SourceCodeEditor
DefaultDirName={autopf64}\SourceCodeEditor
DefaultGroupName={#AppName}
OutputDir=dist
OutputBaseFilename=SourceCodeEditor-Setup-x64
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\SourceCodeEditor.exe
LicenseFile=LICENSE-upstream.txt
[Files]
Source: "dist\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
[Icons]
Name: "{group}\SourceCodeEditor"; Filename: "{app}\SourceCodeEditor.exe"
Name: "{group}\Uninstall SourceCodeEditor"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SourceCodeEditor"; Filename: "{app}\SourceCodeEditor.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
[Run]
Filename: "{app}\SourceCodeEditor.exe"; Description: "Launch SourceCodeEditor"; Flags: nowait postinstall skipifsilent
