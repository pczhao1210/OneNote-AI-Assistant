#define AssemblyVersion GetFileVersion("..\OneNoteAI.AddIn\bin\Release\OneNoteAI.AddIn.dll")

[Setup]
AppId={{67A7A8F4-62B1-449C-91E2-58257C4BFDAA}
AppName=OneNote AI Assistant
AppVersion=2.1.8
AppPublisher=OneNote AI Assistant
DefaultDirName={autopf}\OneNoteAI
DefaultGroupName=OneNote AI Assistant
OutputBaseFilename=OneNoteAISetup-2.1.8
ArchitecturesAllowed=x86 x64os
ArchitecturesInstallIn64BitMode=x64os
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "..\OneNoteAI.AddIn\bin\Release\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\OneNoteAI.AddIn\bin\Release\OneNoteAI.AddIn.dll.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\OneNoteAI.AddIn\bin\Release\*.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Office\OneNote\AddIns\OneNoteAI.Connect"; ValueType: string; ValueName: "Description"; ValueData: "OneNote AI Assistant - AI 智能助手"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Office\OneNote\AddIns\OneNoteAI.Connect"; ValueType: string; ValueName: "FriendlyName"; ValueData: "OneNote AI Assistant"
Root: HKCU; Subkey: "Software\Microsoft\Office\OneNote\AddIns\OneNoteAI.Connect"; ValueType: dword; ValueName: "LoadBehavior"; ValueData: "3"

#define ComRoot "HKCR32"
#define ComCheck ""
#include "com-registration.iss"
#undef ComRoot
#undef ComCheck
#define ComRoot "HKCR64"
#define ComCheck "Check: IsWin64"
#include "com-registration.iss"

[UninstallRun]
Filename: "{dotnet4064}\RegAsm.exe"; Parameters: "/u ""{app}\OneNoteAI.AddIn.dll"""; StatusMsg: "正在注销 64 位 COM 组件..."; Flags: runhidden waituntilterminated skipifdoesntexist; Check: IsWin64
Filename: "{dotnet40}\RegAsm.exe"; Parameters: "/u ""{app}\OneNoteAI.AddIn.dll"""; StatusMsg: "正在注销 32 位 COM 组件..."; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
const
  DotNet48Release = 528040;

function IsDotNet48Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and
    (Release >= DotNet48Release);

  if (not Result) and IsWin64 then
    Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and
      (Release >= DotNet48Release);
end;

function InitializeSetup: Boolean;
begin
  Result := IsDotNet48Installed;
  if not Result then
  begin
    MsgBox('需要先安装 .NET Framework 4.8 才能继续安装 OneNote AI Assistant。', mbCriticalError, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Microsoft\Office\OneNote\AddInsData\OneNoteAI.Connect');
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Microsoft\Office\Excel\AddIns\OneNoteAI.Connect');
  end;
end;
