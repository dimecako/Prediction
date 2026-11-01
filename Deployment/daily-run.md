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

Two UTC schedules (06:00 and 07:00) cover 08:00 Europe/Skopje year-round.
The guard compares `github.event.schedule` with the offset at local 08:00, not
the runner's current hour: same-day queue delays still execute the intended
occurrence. GitHub can delay or drop cron jobs; it is not a guaranteed exact-time
scheduler. After a dropped run use **Actions → Daily football automation → Run
workflow**, optionally specifying a target date and count. A delay crossing a
local midnight uses the new local date; use manual dispatch to recover that date.

Execution is bounded by job/step timeouts. Actions serializes runs without
cancelling an active one; a shared Postgres transaction advisory lock also
prevents overlapping `daily-run` processes, including from another host. Lock
contention fails explicitly for retry. The lock supports direct and pooled
connections, holds a dedicated transaction (one backend for the duration), and
heartbeats every 20 seconds. Lost lock connections stop further steps. Avoid
overlapping manual/local ticket scripts: those legacy individual commands do
not acquire the automation lock.

All past ticket dates with **any Pending selection** are rechecked, even when
another leg has already lost the accumulator. Missing scores and required
half-time scores remain Pending; confirmed scores are not overwritten.
Failures are reported per step, remaining steps continue, and any failed step
makes the job fail. No usable selections or an empty prediction source is
explicitly reported as a failure rather than fabricated success. Inspect job
logs before retrying; reruns preserve ledger fingerprint deduplication, but
changed odds/selections intentionally create another simulation.

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
