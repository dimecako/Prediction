# Prediction

## Render with Neon PostgreSQL

Use the existing Dockerfile for the Render web service and Neon for persistent
PostgreSQL storage. Render's Free PostgreSQL database expires after 30 days;
Neon's Free plan is not a time-limited trial, but storage and compute quotas apply.

Before switching connections:

1. Pause collection, ticket generation and other database writers.
2. Back up the local database with `pg_dump` in custom format. Keep the backup
   outside the repository and never commit database dumps or credentials.
3. Create an empty Neon database with a compatible PostgreSQL version. Restore
   using its direct (non-pooled) endpoint, with `pg_restore --no-owner --no-acl --exit-on-error`.
   Do not restore over an existing populated database.
4. Verify all table row counts, EF migration history and identity sequences.
   Test historical reports before resuming writes. Keep the local database.
5. Set Render environment variables using
   [Deployment/render.env.example](Deployment/render.env.example), including
   web authentication and the Neon Npgsql connection string. After a full
   restore, keep `Database__ApplyMigrations=false`.
6. Deploy and check `/health` and authenticated access to the web app.

Database migration does not transfer Excel workbooks or local ML model ZIPs.
Keep those locally; Render Free's writable filesystem is ephemeral. Web model
inference is not currently connected to predictions. FlareSolverr remains a
separate service as described in the environment example.

### Neon agent tooling

Project-local `neon` and `neon-postgres` skills are installed under
`.agents/skills`, with versions recorded in `skills-lock.json`.
Use `neon skills status` to check freshness. VS Code's local
`.vscode/mcp.json` connects to Neon over OAuth and is scoped to the linked
project; enable the server and sign in through VS Code before first use.
The workspace link (`.neon`) and connection variables (`.env.local`) are
git-ignored. ASP.NET Core does not automatically load `.env.local`; the
application still uses `ConnectionStrings__FootballDb`. Do not switch that
connection until the database restore has been verified.

### Daily cloud execution and database pages

Render runs the web application; Neon stores the shared history. GitHub Actions
runs the daily work even when the local computer is off. Configure the encrypted
repository secret `FOOTBALL_DB_CONNECTION` with a direct Neon Npgsql connection
string. Keep credentials out of workflow YAML, logs and commits.

The daily workflow runs the `daily-run` command, not the Windows-only
`tickets.ps1`. It collects predictions for the current Europe/Skopje date,
collects results for the previous day, and checks one additional day back only
if that date has unresolved ticket selections. Older pending dates are not
scanned. It then runs four ticket variants: daily, daily-league, FT combinations and
fixed-auto. Confirmed results are not overwritten. ML-ranked variants
already train their ranking models from history; a separate `train` command is
not needed. Build happens before execution, and Excel imports are not needed
because selections are stored in PostgreSQL.

Scheduling starts at 07:07 Europe/Skopje, with up to two hourly retries
at 08:07 and 09:07 local time. Since GitHub cron uses UTC, the workflow covers
the UTC hours that map to those three local times and filters each occurrence
through the Europe/Skopje timezone. Avoiding minute zero reduces peak-hour
scheduling load, but GitHub can still delay or drop scheduled occurrences;
this is not an exact-time guarantee.
The three-attempt budget and completed steps survive retries, revision changes
and ticket-count changes. Existing date/mode tickets are reused rather than regenerated.
Once a manual or scheduled execution succeeds for the target date, subsequent
attempts skip building and collection. Success is tracked by a date-specific
GitHub artifact retained for 90 days and checked against its successful run.
GitHub scheduling is best-effort and can be delayed. Scheduled workflows must exist on the repository's default
branch; scheduled runs and manual runs from `main` check out the deployed
`prediction-api` branch. Manual runs from other branches use the selected revision. Use the
Actions tab for manual runs, failure details and reruns. Check the first real
run before relying on unattended operation.

`daily-fixed-auto yyyy-MM-dd [count]` defaults to 15 selections (allowed range
1-15); `daily-run` passes its requested count to this mode too. Fixed-auto uses
only straight 1/2 picks, confirmed odds >=1.60 (preferably >=1.80), source-average
probability >=55% and AI probability >=55%. All source-qualified candidates are
AI-filtered and ranked primarily by exact-market historical Wilson lower bound before taking
the requested count. If fewer qualify, it explicitly reports the shortfall;
thresholds are never lowered and picks are not fabricated. These figures are
estimates, not guaranteed win probabilities. Existing frozen tickets, including
older one-selection fixed-auto tickets, are not replaced on retries.
The legacy `daily-fixed-1|2` and `daily-ai-fixed-1|2` commands retain their counts,
but also require the shared exact-market historical quality gates below.
Raw AI values are uncalibrated model signals, not measured win probabilities.

Set `Exports__Excel=false` for hosted execution; local Excel exports remain
enabled by default. Existing workbooks and model ZIPs are not deleted.
Authenticated read-only web pages:

