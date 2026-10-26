$ErrorActionPreference = "Stop"
$sandbox = Join-Path ([System.IO.Path]::GetTempPath()) ("prediction-tickets-test-" + [guid]::NewGuid().ToString("N"))
$originalLocation = Get-Location
$names = @(
    "tickets.ps1",
    "predictions-2026-10-26.xlsx",
    "daily-ticket-2026-10-26-15.xlsx",
    "ticket-AI-2026-10-26-15-safe-sim-19.xlsx",
    "predictions-2026-10-27.xlsx",
    "predictions-2026-10-25.xlsx",
    "personal-2026-10-26.xlsx",
    "predictions-2026-10-260.xlsx"
)
New-Item -ItemType Directory -Path $sandbox | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "..\tickets.ps1") -Destination (Join-Path $sandbox "tickets.ps1")
    $runner = Join-Path $sandbox "tickets.ps1"
    $global:TicketScriptTest = @{
        Clock = [datetime]"2026-10-26T18:00:00"
        Calls = [System.Collections.Generic.List[string]]::new()
        Fail = ""
    }
    function Get-Date { $global:TicketScriptTest.Clock }
    function dotnet {
        $line = $args -join " "
        $global:TicketScriptTest.Calls.Add($line)
        $global:LASTEXITCODE = 0
        if ($global:TicketScriptTest.Fail -and $line -like $global:TicketScriptTest.Fail) {
            $global:LASTEXITCODE = 1
        }
    }
    function Assert-True($condition, $message) {
        if (-not $condition) { throw $message }
    }
    $cases = @(
        @("2026-10-26T00:00:00", "2026-10-26"),
        @("2026-10-26T17:59:59", "2026-10-26"),
        @("2026-10-26T18:00:00", "2026-10-27"),
        @("2026-10-26T23:59:59", "2026-10-27"),
        @("2026-10-27T00:00:00", "2026-10-27"),
        @("2026-12-31T23:59:59", "2027-01-01")
    )
    foreach ($case in $cases) {
        $global:TicketScriptTest.Clock = [datetime]$case[0]
        $text = (& $runner -PlanOnly 6>&1 | Out-String)
        Assert-True ($text -match "TICKETS: $($case[1])") "Incorrect target at $($case[0])."
        $variants = [regex]::Matches($text,
            "(?m)^dotnet run --no-build -- (?:ticket |ai-rank |daily-ticket |daily-ticket-league |ticket-1-3p3\+ )").Count
        Assert-True ($variants -eq 9) "Expected exactly nine ticket variants."
    }
    Assert-True ($global:TicketScriptTest.Calls.Count -eq 0) "PlanOnly invoked dotnet."
    $text = (& $runner -PlanOnly -Date 2030-01-02 -Count 5 -SkipCollection -SkipCleanup 6>&1 | Out-String)
    Assert-True ($text -match "TICKETS: 2030-01-02") "Explicit date was ignored."
    Assert-True ($text -match "daily-ticket 2030-01-02 5") "Count was ignored."
    Assert-True ($text -notmatch "(?m)^dotnet run --no-build -- 2030-01-02") "SkipCollection was ignored."
    $invalidFailed = $false
    try { & $runner -Date "invalid" -PlanOnly | Out-Null } catch { $invalidFailed = $true }
    Assert-True $invalidFailed "Invalid date must fail."

    foreach ($name in $names | Select-Object -Skip 1) {
        New-Item -ItemType File -Path (Join-Path $sandbox $name) | Out-Null
    }
    & $runner -Date 2026-10-27 6>&1 | Out-Null
    foreach ($name in $names[1..3]) {
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $sandbox $name))) "Old generated file was not deleted: $name"
    }
    foreach ($name in $names[4..7]) {
        Assert-True (Test-Path -LiteralPath (Join-Path $sandbox $name)) "Unrelated file was deleted: $name"
    }
    $calls = $global:TicketScriptTest.Calls
    Assert-True ($calls[0] -eq "build") "Build must run first."
    Assert-True ($calls[1] -eq "run --no-build -- ticket-import 2026-10-26") "Archive must precede generation."
    Assert-True ($calls[$calls.Count - 1] -eq "run --no-build -- ticket-import 2026-10-26") "Reverify before cleanup. Last call: $($calls[$calls.Count - 1])"
    Assert-True (($calls | Where-Object { $_ -eq "run --no-build -- ticket-report 2026-10-26 2026-10-26" }).Count -eq 1) "Previous tickets were not graded."

    foreach ($failure in @("*ai-rank 2026-10-27 15 safe", "*ticket-import 2026-10-26", "build")) {
        New-Item -ItemType File -Path (Join-Path $sandbox $names[1]) -Force | Out-Null
        $global:TicketScriptTest.Calls.Clear()
        $global:TicketScriptTest.Fail = $failure
        $didFail = $false
        try { & $runner -Date 2026-10-27 6>&1 | Out-Null } catch { $didFail = $true }
        Assert-True $didFail "Command failure did not fail the script: $failure"
        Assert-True (Test-Path -LiteralPath (Join-Path $sandbox $names[1])) "Cleanup ran despite failure: $failure"
    }
    $global:TicketScriptTest.Fail = ""
    & $runner -Date 2026-10-27 -SkipCleanup 6>&1 | Out-Null
    Assert-True (Test-Path -LiteralPath (Join-Path $sandbox $names[1])) "SkipCleanup was ignored."
    Write-Output "PASS: date boundaries, nine variants, explicit date/count, plan-only, grading order, exact-date cleanup, failure preservation and skip switches."
}
finally {
    Set-Location $originalLocation
    Remove-Item Function:\Get-Date,Function:\dotnet -ErrorAction SilentlyContinue
    Remove-Variable TicketScriptTest -Scope Global -ErrorAction SilentlyContinue
    foreach ($name in $names) {
        $path = Join-Path $sandbox $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    Remove-Item -LiteralPath $sandbox
}
