[Setup]
AppName=ScrapIt Serial-to-TCP Bridge
AppVersion=1.2.1
AppPublisher=ScrapIt Software
DefaultDirName={autopf}\SerialToTcp
DefaultGroupName=ScrapIt Serial-to-TCP
OutputDir=installer\Output
OutputBaseFilename=SerialToTcpSetup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
AllowNetworkDrive=no
; Must match Program.MutexName. Setup/uninstall will ask the user to close the app instead of
; failing to overwrite a running exe.
AppMutex=ScrapItSerialToTcp_SingleInstance,Global\ScrapItSerialToTcp_SingleInstance
CloseApplications=yes

[Files]
Source: "SerialToTcp\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Serial-to-TCP Bridge"; Filename: "{app}\SerialToTcp.exe"
Name: "{group}\Uninstall"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Serial-to-TCP Bridge"; Filename: "{app}\SerialToTcp.exe"; Tasks: desktopicon
Name: "{userstartup}\Serial-to-TCP Bridge"; Filename: "{app}\SerialToTcp.exe"; Tasks: startupicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "startupicon"; Description: "Start automatically with Windows"; GroupDescription: "Additional icons:"; Flags: unchecked

[Run]
Filename: "{app}\SerialToTcp.exe"; Description: "Launch Serial-to-TCP Bridge"; Flags: nowait postinstall skipifsilent runasoriginaluser
