param(
    [Parameter(Mandatory = $true)]
    [string] $Iscc
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$compiler = (Resolve-Path -LiteralPath $Iscc).Path
$testRoot = Join-Path $projectRoot ('artifacts/upgrade-shutdown-test-' + [guid]::NewGuid().ToString('N'))
$fixtureRoot = Join-Path $testRoot 'fixture'
$payloadRoot = Join-Path $testRoot 'payload'
$targetRoot = Join-Path $testRoot 'target'
$otherRoot = Join-Path $testRoot 'other'
New-Item -ItemType Directory -Force -Path $fixtureRoot, $targetRoot, $otherRoot | Out-Null

# Only disposable fixture processes are started or killed. No real application,
# registry entries, user database or published installer is modified by this test.
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>CloudAlarmOverlay.App</AssemblyName>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $fixtureRoot 'Fixture.csproj') -Encoding UTF8
'System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);' |
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'Program.cs') -Encoding UTF8
& dotnet publish (Join-Path $fixtureRoot 'Fixture.csproj') -c Release -o $payloadRoot --self-contained false
if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
Copy-Item -Path (Join-Path $payloadRoot '*') -Destination $targetRoot
Copy-Item -Path (Join-Path $payloadRoot '*') -Destination $otherRoot

@'
#define AppExeName "CloudAlarmOverlay.App.exe"
[Setup]
AppName=Upgrade Shutdown Isolated Test
AppVersion=0.0.0
DefaultDirName={tmp}\CAO-UNUSED
PrivilegesRequired=lowest
Uninstallable=no
CreateAppDir=yes
CloseApplications=no
RestartApplications=no
OutputBaseFilename=NOT-FOR-DISTRIBUTION-shutdown-test
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion
[Code]
#include ShutdownScript
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := CloseInstalledApp(ExpandConstant('{app}\{#AppExeName}'));
end;
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'test.iss') -Encoding UTF8

& $compiler '/Q' "/O$testRoot" "/DPayloadDir=$payloadRoot" "/DShutdownScript=$projectRoot\installer\CloseInstalledApp.iss" (Join-Path $testRoot 'test.iss')
if ($LASTEXITCODE -ne 0) { throw 'Fixture installer compilation failed.' }

$targetProcess = $null
$otherProcess = $null
try {
    $targetProcess = Start-Process -FilePath (Join-Path $targetRoot 'CloudAlarmOverlay.App.exe') -WindowStyle Hidden -PassThru
    $otherProcess = Start-Process -FilePath (Join-Path $otherRoot 'CloudAlarmOverlay.App.exe') -WindowStyle Hidden -PassThru
    if ($targetProcess.HasExited -or $otherProcess.HasExited) { throw 'Fixture exited before the test.' }
    foreach ($case in @('running', 'already-closed')) {
        $logPath = Join-Path $testRoot "$case.log"
        $setup = Start-Process -FilePath (Join-Path $testRoot 'NOT-FOR-DISTRIBUTION-shutdown-test.exe') -WindowStyle Hidden -PassThru -ArgumentList @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
            ('/DIR="{0}"' -f $targetRoot), ('/LOG="{0}"' -f $logPath)
        )
        if (-not $setup.WaitForExit(30000)) {
            $setup.Kill()
            throw "Installer timed out: $case"
        }
        if ($setup.ExitCode -ne 0) { throw "Installer failed: $case (see $logPath)" }
        $targetProcess.Refresh()
        $otherProcess.Refresh()
        if (-not $targetProcess.HasExited) { throw 'Target process was not terminated.' }
        if ($otherProcess.HasExited) { throw 'Same-name process in another directory was terminated.' }
        if ($case -eq 'running' -and -not (Select-String -LiteralPath $logPath -SimpleMatch 'Upgrade: terminating installed application')) {
            throw 'Missing termination log.'
        }
        Write-Output "PASS: $case; target closed, other-directory process preserved."
    }
}
finally {
    foreach ($fixtureProcess in @($targetProcess, $otherProcess)) {
        if ($null -ne $fixtureProcess) {
            if (-not $fixtureProcess.HasExited) { $fixtureProcess.Kill(); $fixtureProcess.WaitForExit() }
            $fixtureProcess.Dispose()
        }
    }
}
Write-Output "Test logs: $testRoot"