- `/predictions?date=yyyy-MM-dd`: consolidated fixture rows with latest-per-source
  votes, FT/HT scores and expandable source details, consolidated before pagination.
  Defaults to highest 1X2 agreement percentage, then matching source count.
  `sort=result-agreement|result-probability|btts-agreement|btts-probability|goals-agreement|goals-probability`
  and `direction=asc|desc` sort the whole date before pagination; unknown values
  remain last. Each market has separate agreement and source-average probability
  columns; clicking either column header sorts by that metric and toggles direction.
  The average column includes its coverage. Only agreeing sources with valid percentages
  contribute to that average (60% and 55% average to 57.5%); missing values are
  excluded. The current schema stores source probabilities only for 1X2.
  BTTS/goals averages are explicitly unavailable, never copied from 1X2 or
  confused with source agreement. None of these figures is calibrated win probability.
  The "Collect results only" button opens a date confirmation form, then submits
  an authenticated, antiforgery-protected POST to `/results/collect`. It reuses
  `collect-results` (Forebet plus targeted SoccerVista and Flashscore FT/HT fallbacks), without
  migrations, prediction collection or ticket generation. Future dates are rejected;
  concurrent web collection returns 409 and partial/failed collection returns 502.
- `/tickets`: date and mode filters for all saved variants.
- `/tickets/ID`: frozen selections, evidence, current FT/HT scores and pending
  reasons.
- `/performance`: simulated stake, profit and ROI by mode.
- `/leagues`: strict pre-date consensus accuracy by league and market.

Opening these pages does not scrape sources or generate new tickets. The
existing manual collection button remains available separately. All historical
analyses continue to use their existing rules; sparse strict league statistics
are not evidence that migration lost predictions.

### Duplicate fixture results

Result collection retains agreeing providers' source IDs even when their scores
are deduplicated. A confirmed result can be saved to multiple existing records
with the same provider ID and match date, without merging records or changing
ticket/snapshot links. Both teams must pass conservative same-order alias checks;
age/number/women qualifiers must agree. Conflicting IDs from any stored provider,
an exact-name record outside the linked group, or conflicting stored FT/HT scores
block the whole duplicate group. Similar names alone never authorize propagation.
Existing results are never overwritten. Ambiguous groups remain explicit errors,
so the ticket script still preserves Excel files on collection failure.
Prediction saving reuses a unique existing source-ID link while preserving its
canonical names; ambiguous links without an exact record fail instead of creating
another duplicate. This does not repair or merge historical database records.

A fresh verified regulation-time provider fixture can also corroborate a unique
exact-name/date row and a separate unique provider-ID row. This requires a
non-placeholder UTC kickoff on that Skopje date, conservative same-order team
aliases (including club designators), compatible stored scores and no conflicting
provider IDs or real kickoffs. Legacy midnight kickoffs are treated as date-only
placeholders in this check. Results are saved to the proven rows without merging,
deleting, renaming or inventing prediction snapshots. Without this live evidence,
the existing strict duplicate rules still apply.

## Weighted consensus (local)

Build after pulling changes, then run:

```powershell
dotnet build
dotnet run --no-build -- weighted-consensus 2026-10-16
dotnet run --no-build -- backtest 2026-09-22
```

Both commands read the database configured by `ConnectionStrings__FootballDb`;
they do not collect data, write predictions, train models, or change deployment.
Use your local database connection. The existing report, Excel export, and
per-source backtest remain unchanged. Backtest now also prints a strict
equal-vote versus weighted-consensus comparison for 1X2, BTTS, and Goals.

### Rules

- Each source gets one vote per market, using its latest eligible snapshot
  (capture timestamp, then snapshot ID). Source names are case-insensitive.
- In strict mode a snapshot must have been captured **before 00:00 UTC on the
  match date**, even if the match kicks off later. Stored kickoff values
  currently default to midnight and are not reliable actual kickoff times.
  Same-day and later snapshots are excluded, including from training history.
- Weights are computed separately for each source and market using
  `(correct + 5) / (graded + 10)`. A source/market with no history has weight
  `0.5`. The prior reduces the influence of tiny samples; these weights are
  smoothed accuracy, not profit, calibration, or evidence of predictive edge.
- Only earlier match dates whose result was collected **strictly before the
  target day's midnight UTC cutoff** contribute to weights. All fixtures on
  the target date use the same frozen weights. This intentionally excludes
  results backfilled after the historical cutoff.
- Supported votes: Result `1` / `X` / `2`; BTTS `YES` / `NO`; Goals
  `O0.5` / `U0.5`, `O1.5` / `U1.5`, `O2.5` / `U2.5`, `O3.5` / `U3.5`.
  Missing and unsupported values do not vote or train weights. Markets are
  independent: a source without a Result tip may still vote on BTTS or Goals.
  Different Goals lines are distinct selections, not merged into one "over"
  or "under" vote; historical correctness uses each selection's own threshold.
- Highest summed weight wins; ties (within `1e-12`) abstain. No votes also
  abstain. Support share is winning weight / total voting weight, **not an
  outcome probability**.
- Comparison uses identical eligible snapshots and fixtures for both methods.
  Accuracy is correct / graded non-abstaining picks. Coverage is graded picks /
  all target-date fixtures with results. Different coverage can make raw
  accuracies misleading; the comparison does not claim statistical improvement.

No eligible history is reported explicitly and uses the neutral prior.
No eligible predictions is reported explicitly, not as a successful prediction.
Sparse output on old dates may be expected under the strict capture rule.
The original per-source backtest uses its existing latest-snapshot logic and
is therefore not the strict baseline; use the new `CONSENSUS BACKTEST` rows.

### Calibration and ticket

```powershell
dotnet run --no-build -- calibration
dotnet build
dotnet run --no-build -- collect-results 2026-10-18
dotnet run --no-build -- ticket 2026-10-20 15
dotnet run --no-build -- ticket 2026-10-20 5
dotnet run --no-build -- ticket 2026-10-20 15 nodc
dotnet run --no-build -- ticket 2026-10-20 5 safe
dotnet run --no-build -- ticket 2026-10-20 15 why
```

