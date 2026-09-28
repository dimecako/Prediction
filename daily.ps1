$ErrorActionPreference = "Stop"

$todayStr = (Get-Date).ToString("yyyy-MM-dd")
$tomorrowStr = (Get-Date).AddDays(1).ToString("yyyy-MM-dd")

Write-Host "`n=== Football AI DAILY ===" -ForegroundColor Cyan
Write-Host "Results/Backtest: $todayStr"
Write-Host "Predictions:      $tomorrowStr"

Write-Host "`n[1/3] Collecting results..." -ForegroundColor Yellow
dotnet run -- collect-results $todayStr
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n[2/3] Running backtest..." -ForegroundColor Yellow
dotnet run -- backtest $todayStr
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n[3/3] Collecting tomorrow predictions..." -ForegroundColor Yellow
dotnet run -- $tomorrowStr
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n=== DAILY RUN COMPLETE ===" -ForegroundColor Green
