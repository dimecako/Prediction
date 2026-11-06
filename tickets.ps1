# .\tickets.ps1                    # 00:00-17:59 today; 18:00-23:59 tomorrow (local time)
# .\tickets.ps1 -Date 2026-10-27    # explicit target date
# .\tickets.ps1 -PlanOnly          # show commands without build or collection
param(
    [string]$Date = "",
    [ValidateRange(1, 15)]
    [int]$Count = 15,
    [switch]$SkipCollection,
    [switch]$SkipCleanup, # Retained for compatibility; Excel files are never deleted.
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
    @{ Name = "Target ticket report"; Args = @("ticket-report", $Date, $Date) }
)
Write-Host "`n=== TICKETS: $Date | Previous: $previous | Local clock: $now ===" -ForegroundColor Cyan
Write-Host "SIMULATIONS ONLY. Each ticket uses 1 unit total; not actual payments."
if ($PlanOnly) {
    Write-Host "dotnet build"
    foreach ($step in $steps) { Write-Host ("dotnet run --no-build -- " + ($step.Args -join " ")) }
    return
}
dotnet build
if ($LASTEXITCODE -ne 0) { throw "Build failed. No commands executed." }
$failed = @()
foreach ($step in $steps) {
    Write-Host "`n=== $($step.Name) ===" -ForegroundColor Yellow
    $commandArgs = @("run", "--no-build", "--") + $step.Args
    & dotnet @commandArgs
    if ($LASTEXITCODE -ne 0) {
        $failed += $step.Name
        Write-Host "FAILED: $($step.Name)." -ForegroundColor Red
        if ($step.Args[0] -eq "ticket-import") {
            throw "Archive failed. Existing Excel files have been preserved; no ticket generation."
        }
    }
}
if ($failed.Count -gt 0) {
    throw "Failed steps: $($failed -join ', '). Excel files have NOT been deleted."
}
Write-Host "`n=== TICKETS COMPLETE: $Date ===" -ForegroundColor Green