`calibration` (read-only) measures real accuracy of the 1X2 consensus grouped
by average source Prob., confidence label and source, plus BTTS/Goals source
agreement. Groups under 30 matches are flagged; `Safe` is the 95% Wilson lower
bound. The date-only command collects predictions and does **not** automatically
build a ticket. Run `ticket` separately afterwards. It returns at most N distinct
unfinished, not-yet-started matches (default and maximum 15). It never fills
remaining places with unconfirmed, unpriced or below-threshold selections.

### Ticket selection rules

All generation commands now use `exact-market-v2`:

- `ticket-1-3p3+` and the `1-3p3+` flag are retired and return an explicit error
  before collection or database access. They are removed from daily cloud and
  Windows automation. Historical modes, imports and settlement are preserved.
- No new selection may depend on half-time: HalfGoals, HT/FT, first-half goals
  and goals-in-both-halves combinations are excluded from all selectors.
- Strict-market selections require at least three source votes and >=60%
  agreement for their actual market. Winner & FT goals uses the restored
  previous eligibility rules described below, not the new coverage/return gates.
- Exact-event history must have >=100 graded cases for singles or >=50 for
  combinations, >=55% measured accuracy, accuracy * current odds >=1.05, AND
  Wilson 95% lower bound * odds >=1.00. These are conservative eligibility
  rules, not a statistically proven profit guarantee. They apply to straight
  wins, standalone BTTS/Goals and BTTS & 3+, not the restored winner & FT goals baseline.
- **Winner & FT goals baseline restored pending separate validation:** at least
  50 exact-event cases and either measured accuracy >=55% OR historical return
  >=1.05, with actual odds >=1.70. The winner must have >=100 cases and >=55%
  accuracy; 2-5/2-6 goal-range combinations also need a majority Over/Under
  signal with >=100 cases and >=60% accuracy. Team 2+ does not use a total-goals
  proxy. The old three-source-independent eligibility and OR gate are restored;
  the new three-source and Wilson-return requirements do not apply here.
  Baseline calibration is kept under `FT-BASELINE|` keys, separate from the
  strict challenger. Prematch/pre-date timestamp filtering, duplicate/conflict
  exclusions, no half-time markets and no unpriced filler remain in place.
  This restores eligibility, not the old dataset or a guaranteed 67.7% hit rate.
  Do not promote the challenger based only on the already-analyzed period;
  compare each exact type on a new chronological period before changing live rules.
- Fixed selections retain minimum odds 1.60; other fixes/alternatives use 1.70
  and standalone BTTS uses 1.65. A strong 1X2 or Over signal cannot replace a
  combination's own historical qualification. Unknown odds cannot
  qualify. If nothing qualifies with otherwise valid data, `NO_SELECTION` is
  explicit and no empty ticket is saved.
- Historical preparation uses only earlier match dates, results collected before
  the target Skopje date, and snapshots captured strictly before the historical
  kickoff. Unknown/midnight placeholder kickoffs use the start of the fixture
  date as a conservative cutoff. Later snapshots are excluded, not substituted.
  Exact-name/date duplicates count once; conflicting final-score groups are
  excluded. Rejection/coverage counts are logged.
- Straight 1X2/BTTS/Goals ML outputs remain raw, uncalibrated signals.
  Composite/half-time ML probabilities are unavailable: averages of component
  probabilities are no longer presented or ranked as a joint-event probability.
  Exact-market history, not raw ML, is the primary ranking signal.
- Evidence freezes the policy version, exact-market sample/hit rate, Wilson
  lower bound and historical expected return. Existing date/mode tickets are
  reused before fetching odds; old tickets are never regenerated or overwritten.
- No source reweighting, league blacklist, or probability threshold is fitted to
  the 15-day analysis. These changes still need a prospective, untouched-period
  comparison, including coverage and rejected selections; a tiny or zero output
  is preferable to silently relaxing gates.

- A uniquely matched Maxbet offer on the requested local calendar date must have
  a valid actual price for the predicted home/away win. Unknown base odds are
  excluded, even from proposals. The base win requires an unambiguous 1/2 consensus
  with a source probability, even when an independent goals selection is chosen.
- The base price determines which selections are allowed:

  | Base win odds | Allowed selections | Minimum final odds |
  |---|---|---|
  | Below 1.70 | Win & team 2+ or Win & 2-5 / 2-6 total goals | 1.70 |
  | 1.70 through 2.00 inclusive | Straight win only; no goals or HT/FT replacement | Base win price |
  | Above 2.00 | BTTS YES, 3+ (Over 2.5), BTTS & 3+ | 1.65 for BTTS; 1.70 otherwise |

- Straight win, standalone BTTS and standalone 3+ selections need **>=55% measured
  historical accuracy with >=100 graded cases**. Composite (`Combo`)
  selections need **>=50 graded cases**. BTTS & 3+ requires **>=55% historical
  accuracy** AND **expected return >=1.05** and the conservative-return gate.
  Winner & FT goals instead retains the prior **accuracy >=55% OR return >=1.05**
  eligibility. Win and
  winner-combination selections additionally require the win signal to meet `>=55%`
  (`n>=100`). Above 2.00, standalone BTTS/goal selections do not require the win
  signal's historical accuracy.
