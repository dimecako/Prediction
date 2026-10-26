# .\tickets.ps1                    # 00:00-17:59 today; 18:00-23:59 tomorrow (local time)
# .\tickets.ps1 -Date 2026-10-27    # explicit target date
# .\tickets.ps1 -PlanOnly          # show commands without build, collection or deletion
param(
    [string]$Date = "",
    [ValidateRange(1, 100)]
    [int]$Count = 15,
    [switch]$SkipCollection,
    [switch]$SkipCleanup,
    [switch]$PlanOnly
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$now = Get-Date
if ([string]::IsNullOrWhiteSpace($Date)) {
    $target = $now.Date
    if ($now.Hour -ge 18) { $target = $target.AddDays(1) }
    $Date = $target.ToString("yyyy-MM-dd")
}
$target = [datetime]::MinValue
if (-not [datetime]::TryParseExact($Date, "yyyy-MM-dd",
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::None, [ref]$target)) {
    throw "Koristi -Date yyyy-MM-dd."
}
$previous = $target.AddDays(-1).ToString("yyyy-MM-dd")
$steps = @(
    @{ Name = "Archive previous Excel tickets"; Args = @("ticket-import", $previous) },
    @{ Name = "Archive existing target Excel tickets"; Args = @("ticket-import", $Date) },
    @{ Name = "Collect previous results"; Args = @("collect-results", $previous) },
    @{ Name = "Grade previous tickets"; Args = @("ticket-report", $previous, $previous) }
)
if (-not $SkipCollection) {
    $steps += @{ Name = "Collect target predictions"; Args = @($Date) }
}
$steps += @(
    @{ Name = "ticket"; Args = @("ticket", $Date, "$Count") },
    @{ Name = "ticket safe"; Args = @("ticket", $Date, "$Count", "safe") },
    @{ Name = "ai-rank"; Args = @("ai-rank", $Date, "$Count") },
    @{ Name = "ai-rank safe"; Args = @("ai-rank", $Date, "$Count", "safe") },
    @{ Name = "ai-rank safe league"; Args = @("ai-rank", $Date, "$Count", "safe", "league") },
    @{ Name = "daily-ticket"; Args = @("daily-ticket", $Date, "$Count") },
    @{ Name = "daily-ticket-league"; Args = @("daily-ticket-league", $Date, "$Count") },
    @{ Name = "ticket-1-3p3+"; Args = @("ticket-1-3p3+", $Date, "$Count") },
    @{ Name = "ticket-1-3p3+ safe"; Args = @("ticket-1-3p3+", $Date, "$Count", "safe") },
    @{ Name = "Target ticket report"; Args = @("ticket-report", $Date, $Date) }
)
Write-Host "`n=== TICKETS: $Date | Previous: $previous | Local clock: $now ===" -ForegroundColor Cyan
Write-Host "SIMULATIONS ONLY. Each ticket uses 1 unit total; not actual payments."
if ($PlanOnly) {
    Write-Host "dotnet build"
    foreach ($step in $steps) { Write-Host ("dotnet run --no-build -- " + ($step.Args -join " ")) }
    if (-not $SkipCleanup) {
        Write-Host "After ALL steps succeed: verify archive again, then delete generated XLSX for $previous only (including predictions)."
    }
    return
}
dotnet build
if ($LASTEXITCODE -ne 0) { throw "Build failed. No commands or cleanup executed." }
$failed = @()
foreach ($step in $steps) {
    Write-Host "`n=== $($step.Name) ===" -ForegroundColor Yellow
    $commandArgs = @("run", "--no-build", "--") + $step.Args
    & dotnet @commandArgs
    if ($LASTEXITCODE -ne 0) {
        $failed += $step.Name
        Write-Host "FAILED: $($step.Name). Cleanup disabled." -ForegroundColor Red
        if ($step.Args[0] -eq "ticket-import") {
            throw "Archive failed. Existing Excel files have been preserved; no ticket generation or cleanup."
        }
    }
}
if ($failed.Count -gt 0) {
    throw "Failed steps: $($failed -join ', '). Excel files have NOT been deleted."
}
if (-not $SkipCleanup) {
    # Recheck immediately before deletion; old daily selections must survive in the database.
    $verifyArgs = @("run", "--no-build", "--", "ticket-import", $previous)
    & dotnet @verifyArgs
    if ($LASTEXITCODE -ne 0) { throw "Final archive verification failed. No Excel files deleted." }
    $dateToken = [regex]::Escape($previous)
    $pattern = "^(predictions|daily-ticket(?:-league)?|ticket(?:-[A-Za-z0-9+]+)*)-$dateToken(?:-.*)?\.xlsx$"
    $files = @(Get-ChildItem -LiteralPath $PSScriptRoot -File |
        Where-Object { $_.Name -cmatch $pattern })
    foreach ($file in $files) {
        Remove-Item -LiteralPath $file.FullName -ErrorAction Stop
        Write-Host "[CLEANUP] Deleted $($file.Name); database history preserved."
    }
}
Write-Host "`n=== TICKETS COMPLETE: $Date ===" -ForegroundColor Green
