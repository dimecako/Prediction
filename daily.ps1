$ErrorActionPreference = "Stop"

$todayStr = (Get-Date).ToString("yyyy-MM-dd")

Write-Host "`n=== Football AI MORNING PREDICTIONS ===" -ForegroundColor Cyan
Write-Host "Predictions: $todayStr"

Write-Host "`n[1/1] Collecting today predictions..." -ForegroundColor Yellow
dotnet run -- $todayStr
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n=== MORNING PREDICTIONS COMPLETE ===" -ForegroundColor Green