- If an alternative uses BTTS YES, its majority BTTS signal needs
  **>=55% measured accuracy with >=100 cases**. `BTTS & 3+` needs both BTTS YES
  (`>=55%`) and Over 2.5 (`>=60%`, `n>=100`); standalone `3+`
  requires Over 2.5 (`>=60%`, `n>=100`).
- Winner goal-range selections require a
  reliable majority Over/Under 2.5 signal (>=60%, >=100 cases), plus the
  whole-selection threshold. An Under signal is evidence for calibration, not a
  new permitted Under bet. Win & team 2+ uses the winner and whole-selection
  thresholds; no unused BTTS/Goals component is required.
- Missing, tied or insufficient binary signals do not pass. These percentages
  are historical hit rates, **not source agreement** or guaranteed match probabilities.
- If no qualifying priced alternative exists, the fixture is rejected.
  There is no unpriced proposal, low-priced straight-win or below-threshold fallback.

The allowed final selections, subject to the odds bands above, are:

- `fix 1`, `fix 2`;
- `1 & T1 2+`, `2 & T2 2+` (winner scores at least two goals);
- `1 & 2-5 golovi`, `1 & 2-6 golovi`, `2 & 2-5 golovi`, `2 & 2-6 golovi`;
- `BTTS`, `BTTS & 3+`, `3+`.

`1-1` / `2-2` mean half-time/full-time home win / away win, **not exact scores**.
`1+ prvo` / `1+ vtoro` require at least one total goal in the first / second half.
`2+` / `3+` require at least two / three full-time goals; goal ranges are inclusive.
Draws, double chance and BTTS NO are not ticket selections.
Those half-time definitions apply only to historical tickets. Existing
calibration commands can still grade their supported markets.

The ticket can never hold more picks than Maxbet offers: its public football offer
is capped at roughly 500 matches for the whole coming week (about 70 per day), so
most of the day's predicted matches simply have no Maxbet price and are reported as
`Nema spartena ponuda`. The `why` flag prints one `[ODBIEN]` line per matched fixture with its base odds, reason
and signals, and `[BEZ-PAR]` lines for Maxbet offers that no stored match was paired
with. One Maxbet fixture stored under several spellings counts once (the record with
the most sources is kept). Team names tolerate Maxbet's initials ("Bayern M." =
"Bayern Munich"); an initial is expanded only when the other name has a word that
starts with it, so "Real Madrid C" never equals "Real Madrid".

`nodc` remains accepted for compatibility but no longer changes the allowed markets.

### Winner & full-time goals only

```powershell
dotnet run --no-build -- ticket-ft-combos 2026-11-07 15
dotnet run --no-build -- ticket-ft-combos 2026-11-07 15 why
```

This separate ticket considers only `1/2 & 2-5 golovi`, `1/2 & 2-6 golovi`,
`1 & T1 2+` and `2 & T2 2+`. It never selects a standalone fix, BTTS,
BTTS & 3+, standalone goals or a half-time market. Unlike the general ticket's
base-price bands, it evaluates these winner combinations at every valid base
win price, using the restored FT baseline qualification above. Ranking uses
historical safety first. It accepts 1-15 selections, defaults to 15, and never
fills a shortfall. Mode `ft-combos` is stored and graded separately, appears in
the web ticket filter, and reuses the first frozen date/mode ticket.
Both `daily-run` and `tickets.ps1` include this command.

### Prospective paired FT comparison (simulation only)

```powershell
dotnet run --no-build -- ticket-ft-shadow 2026-11-07 15
dotnet run --no-build -- ticket-ft-shadow-report
dotnet run --no-build -- ticket-ft-shadow-report 2026-11-07 2026-12-07
```

`ft-paired-v1` compares the restored live FT eligibility baseline
(`ft-baseline-exact-market-v2`) with the strict challenger
(`ft-strict-exact-market-v1`). Both arms see the **same matched upcoming fixtures,
as-of source snapshots and actual prices**, with the same one-pick-per-fixture,
historical-safety-first ranking and maximum requested count (1-15, default 15).
This is a paired eligibility experiment, not an AI on/off test. Its deterministic
match-ID tie break need not reproduce a separately captured live ticket exactly.
Active tickets and their rules are unchanged; there is **no automatic promotion**.

The challenger additionally requires source coverage (at least three valid
votes and 60% agreement), coverage-qualified component/exact-event calibration,
and both accuracy and conservative-return gates. Baseline uses the restored
FT criteria documented above. Both use only pre-date, prematch history with
results known at capture time. Draw/tied signals abstain; missing alternative
prices are frozen rejections, not invented selections.

Only current/future Skopje dates can be newly captured; started fixtures and
known results are excluded. There is no retrospective backfill. The first valid
capture is saved atomically in `ft_shadow_runs`, once per experiment/match date,
including valid zero-selection evaluations. Retries reuse it before fetching
odds, even if the requested count changes. Missing/invalid input fails explicitly
without saving a misleading empty run. Frozen offers retain eligibility,
rejection reasons, historical scores and each arm's selected positions.
Frozen capture timestamps are compared with database metadata at PostgreSQL's
microsecond precision; the original JSON timestamp and decisions remain unchanged.

