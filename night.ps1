$ErrorActionPreference = "Stop"

$todayStr = (Get-Date).ToString("yyyy-MM-dd")

Write-Host "`n=== Football AI NIGHT ===" -ForegroundColor Cyan
Write-Host "Results/Backtest: $todayStr"

Write-Host "`n[1/2] Collecting results..." -ForegroundColor Yellow
dotnet run -- collect-results $todayStr
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n[2/2] Running backtest..." -ForegroundColor Yellow
dotnet run -- backtest $todayStr
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n=== NIGHT RUN COMPLETE ===" -ForegroundColor Green
