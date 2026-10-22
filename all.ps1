# Upotreba:
#   .\all.ps1                          (build + rezultati/analizi za denes)
#   .\all.ps1 -Date 2026-10-17
#   .\all.ps1 -Date 2026-10-17 -BacktestFrom 2026-09-22
param(
    [string]$Date = (Get-Date).ToString("yyyy-MM-dd"),
    [string]$BacktestFrom = "2026-09-22"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$parsedDate = [datetime]::MinValue
$parsedFrom = [datetime]::MinValue
if (-not [datetime]::TryParseExact($Date, "yyyy-MM-dd",
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::None, [ref]$parsedDate) -or
    -not [datetime]::TryParseExact($BacktestFrom, "yyyy-MM-dd",
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::None, [ref]$parsedFrom) -or
    $parsedFrom -gt $parsedDate) {
    throw "Koristi yyyy-MM-dd; BacktestFrom mora da bide pred ili ednakov na Date."
}

Write-Host "`n=== Football AI: rezultati i analizi ===" -ForegroundColor Cyan
Write-Host "Datum: $Date | Istorija od: $BacktestFrom"
Write-Host "ROI: site zacuvani simulacii; NE se vistinski uplati. Ne se generira nov tiket."
Write-Host "`n[BUILD] Kompajliranje..." -ForegroundColor Yellow
dotnet build
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build ne uspea; komandite nema da se pustat so star kod." -ForegroundColor Red
    exit $LASTEXITCODE
}

$steps = @(
    @{ Name = "collect-results";     Args = @("collect-results", $Date) },       # vistinski rezultati vo baza
    @{ Name = "db-status";           Args = @("db-status", $Date) },             # sto ima vo bazata za toj den
    @{ Name = "backtest";            Args = @("backtest", $BacktestFrom) },      # tocnost po izvor, Equal vs Weighted
    @{ Name = "weighted-consensus";  Args = @("weighted-consensus", $Date) },    # glasanje so tezini
    @{ Name = "calibration";         Args = @("calibration") },                  # tocnost po Prob. grupa
    @{ Name = "ticket-report";       Args = @("ticket-report") },              # site simulacii, profit/ROI i pending
    @{ Name = "league-stats";        Args = @("league-stats", $BacktestFrom, $Date) } # tocnost po liga vo opsegot
)

$failed = @()
$i = 0
foreach ($step in $steps) {
    $i++
    Write-Host "`n[$i/$($steps.Count)] $($step.Name)" -ForegroundColor Yellow
    $commandArgs = @("run", "--no-build", "--") + $step.Args
    & dotnet @commandArgs
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