The cumulative report shows each exact combination type and totals: offered,
qualified, selected, coverage, wins/losses/Pending, accuracy with descriptive
Wilson intervals, shared/arm-only selections and hypothetical flat-one-unit
**singles** profit/ROI (not accumulator ROI). It grades frozen exact picks using
stored regulation FT results; missing results remain Pending. Thirty captured
dates and 100 graded selections **per arm/type** are only a manual-review floor,
not proof or a significance test. Arms/types overlap and outcomes are correlated.
Changes to experimental rules require a new experiment version, not rewriting
previous decisions.

Both automation entry points capture and report the comparison. Automatic result
collection remains limited to yesterday and pending day-before-yesterday;
older unresolved dates need manual `collect-results yyyy-MM-dd`, followed by the
shadow report. Apply the additive `AddFtShadowRuns` migration before deploying
automation: `daily-run` never migrates. Standalone shadow commands follow existing
CLI migration behavior.

Winner combinations use the corresponding 1X2 consensus and probability bucket;
they are calibrated against the **whole combination**, not by multiplying component
accuracies. There is no separate half-time prediction model. BTTS & 3+ requires
both BTTS YES and Over 2.5 source signals. Historical missing half-time scores
remain ungraded rather than assumed to be 0-0.

`collect-results` now reads Forebet's explicit `Host_SC_HT` / `Guest_SC_HT` scores
alongside final scores. Missing, incomplete or invalid half-time scores remain
unknown; invalid scores are logged. The existing fallback providers still supply
final scores only. Rerun `collect-results yyyy-MM-dd` for previous dates to backfill
half-time scores in existing results. Existing final scores and their collection
timestamps are preserved; half-time is merged only if final scores agree.
`collect-results` and `ticket` automatically apply the additive nullable half-time
migration using the configured database.

Odds come from Maxbet MK. Without a verified base win price no selection or proposal
is produced; unavailable offers and rejection reasons are reported explicitly.
Combined-market codes were verified against Maxbet's public
`/restapi/offer/en/ttg_lang` metadata on 2026-10-20. HT/FT uses codes 10/18;
HT/FT & 2+ uses 862/863; BTTS & 3+ uses 303; standalone half-goal combinations
use 51428, 51568 and 55173 respectively. The `v` in Maxbet labels means OR;
`&` means AND: `GGv3+` (536) is not `GG3+` (303).
An offer must contain the exact mapped combined-market price; otherwise the
selection is rejected. Component odds are never multiplied
to invent a combined-market price. The console and Excel include base odds and winner, BTTS YES/NO and Over/Under
2.5 historical signal accuracy/sample sizes. Excel separates `Tiket` from
`Predlozi - nepotvrdeni`; proposals do not contribute to combined odds or chance.
EV = historical accuracy x odds - 1; picks with EV > 0
are ranked first. `safe` ranks by safety only. The chance shown is measured
history, not a guarantee; combined chance assumes independent matches, and EV is
optimistic because accuracy is averaged per signal, not per league.

### Phase 9: simulated ticket tracking, ROI and leagues

```powershell
dotnet build
dotnet run --no-build -- ticket 2026-10-23 5 safe
dotnet run --no-build -- collect-results 2026-10-23
dotnet run --no-build -- ticket-report
dotnet run --no-build -- ticket-report 2026-10-01 2026-10-31
dotnet run --no-build -- ticket-diagnostics 2026-10-26 2026-10-28
dotnet run --no-build -- ticket-source-check 2026-10-28 2026-10-28
dotnet run --no-build -- league-stats 2026-10-01 2026-10-31
```

Every nonempty confirmed ticket is automatically saved as a **simulation**, not
an actual wager, with a stake of **1 unit for the whole accumulator**. Selections,
order, fixture identity, league, actual Maxbet kickoff, prices and calibration
evidence are frozen in PostgreSQL. Unconfirmed proposals are never recorded as
bets. Empty tickets are not saved.

An identical date, mode, set of matches/selections, kickoff times and prices
reuses its existing simulation ID, even if the requested count or calibration
statistics changed. Frozen evidence is also reused in the console/Excel.
Changed selections/prices or `safe` versus default `value` mode create separate
simulations. These may overlap: they are not independent experiments.
The Excel filename includes `-sim-ID`, and its `Simulation` sheet records the
ID, creation time, mode and stake. A simulation is saved before Excel export;
an export failure does not lose it.

`ticket-report [from] [to]` accepts inclusive **match-date** limits. With no limits
it reports all saved simulations; with only `from` it reports that date onward.
It prints each frozen selection, final/half-time scores and Won/Lost/Pending,
plus selection counts and UTC creation time for each ticket. Pending reasons
distinguish missing final results from missing required half-time scores.
Missing final scores are not evidence that a fixture is still playing: a source
may be incomplete or team identities may not match. The report reads stored
results only; it does not fetch live statuses or infer postponement/void rules.

`ticket-diagnostics [from] [to]` explains pending selections using stored data:
missing final versus required half-time scores, prediction sources, Forebet
capture times and stored provider fixture IDs. It lists up to five unverified
same-order team-name candidates within one day, including their dates, sources
and available final scores. A candidate is NOT a confirmed alias or a safe result
link (for example, CD Ourense and UD Ourense must not be assumed identical).
The same diagnostics run after `collect-results` and `ticket-report`.

`ticket-source-check [from] [to]` additionally fetches the original Forebet feed
for dates with pending ticket fixtures. It prints exact stored-ID/name matches
with their source date, raw score and status (including non-FT statuses), or says
that no exact match was returned. It never writes scores or changes tickets.
An absent row is not proof of postponement or of a matching failure; it still
requires verification. Provider/schema failures are explicit nonzero errors.
Postponed (`Postp.`), extra-time and penalty (`Pen.`) rows are diagnostic only.
They are not converted into a loss, a void, or an assumed 90-minute score.
Half-time, extra-time and penalty fields are printed separately for verification.

