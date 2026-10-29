# .\collect-results-range.ps1 -From 2026-10-21 -To 2026-10-28
param(
    [Parameter(Mandatory = $true)]
    [string]$From,
    [Parameter(Mandatory = $true)]
    [string]$To
)

$ErrorActionPreference = "Stop"
$start = [datetime]::MinValue
$end = [datetime]::MinValue
if (-not [datetime]::TryParseExact($From, "yyyy-MM-dd",
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::None, [ref]$start) -or
    -not [datetime]::TryParseExact($To, "yyyy-MM-dd",
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::None, [ref]$end) -or
    $start -gt $end) {
    throw "Koristi -From yyyy-MM-dd -To yyyy-MM-dd; From mora da bide pred ili ednakov na To."
}

$originalLocation = Get-Location
try {
    Set-Location $PSScriptRoot
    $failedDates = @()
    for ($date = $start; $date -le $end; $date = $date.AddDays(1)) {
        $day = $date.ToString("yyyy-MM-dd")
        Write-Host "`n=== Collect results: $day ==="
        dotnet run --no-build -- collect-results $day
        if ($LASTEXITCODE -ne 0) {
            $failedDates += $day
            Write-Warning "Problemi pri sobiranje za $day. Proveri go izlezot pogore; prodolzuvam."
        }
    }

    dotnet run --no-build -- ticket-report $From $To
    $reportFailed = $LASTEXITCODE -ne 0
    if ($failedDates.Count -gt 0) {
        Write-Warning "Datumi za proverka/povtoruvanje: $($failedDates -join ', ')."
    }
    if ($reportFailed -or $failedDates.Count -gt 0) {
        throw "Opsegot e obraboten so greski. Ticket report neuspesen: $reportFailed. Proveri go izlezot pogore."
    }
    Write-Host "`n=== Collect results complete: $From - $To ==="
}
finally {
    Set-Location $originalLocation
}
