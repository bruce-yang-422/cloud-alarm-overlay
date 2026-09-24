param([string]$Version = "1.3.0")
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Version must be numeric, e.g. 1.0.0' }
$projectRoot = Split-Path $PSScriptRoot -Parent
# Restore the explicit RID first so WPF temporary projects reuse the correct assets.
dotnet restore (Join-Path $projectRoot 'src/CloudAlarmOverlay.App/CloudAlarmOverlay.App.csproj') -r win-x64 -p:BaseOutputPath="$projectRoot/artifacts/publish-build/"
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet publish --no-restore (Join-Path $projectRoot 'src/CloudAlarmOverlay.App/CloudAlarmOverlay.App.csproj') -c Release -r win-x64 --self-contained true -p:Version=$Version -p:BaseOutputPath="$projectRoot/artifacts/publish-build/" -o (Join-Path $projectRoot 'artifacts/publish')
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Write-Output "Publish complete. Compile installer/CloudAlarmOverlay.iss with /DAppVersion=$Version using Inno Setup 6."