New Forebet predictions retain the original `forebet:ID` through aggregation and
snapshot persistence. Result collection resolves that ID within the same source
and match date before falling back to exact normalized names/date. Verified
shared-ID duplicates use the safeguards described above; other conflicting
or ambiguous ID/name links are logged and rejected.

`merge-duplicate-matches [--apply]` lists stored fixture rows that share one
provider ID (for example `forebet:ID` or `soccervista:ID`) on the same match date
and pass the verified-duplicate checks (no differing IDs per source, consistent
scores, related team names). Without `--apply` it is a dry run. With `--apply`
it moves snapshots, and a missing result, to one kept row and deletes the others
in one transaction. Rows used by more than one saved ticket, cross-date links and
name-only duplicates are reported as skipped and never merged automatically.
The nullable source-ID migration is additive. Existing snapshots remain valid
but have no recoverable source ID unless a new prediction capture provides it;
old frozen ticket selections, prices and results are not rewritten.
Sources that return no final scores cannot be repaired by identity matching:
rerun collection later or verify against another reliable result source.

`collect-results` tries Forebet first, then targets only fixtures still missing
FT or complete HT with SoccerVista, and finally targets any remaining gaps with
Flashscore. SoccerVista verifies the fixture ID, teams, date, final score and
regulation-time `FIN` status against the event's detail feed; it does not provide
HT, which stays unknown rather than being inferred from goal-event minutes.
Flashscore verifies the selected date, teams and FT on both its result list and
match page, reads HT only from the displayed first-half score, and rejects
extra-time/penalty status or period data. Conflicting stored scores are not
overwritten, and unavailable or invalid provider data remains unresolved.
New SoccerVista prediction snapshots retain `soccervista:ID`; older snapshots
still use exact normalized teams and date. Existing scores are not overwritten
on disagreement, and HTTP/schema failures remain explicit collection errors.
Detail verification requires an extra request per finished SoccerVista fixture.
If the detail API returns an HTTP error or times out, the collector reads the
embedded fixture object from the match page and applies the same ID, team,
date, score and `FIN` checks. It never substitutes a prediction or an unchecked
daily score. If a fixture's details remain unavailable, that fixture is logged
and left unresolved, while other verified SoccerVista results are retained.
Unresolved detail failures still count as a failed provider and give a nonzero
collection exit code; a successfully verified page fallback is not a failure.

