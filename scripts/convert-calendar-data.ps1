param([string]$Version = '2026-2027.1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputFolder = Join-Path $projectRoot 'data/calendar'
New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
$lunarRows = @(Import-Csv -LiteralPath (Join-Path $projectRoot 'Sheet範例/SheetA_LunarCalendar.csv') -Encoding utf8 | Select-Object -Skip 1 | ForEach-Object {
    [ordered]@{ date=$_.Date; lunarDate=$_.LunarDate; lunarDay=[int]$_.LunarDay; solarTerm=$_.SolarTerm }
})
$holidayRows = @(Import-Csv -LiteralPath (Join-Path $projectRoot 'Sheet範例/SheetA_Holidays.csv') -Encoding utf8 | Select-Object -Skip 1 | ForEach-Object {
    [ordered]@{ date=$_.Date; type=$_.Type; note=$_.Note }
})
$from = $lunarRows[0].date
$to = $lunarRows[-1].date
foreach ($kind in @('LunarCalendar','Holidays')) {
    $rows = if ($kind -eq 'LunarCalendar') { $lunarRows } else { $holidayRows }
    $document = [ordered]@{ schemaVersion=1; kind=$kind; version=$Version; from=$from; to=$to; entries=@($rows) }
    $target = Join-Path $outputFolder "SheetA_$kind.json"
    [IO.File]::WriteAllText($target, ($document | ConvertTo-Json -Depth 6) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Output "$target : $($rows.Count) 筆，$from ～ $to"
}
