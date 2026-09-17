#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #error SourceDir must point to the published application directory.
#endif
#ifndef OutputDir
  #error OutputDir must point to the installer output directory.
#endif

[Setup]
AppId={{5A43824A-4D82-4C94-9807-9F4969FA31B5}
AppName=PDF Ninja
AppVersion={#AppVersion}
AppPublisher=Internal Tools
DefaultDirName={localappdata}\Programs\PDF Ninja
DefaultGroupName=PDF Ninja
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=QpdfDecryptor-Setup
SetupIconFile=..\logo\qpdf.ico
UninstallDisplayIcon={app}\QpdfDecryptor.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoDescription=PDF Ninja Installer
VersionInfoProductName=PDF Ninja
VersionInfoProductVersion={#AppVersion}

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\QpdfDecryptor.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Native\*"; DestDir: "{app}\Native"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Shortcuts from the pre-rename "qpdf Decryptor" releases
Type: files; Name: "{autoprograms}\qpdf Decryptor.lnk"
Type: files; Name: "{autodesktop}\qpdf Decryptor.lnk"

[Icons]
Name: "{autoprograms}\PDF Ninja"; Filename: "{app}\QpdfDecryptor.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\PDF Ninja"; Filename: "{app}\QpdfDecryptor.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\QpdfDecryptor.exe"; Description: "Launch PDF Ninja"; Flags: nowait postinstall skipifsilent