For inclusive multi-day collection, followed by a report:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\collect-results-range.ps1 -From 2026-10-21 -To 2026-10-28
```

The script uses the existing build (`dotnet build` after code changes), continues
after per-day nonzero exit codes and reports all failed dates at the end.
It does not generate tickets or delete Excel files. A nonzero collection exit
may mean partial provider failure or rejected score/identity conflicts, not that
all collected results were lost. Unavailable scores remain pending.

`ticket-report` grades the frozen market/pick using current stored final/half-time results,
so result corrections and half-time backfills are reflected on the next report.
An accumulator wins only if every leg wins; any losing leg settles it as lost,
even if other legs are still pending. Missing results or required half-time
scores are ungraded, not inferred losses, except when the final total alone
already makes a half-goal selection impossible. For example, `1-3 prvo & 3+`
loses at a final score of 1-1 even without half-time data. The two both-half
goal ranges also lose when the final total is outside their possible sum.
A feasible total still requires explicit half-time scores; it never implies a win.
This final-only loss rule applies to ticket settlement, not historical selection
calibration, which still requires complete half-time samples to avoid counting
only the known losses among fixtures with missing half-time data.
Postponements/cancellations stay
pending until a usable result exists; bookmaker void rules are not inferred.

- Winning payout = stake x product of the frozen decimal odds; losing payout = 0.
- Profit = payout - stake. ROI = total settled profit / total settled stake.
- Pending stakes are shown separately and excluded from settled ROI.
- No settled tickets means ROI `n/a`, not a claimed 0% return.
- Units exclude tax, fees, bookmaker limits and real-money payouts.
- The report shows totals and separate `safe`/`value` totals. Its league/market
  breakdown deduplicates match+market+pick and uses the earliest frozen price
  in the requested range. That breakdown's **flat 1-unit singles ROI is
  hypothetical**, not accumulator ROI or an allocation of accumulator profit.

During `ticket`, uniquely matched Maxbet offers also save their explicit
`leagueName` on matches, even if the fixture does not qualify for a ticket.
The league is frozen separately on saved selections. Older matches without
verified labels remain `(unknown league)`; no team-name or country guesses
are used. Existing Excel tickets are **not** imported retroactively: their
pre-match prices/timestamps cannot be reliably reconstructed.

`league-stats [from] [to]` measures equal-vote 1X2, BTTS and Goals O/U 2.5
consensus accuracy by league, with sample sizes and 95% Wilson lower bounds.
It uses the existing strict backtest eligibility rule: latest snapshot per
source captured **before 00:00 UTC on the match date**; ties abstain and
missing predictions do not count as losses. Same-day-only history can therefore
have no eligible predictions. Groups under 30 are marked as small samples.
This is observational analysis, not league-specific calibration of the ticket
selector: existing ranking, thresholds and EV calculations are unchanged.

The additive migration adds a nullable league and the simulation tables;
`ticket`, `collect-results`, `ticket-report` and `league-stats` apply migrations
automatically. `night.ps1` reports all tracked ticket ROI after collecting results
and backtesting; `all.ps1` validates its date range and builds first, stopping if
the build fails. It then runs results collection, database status, backtest,
weighted consensus, calibration, all saved ticket ROI and league statistics
from `BacktestFrom` through `Date`. It does not collect new predictions or
generate a new ticket.
This completes Phase 9's tracking/measurement functionality, **not proof of
profitability**. That requires enough newly saved, subsequently graded tickets.

`collect-results <date>` queries all four existing providers in priority order
(Forebet, SoccerVista, FootballResultsOnline, OpenFootball), even when an earlier source
returns some results. It merges by the existing date + normalized home/away
identity, never merging by fuzzy names alone or reversing teams. Agreeing source
IDs are retained to resolve verified duplicate database records. Duplicate final scores
are not inserted twice; matching final scores can supplement missing half-time
data. Conflicting final/half-time scores are logged and never overwrite an
earlier source or an existing stored score. Provider request/JSON failures,
timeouts and rejected/conflicting rows are reported; usable results from other
providers are still saved, and the command exits nonzero for these errors.
It prints final-score coverage for the date and for unique saved-ticket fixtures.
Provider counts of zero and incomplete coverage are explicit, not proof that
all fixtures have been graded. No existing provider can guarantee complete
coverage, and an empty response does not invent scores or settle pending picks.

### Phase 10: AI Ranking and out-of-time ML validation

```powershell
dotnet run --no-build -- train-model
dotnet run --no-build -- ai-rank 2026-10-23 15
dotnet run --no-build -- ai-rank 2026-10-23 5 safe
dotnet run --no-build -- ai-rank 2026-10-23 15 safe league
dotnet run --no-build -- ticket 2026-10-23 15 ml
```

- `FootballMatchTrainingRow` and `FootballModelTrainer` encode all **11 sources**
  (`Forebet`, `Statarea`, `Vitibet`, `PredictZ`, `ZuluBet`, `WinDrawWin`,
  `Feedinco`, `SoccerVista`, `FootyStats`, `Oddslot`, `TheDatabetics`) together
  with consensus vote shares (`HomeVoteShare`, `DrawVoteShare`, `AwayVoteShare`,
  `AverageWinnerConfidence`, `BttsYesShare`, `Over25Share`).
- `ResultModelTrainer`, `BttsModelTrainer`, and `GoalsModelTrainer` evaluate on a
  **strictly chronological 80/20 out-of-time split** (`MlMatchRanker.SplitChronologically`)
  before fitting the full models (`ResultModel.zip`, `BttsModel.zip`, `GoalsModel.zip`),
  and print out-of-time calibration by ML probability bucket (`<50`, `50-55`, `55-60`,
  `60-65`, `65-70`, `70+`).
- `ai-rank` (or `ticket ... ml`) trains `MlMatchRanker` strictly on historical matches
  before the target date (`MatchDate < date`), computes the normalized ML probability
  for each match's candidate (`ML=...%` in `[SIGNALI]`), blends it with the empirical
  95% Wilson lower bound (`RankSafe = (Safe + MlProbability) / 2`), and saves the
  resulting simulated ticket under mode `ml` or `ml-safe`.
- Add the `league` flag to `ai-rank` to select from these Maxbet league labels in
  priority order: England Premier League, France Ligue 1, Germany Bundesliga,
  Italy Serie A, Spain La Liga, Belgium Jupiler Pro League, Netherlands
  Eredivisie, Portugal Liga Portugal, Turkey Super Lig, Scotland Premiership,
  Switzerland Super League, Austria Bundesliga, Poland Ekstraklasa, Sweden
  Allsvenskan, Slovakia Nike Liga, Slovenia Prva Liga, Russia Premier League,
  Romania Superliga, Denmark 1st Division, Czech Chance Liga, France Ligue 2,
  England Championship, Germany Bundesliga 2, Italy Serie B, Spain La Liga 2.
  All ticket generators use the same ordering: priority leagues first, one
  selection per league per round; eligible unrecognized league labels are used
  afterward as fallback. Explicitly excluded cup competitions and missing
  league labels are never selected. Within each league, the generator's existing
  ranking determines the order. The `league` flag keeps its separate simulation
  mode but does not change this ordering.
  Existing odds and calibration thresholds still apply; if fewer than the
  requested number qualify, the ticket stays shorter. This ranking is not a
  guarantee of improved accuracy.
  League simulations are saved as `ml-league` or `ml-league-safe`, separately
  from standard AI simulations, and included in `ticket-report`.

### Daily tickets with exact-market historical filters

```powershell
dotnet run --no-build -- 2026-10-26
dotnet run --no-build -- daily-ticket-league 2026-10-26 15
dotnet run --no-build -- daily-ticket 2026-10-26 15
```

Both commands read only matches dated on the requested day and their saved
prediction snapshots (latest per source). They do not scrape prediction sites.
They prepare pre-date history, apply the shared exact-market accuracy, sample
and return gates, and optionally train an ML ranker when sufficient history exists.
They fetch current Maxbet prices, require unique
fixture/date matching, merge duplicate stored fixtures, and exclude started
matches. Nonempty tickets are now saved to the simulation ledger for subsequent
grading, with the historical quality evidence frozen for each pick.

- Both commands take one candidate per priority league in configured order,
  then subsequent rounds. If that does not fill the ticket, other unrecognized
  league labels are used as fallback, one per league per round. Explicitly
  excluded cups and fixtures without league metadata are never selected.
- `daily-ticket-league` and `daily-ticket` share the same selection ordering;
  they retain distinct simulation modes and output filenames.

Ranking uses the exact-market Wilson lower bound, measured accuracy and sample
size before source agreement/probability tie-breaks. Source forecasts and raw ML
are not calibrated whole-bet probabilities.

Price bands are retained: 1.70-2.00 selects the consensus straight win.
Below 1.70, confirmed winner combinations are considered, including `1 & T1 2+`
and `2 & T2 2+` (winner scores at least two goals). Total-goal combinations need
a daily Over 2.5 majority; the team-goal variant uses winner consensus only and
does not pretend total goals predict team goals. Above 2.00, qualified signals
may allow BTTS, 3+, or BTTS & 3+. No half-time markets are allowed.
BTTS requires odds >=1.65; other alternatives >=1.70.
Among allowed alternatives historical safety/accuracy determines selection.
No composite price is manufactured from component prices.

The count defaults to 15 and accepts 1-15. If there are insufficient unstarted fixtures with
valid signals, exact-market history, confirmed prices and (for the league variant) supported
leagues, the output explicitly reports the shortfall. Add `why` to see
rejections, for example `daily-ticket-league 2026-10-26 15 why`.
Excel files are `daily-ticket-league-DATE-COUNT.xlsx` and
`daily-ticket-DATE-COUNT.xlsx`, with daily source evidence and an Info sheet.
Historical sample, accuracy and return are frozen in the evidence; there is no
claimed calibrated whole-ticket win probability. Separate ledger modes are `daily` and `daily-league`, with
1 simulated unit for the whole ticket and frozen selections/prices. The existing
non-null numeric calibration fields retain zero with sample size zero for
backward compatibility; read the quality evidence, never interpret this as
measured 0% accuracy. Reports grade actual
results instead. Excel includes a Simulation sheet and database match IDs.
Older `daily-league-safe` simulations remain readable by `ticket-report`.
`ai-rank` and other ticket commands share the same strict quality policy.

### Running all tickets and grading automatically

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass; .\tickets.ps1
.\tickets.ps1 -Date 2026-10-27
.\tickets.ps1 -PlanOnly
.\tickets.ps1 -Count 5 -SkipCollection
```

