# Daily automation deployment

The app remains deployed from `prediction-api`. Deploy the completed application
changes there and copy `.github/workflows/daily-run.yml` to the repository's
default branch **main**. GitHub only schedules workflows present on the default
branch. No default-branch change is required: this workflow explicitly checks out
`prediction-api`, including `Deployment/daily_schedule.py`. Until both branches
contain the required files, scheduling is not deployed.

Create repository Actions secret `FOOTBALL_DB_CONNECTION` containing the existing
Neon **Npgsql connection string**, for example `Host=...;Database=...;Username=...;Password=...;SSL Mode=Require`.
Do not paste a PostgreSQL URI or commit credentials. The database must already
contain the current migrations. This job never migrates, imports Excel, rebuilds
at runtime, or runs a separate ML training command; `ai-rank` trains from history.

The first attempt is at **08:00 Europe/Skopje**, with up to three additional
attempts at **09:00, 10:00 and 11:00** if no attempt has succeeded for that date.
Five UTC schedules (06:00 through 10:00) cover both seasonal offsets; the guard
selects only the four local hours. It compares `github.event.schedule` with the
planned offset, not the runner's current hour, so same-day queue delays still
execute the intended occurrence. GitHub can delay, drop or coalesce queued cron
jobs; these are retry opportunities, not a guaranteed exact-time scheduler.

After a completely successful execution, the workflow uploads
`daily-success-yyyy-MM-dd` with 90-day retention. Before building or scraping,
each attempt checks the exact target date's artifacts and verifies that the
producing run completed successfully in this workflow. A successful manual run
also stops automatic attempts for that date; manually selecting an already
successful date skips the work too, regardless of the requested ticket count.
Inactive cron occurrences and skipped jobs do not upload success markers.
GitHub API errors fail the guard explicitly rather than risking duplicate work.
Do not delete the marker if you want to retain the once-per-date guard.
Older runs made before this marker mechanism was deployed are not backfilled.

After a dropped run or three failed retries, use **Actions → Daily football
automation → Run workflow**, optionally specifying a target date and count.
The target date is frozen by the guard and reused by execution and the marker,
even if the job subsequently crosses midnight. A queue delay crossing midnight
before the guard starts uses the new local date; dispatch the intended date
manually to recover it. Dates outside artifact retention can be executed again.

Execution is bounded by job/step timeouts. Actions serializes runs without
cancelling an active one; a shared Postgres transaction advisory lock also
prevents overlapping `daily-run` processes, including from another host. Lock
contention fails explicitly for retry. The lock supports direct and pooled
connections, holds a dedicated transaction (one backend for the duration), and
heartbeats every 20 seconds. Lost lock connections stop further steps. Avoid
overlapping manual/local ticket scripts: those legacy individual commands do
not acquire the automation lock.

Results are always collected for the day before the target date, even if there
are no pending tickets or results were already collected locally. One additional
day back is checked only when that date has **any Pending selection**, even when
another leg has already lost the accumulator. No earlier dates are scanned;
older pending history requires a separate manual `collect-results` command.
For example, a November 2 run collects November 1 results and optionally
October 31 results, never October 30 or earlier. Manual target dates use the same
two-day window relative to the supplied date. Missing scores and required
half-time scores remain Pending; confirmed scores are not overwritten.
Failures are reported per step, remaining steps continue, and any failed step
makes the job fail. No usable selections or an empty prediction source is
explicitly reported as a failure rather than fabricated success. Inspect job
logs before retrying; reruns preserve ledger fingerprint deduplication, but
changed odds/selections intentionally create another simulation.

Only verified regulation-time FT (90 minutes plus stoppage time) is eligible
for persistence and grading. Forebet requires `FT` without extra-time or
shootout evidence; SoccerVista requires its verified regulation-finished detail;
OpenFootball uses only the explicit `score.ft`, never `et`, `p` or an unlabelled
score array. FootballResultsOnline's unlabelled scores cannot establish the
period and are rejected by collection. AET/penalty fixtures without verified
regulation scores stay unresolved with a diagnostic; half-time is not a
substitute for regulation FT. Providers must explicitly set
`IsRegulationTimeVerified` after checking their source evidence.

Disagreeing provider scores quarantine that fixture for the capture rather than
choosing the first, latest or larger score. Stored-score discrepancies are not
automatically corrected: diagnostics identify the stored source, capture time
and incoming source fixture ID for verification. A score difference alone does
not establish extra time, penalties or a wrong historical score. Existing
historical data must be verified separately; these guards do not certify or
rewrite it.

Prediction persistence verifies shared-source aliases even when the new display
name has no exact stored match. Verification includes stored FT/HT results, not
just names and source IDs. Ambiguous identities are skipped, independent valid
fixtures are saved, and the command still fails. Safe diagnostics report capture
row, conflict reason and numeric stored match IDs; unexpected exceptions report
their code origin, never raw exception messages or connection strings.

`Exports__Excel=false` disables Excel in prediction reporting and both ticket
commands while retaining database persistence and console reports. Unset (or
`true`) preserves local script behavior. Invalid values fail rather than
silently enabling export. FlareSolverr runs only on the runner loopback with a
fresh masked gateway key; no Render FlareSolverr secret is required.

Local equivalent after building (using your existing connection environment):

```powershell
$env:Exports__Excel = "false"
dotnet run --no-build -- daily-run 2026-11-01 15
```

The date defaults to Europe/Skopje today, irrespective of the host clock zone.
Set the host `TZ=Europe/Skopje` on Linux, as the workflow does, to retain the
existing bookmaker fixture date matching behavior.

Focused validation:

```powershell
dotnet test Tests\Prediction.Tests.csproj --filter "FullyQualifiedName~DailyAutomationTests"
python -B -m unittest discover -s Tests -p test_daily_schedule.py
```

The advisory-lock test is opt-in with `PREDICTION_RUN_DB_TESTS=1` and the existing
connection variable; it uses transactions and SELECT only, without migrations or
data writes.
