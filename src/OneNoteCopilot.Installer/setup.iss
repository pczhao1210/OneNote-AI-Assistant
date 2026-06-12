[Setup]
AppId={{67A7A8F4-62B1-449C-91E2-58257C4BFDAA}
AppName=OneNote Copilot
AppVersion=1.0.0
AppPublisher=OneNote Copilot
DefaultDirName={autopf}\OneNoteCopilot
DefaultGroupName=OneNote Copilot
DisableProgramGroupPage=yes
OutputBaseFilename=OneNoteCopilotSetup
ArchitecturesAllowed=x86 x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "..\OneNoteCopilot.AddIn\bin\Release\OneNoteCopilot.AddIn.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\OneNoteCopilot.AddIn\bin\Release\OneNoteCopilot.AddIn.dll.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\OneNoteCopilot.AddIn\bin\Release\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\OneNoteCopilot.AddIn\bin\Release\Extensibility.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\OneNoteCopilot.AddIn\bin\Release\office.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\OneNoteCopilot.AddIn\bin\Release\Microsoft.Office.Interop.OneNote.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\OneNoteCopilot.AddIn\bin\Release\stdole.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\OneNoteCopilot.AddIn\bin\Release\*.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Office\OneNote\AddIns\OneNoteCopilot.Connect"; ValueType: string; ValueName: "Description"; ValueData: "OneNote Copilot - AI 智能助手"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Office\OneNote\AddIns\OneNoteCopilot.Connect"; ValueType: string; ValueName: "FriendlyName"; ValueData: "OneNote Copilot"
Root: HKCU; Subkey: "Software\Microsoft\Office\OneNote\AddIns\OneNoteCopilot.Connect"; ValueType: dword; ValueName: "LoadBehavior"; ValueData: "3"

Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}"; ValueType: string; ValueData: "OneNoteCopilot.AddIn.Connect"; Flags: uninsdeletekey
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}"; ValueType: string; ValueName: "AppID"; ValueData: "{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}"
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\Implemented Categories\{{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}"; Flags: uninsdeletekey
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\InprocServer32"; ValueType: string; ValueData: "mscoree.dll"; Flags: uninsdeletekey
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Both"
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\InprocServer32"; ValueType: string; ValueName: "Class"; ValueData: "OneNoteCopilot.AddIn.Connect"
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\InprocServer32"; ValueType: string; ValueName: "Assembly"; ValueData: "OneNoteCopilot.AddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\InprocServer32"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\InprocServer32"; ValueType: string; ValueName: "CodeBase"; ValueData: "{app}\OneNoteCopilot.AddIn.dll"
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "Class"; ValueData: "OneNoteCopilot.AddIn.Connect"
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "Assembly"; ValueData: "OneNoteCopilot.AddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\InprocServer32\1.0.0.0"; ValueType: string; ValueName: "CodeBase"; ValueData: "{app}\OneNoteCopilot.AddIn.dll"
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\ProgId"; ValueType: string; ValueData: "OneNoteCopilot.Connect"
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\Programmable"; ValueType: string; ValueData: ""
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\TypeLib"; ValueType: string; ValueData: "{{67A7A8F4-62B1-449C-91E2-58257C4BFDAA}"
Root: HKCR; Subkey: "CLSID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}\VersionIndependentProgID"; ValueType: string; ValueData: "OneNoteCopilot.Connect"

; --- AppID + DllSurrogate (force COM+ surrogate hosting like OneMore) ---
Root: HKCR; Subkey: "AppID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}"; ValueType: string; ValueData: "OneNoteCopilot.AddIn"; Flags: uninsdeletekey
Root: HKCR; Subkey: "AppID\{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}"; ValueType: string; ValueName: "DllSurrogate"; ValueData: ""
Root: HKCR; Subkey: "AppID\OneNoteCopilot.AddIn.dll"; ValueType: string; ValueName: "AppID"; ValueData: "{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}"; Flags: uninsdeletekey

Root: HKCR; Subkey: "TypeLib\{{67A7A8F4-62B1-449C-91E2-58257C4BFDAA}}\1.0"; ValueType: string; ValueData: "OneNote 的 Deepseek AI 智能助手插件"; Flags: uninsdeletekey
Root: HKCR; Subkey: "TypeLib\{{67A7A8F4-62B1-449C-91E2-58257C4BFDAA}}\1.0\0\win64"; ValueType: string; ValueData: "{app}\OneNoteCopilot.AddIn.tlb"
Root: HKCR; Subkey: "TypeLib\{{67A7A8F4-62B1-449C-91E2-58257C4BFDAA}}\1.0\FLAGS"; ValueType: string; ValueData: "0"
Root: HKCR; Subkey: "TypeLib\{{67A7A8F4-62B1-449C-91E2-58257C4BFDAA}}\1.0\HELPDIR"; ValueType: string; ValueData: "{app}"

Root: HKCR; Subkey: "OneNoteCopilot.Connect"; ValueType: string; ValueData: "OneNoteCopilot.AddIn.Connect"; Flags: uninsdeletekey
Root: HKCR; Subkey: "OneNoteCopilot.Connect\CLSID"; ValueType: string; ValueData: "{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}"
Root: HKCR; Subkey: "OneNoteCopilot.Connect\CurVer"; ValueType: string; ValueData: "OneNoteCopilot.Connect.1"
Root: HKCR; Subkey: "OneNoteCopilot.Connect.1"; ValueType: string; ValueData: "OneNoteCopilot.AddIn.Connect"; Flags: uninsdeletekey
Root: HKCR; Subkey: "OneNoteCopilot.Connect.1\CLSID"; ValueType: string; ValueData: "{{B3D4E5F6-A7B8-C9D0-E1F2-A3B4C5D6E7F8}"

[UninstallRun]
Filename: "{dotnet4064}\RegAsm.exe"; Parameters: "/u ""{app}\OneNoteCopilot.AddIn.dll"""; StatusMsg: "正在注销 64 位 COM 组件..."; Flags: runhidden waituntilterminated skipifdoesntexist; Check: IsWin64
Filename: "{dotnet40}\RegAsm.exe"; Parameters: "/u ""{app}\OneNoteCopilot.AddIn.dll"""; StatusMsg: "正在注销 32 位 COM 组件..."; Flags: runhidden waituntilterminated skipifdoesntexist

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

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
    MsgBox('需要先安装 .NET Framework 4.8 才能继续安装 OneNote Copilot。', mbCriticalError, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Microsoft\Office\OneNote\AddInsData\OneNoteCopilot.Connect');
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Microsoft\Office\Excel\AddIns\OneNoteCopilot.Connect');
  end;
end;
