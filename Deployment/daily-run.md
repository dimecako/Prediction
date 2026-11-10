# Daily automation deployment

The app remains deployed from `prediction-api`. Deploy the completed application
changes there and copy `.github/workflows/daily-run.yml` to the repository's
default branch **main**. GitHub only schedules workflows present on the default
branch. No default-branch change is required: this workflow explicitly checks out
`prediction-api`, including `Deployment/daily_schedule.py`. Until both branches
contain the required files, scheduling is not deployed.

Manual **Run workflow** executions from `main` also check out `prediction-api`,
because `main` contains only automation. Other selected branches check out their
exact revision. To test these changes before deploying, select
`copilot/ml-leagua-save-pending-results`, enter the target date and ticket count,
and launch the workflow. This is a real execution using `FOOTBALL_DB_CONNECTION`,
not a dry run: it shares that date's success marker, attempt budget and saved
tickets with production. Already-completed steps may be skipped. It does not
deploy the selected branch to Render.

Create repository Actions secret `FOOTBALL_DB_CONNECTION` containing the existing
Neon **Npgsql connection string**, for example `Host=...;Database=...;Username=...;Password=...;SSL Mode=Require`.
Do not paste a PostgreSQL URI or commit credentials. The database must already
contain the current migrations. This job never migrates, imports Excel, rebuilds
at runtime, or runs a separate ML training command; ticket commands may train
their in-memory ML ranker from prepared history.

Optionally set the repository Actions secret `OPENAI_API_KEY` to enable AI
suggestions for up to 20 result identity conflicts per collection. The model
receives only the incoming fixture identity and candidate fixture names,
competition, kickoff and provider IDs; it does not receive scores. Suggestions
are printed to the run logs for manual review and never change or settle records.
Without this secret, result collection behaves as before.

The first attempt is at **07:07 Europe/Skopje**, with up to two additional
attempts at **08:07 and 09:07 local time** if no attempt has succeeded for that
date. GitHub cron is UTC, so the workflow schedules 05:07 through 08:07 UTC and
the guard converts each scheduled occurrence to Skopje time, selecting only
07:07, 08:07 and 09:07. This covers both winter and summer time without shifting
the local start hour. The guard checks the scheduled occurrence, not the runner's
current hour, so same-day queue delays still execute the intended attempt.
GitHub can delay, drop or coalesce queued cron jobs; these are retry opportunities,
not a guaranteed exact-time scheduler.

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

Each `daily-run` step checkpoints its success to a local manifest, which the
workflow uploads as `daily-checkpoint-yyyy-MM-dd` even when the daily job
fails. The next retry downloads the newest completed checkpoint artifact for
that target date, including legacy count-suffixed artifacts. Completed steps and
the three-attempt budget survive revision or count changes. The workflow reserves
an attempt before setup/build; local CLI runs keep their manifest under
`.automation/daily-checkpoints`. Failed and deferred work is retried, not successful
work. Ticket commands are independent of collection success and use available
stored predictions, pre-date exact-market history and current odds; insufficient
data must not fabricate picks. The four generation modes are `daily`,
`daily-league`, `fixed-auto` and `ft-combos`, with at most 15 qualified picks each and no forced
filling. `1-3p3+` is retired; no new ticket can contain a half-time-dependent market.
Strict markets require exact-event history, source coverage and conservative-return
qualification; winner & FT goals retains its prior eligibility baseline pending
separate validation. `ticket-ft-combos` contains only winner & 2-5/2-6 goals
or winner scores 2+, never a straight win or BTTS. Raw AI output is not calibrated
confidence. See the README's `exact-market-v2` selection rules.

The job also runs `ticket-ft-shadow target-date count` and cumulative
`ticket-ft-shadow-report`. These are **paired simulations, not additional live
tickets**: baseline and strict challenger use the same frozen offers, prices and
as-of snapshots, with no active rule changes or automatic promotion. Apply the
additive `AddFtShadowRuns` migration **before** deploying this revision; a missing
`ft_shadow_runs` table is an explicit failure, not a skipped experiment. The
unique experiment/date index preserves the first valid capture, even with zero
qualified picks; retries do not refetch prices or revise it. New past-date
captures are prohibited, while existing captures remain reportable/reusable.
The cumulative report depends on shadow capture and all scheduled result
collection steps, and reports exact-type accuracy, coverage, Pending and singles
ROI. Thirty dates and 100 graded selections per arm/type are only a manual-review
floor, not evidence of improvement. The two-day collection window also applies
to shadow fixtures; collect older unresolved dates manually before reporting.

Reports still run against stored data when prerequisites fail, with an explicit
partial-data warning. A partial report is not checkpointed as complete; reports
refresh after their prerequisite work changes on a retry. Under the daily database lock, an
existing date/mode ticket prevents regeneration even if odds change or the
checkpoint is lost. No existing tickets are deleted.

