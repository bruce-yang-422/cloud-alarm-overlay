param([string]$Version = "1.0.0")
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Version must be numeric, e.g. 1.0.0' }
$projectRoot = Split-Path $PSScriptRoot -Parent
dotnet publish (Join-Path $projectRoot 'src/CloudAlarmOverlay.App/CloudAlarmOverlay.App.csproj') -c Release -r win-x64 --self-contained true -p:Version=$Version -p:BaseOutputPath="$projectRoot/artifacts/publish-build/" -o (Join-Path $projectRoot 'artifacts/publish')
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Write-Output "Publish complete. Compile installer/CloudAlarmOverlay.iss with /DAppVersion=$Version using Inno Setup 6."
