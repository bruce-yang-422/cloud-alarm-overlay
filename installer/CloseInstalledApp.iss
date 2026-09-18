// Included in [Code]. Never terminate by image name alone: verify the exact
// installation path on the same process handle used for termination, and only
// consider the installer's Windows session. No taskkill /IM or child-process kill.
function UpgradeOpenProcess(Access: LongWord; InheritHandle: Boolean; ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function UpgradeQueryImage(Process: THandle; Flags: LongWord; Name: String; var Size: LongWord): Boolean;
  external 'QueryFullProcessImageNameW@kernel32.dll stdcall';
function UpgradeTerminateProcess(Process: THandle; ExitCode: LongWord): Boolean;
  external 'TerminateProcess@kernel32.dll stdcall';
function UpgradeWait(Process: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function UpgradeCloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function UpgradeProcessId: LongWord;
  external 'GetCurrentProcessId@kernel32.dll stdcall';
function UpgradeSessionId(ProcessId: LongWord; var SessionId: LongWord): Boolean;
  external 'ProcessIdToSessionId@kernel32.dll stdcall';

function CloseInstalledApp(const Executable: String): String;
var
  Locator, Service, Processes, Candidate: Variant;
  I: Integer;
  SessionId, ProcessId, NameLength, ErrorCode: LongWord;
  Process: THandle;
  ImagePath: String;
begin
  Result := '';
  try
    if not UpgradeSessionId(UpgradeProcessId, SessionId) then
      RaiseException('無法確認安裝程式的 Windows 工作階段。');
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Service.ExecQuery(
      'SELECT ProcessId FROM Win32_Process WHERE Name = ''{#AppExeName}'' AND SessionId = ' + IntToStr(SessionId));
    for I := 0 to Processes.Count - 1 do
    begin
      Candidate := Processes.ItemIndex(I);
      ProcessId := Candidate.ProcessId;
      { SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE }
      Process := UpgradeOpenProcess($00101001, False, ProcessId);
      if Process = 0 then
      begin
        ErrorCode := DLLGetLastError;
        { ERROR_INVALID_PARAMETER: the process exited before OpenProcess. }
        if ErrorCode <> 87 then
          RaiseException(Format('無法存取舊版程式（PID %d，錯誤 %d）。', [ProcessId, ErrorCode]));
      end
      else
      begin
        try
          if UpgradeWait(Process, 0) <> 0 then
          begin
            NameLength := 32768;
            ImagePath := StringOfChar(#0, NameLength);
            if not UpgradeQueryImage(Process, 0, ImagePath, NameLength) then
            begin
              if UpgradeWait(Process, 0) <> 0 then
                RaiseException('無法確認舊版程式的完整路徑。');
            end
            else
            begin
              SetLength(ImagePath, NameLength);
              if CompareText(ImagePath, Executable) = 0 then
              begin
                Log(Format('Upgrade: terminating installed application PID %d: %s', [ProcessId, ImagePath]));
                if not UpgradeTerminateProcess(Process, 0) then
                begin
                  if UpgradeWait(Process, 0) <> 0 then
                    RaiseException('無法關閉舊版程式，請聯絡管理者協助。');
                end;
                if UpgradeWait(Process, 10000) <> 0 then
                  RaiseException('等待舊版程式結束逾時，請重試安裝。');
              end;
            end;
          end;
        finally
          UpgradeCloseHandle(Process);
        end;
      end;
    end;
  except
    Result := '安裝前關閉舊版失敗：' + GetExceptionMessage;
    Log(Result);
  end;
end;