The workflow publishes a separate reservation artifact before setup, so failed
builds and interrupted application steps still consume their attempt. Reruns also
use GitHub's run-attempt number as a minimum attempt count. Progress after the
reservation is available to another runner only after the final checkpoint upload;
cancellation can lose those later completed-step markers. Deleting or expiring
all artifacts can lose historical attempt counts across separate runs. The
database ticket check still prevents repeat ticket creation. Artifacts expire
after 90 days.

Use **Actions → Daily football automation → Run workflow** to request a manual
attempt, optionally specifying a target date and count. It shares the same
three-attempt budget; dispatching again does not reset it.
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
makes the job fail. A valid league-filter outcome with no eligible selections,
or a fully validated evaluation in any mode with no picks passing the quality
thresholds, is reported as `NO_SELECTION`, without saving an empty ticket. Missing source data,
invalid inputs and technical failures remain errors rather than fabricated success. Inspect job
logs before retrying. Daily automation reuses the first saved ticket for each
date/mode even when odds or selections change. Standalone ticket commands now
also reuse the first saved date/mode ticket before fetching new odds. This is
not an atomic concurrency guarantee for overlapping standalone commands; use the
daily automation lock for that.
No actionable 1/2 signal (including a draw or tied votes) is a normal abstention
when the matched future fixture has valid source votes and bookmaker prices.
Unmatched unrelated fixtures do not invalidate that evaluation. Missing source
votes, invalid probabilities/prices and missing league metadata remain errors.

Result resolution additionally accepts fresh provider evidence linking one exact
names/date row to one unique provider-ID row, only with a real UTC kickoff on that
Skopje date, conservative team aliases, compatible scores and no contradictory
IDs or real kickoffs. This saves results without merging or deleting match rows.
Date-only midnight values are not real kickoff evidence. Missing live evidence,
different squads, contradictory results and ambiguous links still fail explicitly.

Only verified regulation-time FT (90 minutes plus stoppage time) is eligible
for persistence and grading. Forebet requires `FT` without extra-time or
shootout evidence; SoccerVista requires its verified regulation-finished detail;
OpenFootball uses only the explicit `score.ft`, never `et`, `p` or an unlabelled
score array. FootballResultsOnline's unlabelled scores cannot establish the
period, so this provider is explicitly disabled in the default collection.
If supplied directly to the collector, its unverified rows are still rejected.
AET/penalty fixtures without verified
regulation scores stay unresolved with a diagnostic; half-time is not a
substitute for regulation FT. Providers must explicitly set
`IsRegulationTimeVerified` after checking their source evidence.

Forebet remains the primary result source. SoccerVista fallback targets fixtures
missing FT or either half-time score. Its current provider verifies FT but does
not yet extract HT. Goal-timeline reconstruction is deliberately not enabled
until an actual provider response can be verified: attempts to fetch a real
fixture during this change failed DNS resolution. A future implementation must
identify the scoring side (including own goals), exclude disallowed goals,
include first-half stoppage time, distinguish periods, and reconcile all valid
regulation goals against verified FT. Incomplete or ambiguous evidence must
leave HT unknown, never assume 0-0.

Ticket settlement can mark half-dependent combinations lost without HT when
the verified FT disproves the required winner or minimum total. Possible wins
still need HT; calibration continues to require complete half-time evidence.

Disagreeing provider scores quarantine that fixture for the capture rather than
choosing the first, latest or larger score. Stored-score discrepancies are not
automatically corrected: diagnostics identify the stored source, capture time
and incoming source fixture ID for verification. A score difference alone does
not establish extra time, penalties or a wrong historical score. Existing
historical data must be verified separately; these guards do not certify or
rewrite it.

Identity-conflict logs include the incoming fixture and every exact-name or
source-linked database candidate, with date, UTC kickoff, source IDs and FT/HT.
Persistence identity/score conflict counts are separate from provider rejections.
Two rows may be linked only when shared provider evidence, compatible identities,
kickoffs and stored scores verify a duplicate; similar names alone are insufficient.
`merge-duplicate-matches` remains a dry-run by default, showing both kept and
removed rows with their evidence. Review its output before explicitly applying
the plan with `--apply`. Application rechecks current evidence inside the
transaction and refuses changed/conflicting evidence or removal of rows referenced
by tickets.
No automatic destructive cleanup is performed by `daily-run`.

League diagnostics separate missing labels, recognized competitions outside the
allowlist, and unrecognized labels, including the actual labels and counts.
League-bearing prediction sources supply the league identity. Other prediction
sources are attached by home/away team names only when they uniquely match a
league-bearing prediction for an allowed competition; ambiguous, unanchored and
out-of-allowlist predictions are filtered before merging. Result persistence,
calibration history and ticket generation use the same allowlist. The bookmaker
is used for fixture matching and prices, not to infer the prediction's league.
Allowed leagues have no priority order and no unknown-league fallback.
Champions League, Europa League and Conference League are explicitly included;
other cup competitions remain outside the allowlist.

Prediction persistence verifies shared-source aliases even when the new display
name has no exact stored match. Verification includes stored FT/HT results, not
just names and source IDs. Ambiguous identities are diagnosed and the capture
fails without saving any of its pending changes, including earlier snapshots
implicated by a later collision. Safe diagnostics report capture
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
