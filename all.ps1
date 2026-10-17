# Upotreba:
#   .\all.ps1                          (denes, backtest od 2026-09-22)
#   .\all.ps1 -Date 2026-10-17
#   .\all.ps1 -Date 2026-10-17 -BacktestFrom 2026-09-22
param(
    [string]$Date = (Get-Date).ToString("yyyy-MM-dd"),
    [string]$BacktestFrom = "2026-09-22"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$steps = @(
    @{ Name = "collect-results";     Args = @("collect-results", $Date) },       # vistinski rezultati vo baza
    @{ Name = "db-status";           Args = @("db-status", $Date) },             # sto ima vo bazata za toj den
    @{ Name = "backtest";            Args = @("backtest", $BacktestFrom) },      # tocnost po izvor, Equal vs Weighted
    @{ Name = "weighted-consensus";  Args = @("weighted-consensus", $Date) },    # glasanje so tezini
    @{ Name = "calibration";         Args = @("calibration") }                   # tocnost po Prob. grupa
)

$failed = @()
$i = 0
foreach ($step in $steps) {
    $i++
    Write-Host "`n[$i/$($steps.Count)] $($step.Name)" -ForegroundColor Yellow
    dotnet run --no-build -- @($step.Args)
    if ($LASTEXITCODE -ne 0) {
        Write-Host "GRESKA vo '$($step.Name)' (kod $LASTEXITCODE), prodolzuvam..." -ForegroundColor Red
        $failed += $step.Name
    }
}

if ($failed.Count -eq 0) {
    Write-Host "`n=== SITE KOMANDI ZAVRSIJA ===" -ForegroundColor Green
} else {
    Write-Host "`n=== Neuspesni: $($failed -join ', ') ===" -ForegroundColor Red
    exit 1
}