`tickets.ps1` freezes the target date once using the **local system clock**:
00:00 through 17:59 selects today; 18:00 through 23:59 selects tomorrow.
`-Date yyyy-MM-dd` overrides this rule. The previous day always means target
date minus one, including when an explicit date is supplied.

The script builds, archives/verifies existing previous-day and target-day ticket
workbooks, collects previous-day results, prints their grading, and collects
target-day predictions. It then runs eight distinct variants (up to 15 qualified picks
each by default): `ticket`, `ticket safe`, `ai-rank`, `ai-rank safe`,
`ai-rank safe league`, `daily-ticket`, `daily-ticket-league`, and `ticket-ft-combos`.
New date/mode tickets fetch current odds; existing date/mode tickets are reused. These
are not guaranteed identical across variants. ML ticket commands perform their
existing historical training. A final target-date report prints the available
results, per-mode pick accuracy, ticket status and simulated ROI.

The script never deletes XLSX files; remove unwanted workbooks manually.
Database history is never deleted. Pending results can still be graded after
manual Excel deletion. A build/archive failure stops immediately; other command
failures are reported. `-SkipCleanup` remains accepted for compatibility but has
no effect because automatic cleanup has been removed.
`-SkipCollection` skips prediction collection only. `-PlanOnly` changes nothing.

Older daily workbooks can also be imported without generating tickets or
deleting files:

```powershell
dotnet run --no-build -- ticket-import 2026-10-26
dotnet run --no-build -- collect-results 2026-10-26
dotnet run --no-build -- ticket-report 2026-10-26 2026-10-26
```

Import uses the original creation time, kickoff, selections and prices, not
today's odds or a reconstructed strategy. It requires unique stored fixture
identity (new workbooks include match IDs), valid pre-kickoff capture and supported
markets. Ambiguous/missing matches or a historical ticket whose confirmed picks
do not match its saved simulation cause an explicit failure, preserving files.
Identical imports reuse the existing simulation; all workbook imports in one
command are transactional. Result/half-time gaps remain pending, and later
backfills or corrections are reflected by rerunning `ticket-report`. Overlapping
tickets are not independent evidence; one day's ROI does not prove profitability.

`all.ps1` retains its existing analytics/training behavior and does not generate
tickets. PowerShell orchestration tests (mock commands and isolated temp files):

```powershell
powershell -ExecutionPolicy Bypass -File .\Tests\TicketsScript.Tests.ps1
```

### Tests

```powershell
dotnet test .\Tests\Prediction.Tests.csproj
```

The database integration test is opt-in. It requires the migrated local database
configured by `ConnectionStrings__FootballDb`, and rolls back all test rows:

```powershell
$env:PREDICTION_RUN_DB_TESTS = "1"
dotnet test .\Tests\Prediction.Tests.csproj --filter "FullyQualifiedName~TicketLedgerDatabaseTests"
Remove-Item Env:\PREDICTION_RUN_DB_TESTS
```