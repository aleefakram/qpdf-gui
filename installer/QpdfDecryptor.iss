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
AppName=qpdf Decryptor
AppVersion={#AppVersion}
AppPublisher=Internal Tools
DefaultDirName={localappdata}\Programs\qpdf Decryptor
DefaultGroupName=qpdf Decryptor
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
VersionInfoDescription=qpdf Decryptor Installer
VersionInfoProductName=qpdf Decryptor
VersionInfoProductVersion={#AppVersion}

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\QpdfDecryptor.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\qpdf Decryptor"; Filename: "{app}\QpdfDecryptor.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\qpdf Decryptor"; Filename: "{app}\QpdfDecryptor.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\QpdfDecryptor.exe"; Description: "Launch qpdf Decryptor"; Flags: nowait postinstall skipifsilent
