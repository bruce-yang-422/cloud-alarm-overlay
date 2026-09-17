param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [Parameter(Mandatory = $true)]
    [string] $Version,
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
$manifestPath = Join-Path $projectRoot 'version.json'
$releasedVersionText = (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).latestVersion
if ($releasedVersionText -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw 'version.json 的 latestVersion 格式錯誤。'
}
if ([version]$Version -le [version]$releasedVersionText) {
    throw "版本 $Version 不高於已發布的 $releasedVersionText。請使用新版本號，勿覆蓋已安裝的版本。"
}

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
$requestedVersion = [version]$Version
$expectedFileVersion = [version]::new(
    $requestedVersion.Major, $requestedVersion.Minor,
    $requestedVersion.Build, [Math]::Max(0, $requestedVersion.Revision))
$actualFileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($publishedExecutable).FileVersion
if ([version]$actualFileVersion -ne $expectedFileVersion) {
    throw "發布程式版本 $actualFileVersion 與指定的 $Version 不符；請重新發布，勿用 -SkipPublish 封裝舊版本。"
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
