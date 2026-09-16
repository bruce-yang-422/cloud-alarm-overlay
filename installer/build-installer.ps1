param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [string] $Version = '1.0.0',
    [string] $Iscc = '',
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw 'Version 必須是數字格式，例如 1.0.0。'
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishDirectory = Join-Path $projectRoot 'artifacts\publish'
$outputDirectory = Join-Path $projectRoot 'artifacts\installer'

if (-not $Iscc) {
    $command = Get-Command iscc -ErrorAction SilentlyContinue
    if ($command) { $Iscc = $command.Source }
}

if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc)) {
    $installRoots = @(
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)},
        (Join-Path $env:LOCALAPPDATA 'Programs')
    ) | Where-Object { $_ }

    foreach ($installRoot in $installRoots) {
        foreach ($majorVersion in @(7, 6)) {
            $candidate = Join-Path $installRoot "Inno Setup $majorVersion\ISCC.exe"
            if (Test-Path -LiteralPath $candidate) {
                $Iscc = $candidate
                break
            }
        }
        if ($Iscc) { break }
    }
}

if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc)) {
    throw '找不到 Inno Setup Compiler（ISCC.exe）。請安裝 Inno Setup 7/6，或使用 -Iscc 指定完整路徑。'
}

if (-not $SkipPublish) {
    & (Join-Path $projectRoot 'scripts\publish.ps1') -Version $Version
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失敗，退出碼：$LASTEXITCODE" }
}

$publishedExecutable = Join-Path $publishDirectory 'CloudAlarmOverlay.App.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw "找不到發布程式：$publishedExecutable"
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$installerScript = Join-Path $projectRoot 'installer\CloudAlarmOverlay.iss'
& $Iscc "/DPublishDir=$publishDirectory" "/DAppVersion=$Version" $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 編譯失敗，退出碼：$LASTEXITCODE" }

$installer = Join-Path $outputDirectory "CloudAlarmOverlay-v$Version-Setup-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "找不到輸出安裝包：$installer"
}

Write-Output "完成：$installer"
Write-Output (Get-FileHash -LiteralPath $installer -Algorithm SHA256 | Format-List | Out-String)
