# Prediction

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
dotnet run --no-build -- ticket-1-3p3+ 2026-10-23 15
dotnet run --no-build -- ticket-1-3p3+ 2026-10-23 15 safe
```

`calibration` (read-only) measures real accuracy of the 1X2 consensus grouped
by average source Prob., confidence label and source, plus BTTS/Goals source
agreement. Groups under 30 matches are flagged; `Safe` is the 95% Wilson lower
bound. The date-only command collects predictions and does **not** automatically
build a ticket. Run `ticket` separately afterwards. It returns at most N distinct
unfinished, not-yet-started matches (default 15), prioritizing confirmed selections
and using remaining places for separately labelled unconfirmed proposals.

### Ticket selection rules

- A uniquely matched Maxbet offer on the requested local calendar date must have
  a valid actual price for the predicted home/away win. Unknown base odds are
  excluded, even from proposals. The base win requires an unambiguous 1/2 consensus
  with a source probability, even when an independent goals selection is chosen.
- The base price determines which selections are allowed:

  | Base win odds | Allowed selections | Minimum final odds |
  |---|---|---|
  | Below 1.40 | HT/FT `1-1` / `2-2` (with or without 2+ goals) or Win & Goals combinations | 1.70 |
  | 1.40 to below 1.70 | Win with first-half goals, goals in both halves, or 2-5 / 2-6 total goals | 1.70 |
  | 1.70 through 2.00 inclusive | Straight win only; no goals or HT/FT replacement | Base win price |
  | Above 2.00 | BTTS YES, 3+ (Over 2.5), BTTS & 3+, or the three standalone half-goal selections below | 1.65 for BTTS; 1.70 otherwise |

- Straight win, standalone BTTS and standalone 3+ selections need **>=55% measured
  historical accuracy with >=100 graded cases**. Composite (`Combo` / `HalfGoals`)
  selections need **>=50 graded cases** and either **>=55% historical accuracy**
  or **expected return >=1.05 (EV >= +5%)** at the actual Maxbet price. Win and
  winner-combination selections additionally require the win signal to meet `>=55%`
  (`n>=100`). Above 2.00, standalone BTTS/goal selections do not require the win
  signal's historical accuracy.
- If an alternative uses BTTS YES, its majority BTTS signal needs
  **>=55% measured accuracy with >=100 cases**. `BTTS & 3+` needs both BTTS YES
  (`>=55%`) and Over 2.5 (`>=60%`, `n>=100`); standalone `3+` and standalone
  half-goal markets require Over 2.5 (`>=60%`, `n>=100`).
- Winner goal-range, first/second-half goal and HT/FT-with-goals selections require a
  reliable majority Over/Under 2.5 signal (>=60%, >=100 cases), plus the
  whole-selection threshold. An Under signal is evidence for calibration, not a
  new permitted Under bet. Plain HT/FT uses only the winner and whole-selection
  thresholds. No BTTS/Goals threshold is required when that component is unused.
- Standalone half-goal markets use their own whole-selection historical hit rate,
  across all historical matches with valid half-time scores, not the winner's
  probability bucket. No generic BTTS/Over signal is imposed on these markets:
  over 2.5 does not predict a specific distribution of goals between halves.
- Missing, tied or insufficient binary signals do not pass. These percentages
  are historical hit rates, **not source agreement** or guaranteed match probabilities.
- If no qualifying priced alternative exists, an otherwise eligible combination
  with unknown odds may appear only as a proposal with `n/a`, outside the confirmed
  ticket and its combined odds/return. Known odds below the applicable minimum are not treated as
  unknown. There is no fallback to a low-priced straight win or a below-threshold pick.

The allowed final selections, subject to the odds bands above, are:

- `fix 1`, `fix 2`;
- `1 & 1+ prvo`, `2 & 1+ prvo`;
- `1 & 1+ prvo & 1+ vtoro`, `2 & 1+ prvo & 1+ vtoro`;
- `1 & 2-5 golovi`, `1 & 2-6 golovi`, `2 & 2-5 golovi`, `2 & 2-6 golovi`;
- `BTTS`, `BTTS & 3+`, `3+`;
- `1-1`, `2-2`, `1-1 & 2+`, `2-2 & 2+`;
- `1-2 prvo & 1-2 vtoro`: 1-2 total goals in each half;
- `1-3 prvo & 1-3 vtoro`: 1-3 total goals in each half;
- `1-3 prvo & 3+`: 1-3 total goals in the first half and at least 3 in the match.

`1-1` / `2-2` mean half-time/full-time home win / away win, **not exact scores**.
`1+ prvo` / `1+ vtoro` require at least one total goal in the first / second half.
`2+` / `3+` require at least two / three full-time goals; goal ranges are inclusive.
Draws, double chance and BTTS NO are not ticket selections.
Existing calibration commands can still grade their supported markets.

The ticket can never hold more picks than Maxbet offers: its public football offer
is capped at roughly 500 matches for the whole coming week (about 70 per day), so
most of the day's predicted matches simply have no Maxbet price and are reported as
`Nema spartena ponuda`. The `why` flag prints the history hit rate of the standalone
half-goal markets, one `[ODBIEN]` line per matched fixture with its base odds, reason
and signals, and `[BEZ-PAR]` lines for Maxbet offers that no stored match was paired
with. One Maxbet fixture stored under several spellings counts once (the record with
the most sources is kept). Team names tolerate Maxbet's initials ("Bayern M." =
"Bayern Munich"); an initial is expanded only when the other name has a word that
starts with it, so "Real Madrid C" never equals "Real Madrid".

The standalone half-goal markets use one global hit rate (about 29%, 47% and 47.5%)
that cannot distinguish matches. Relaxing the 55% gate for them would select
fixtures only because their price happens to be high; they need a match-specific
signal before they can be treated as value bets.
`nodc` remains accepted for compatibility but no longer changes the allowed markets.

Each complete selection needs 100+ graded historical cases and >=55% accuracy for its signal.
Winner combinations use the corresponding 1X2 consensus and probability bucket;
they are calibrated against the **whole combination**, not by multiplying component
accuracies. There is no separate half-time prediction model. BTTS & 3+ requires
both BTTS YES and over-2.5/over-3.5 source signals. Missing half-time scores are
ungraded rather than assumed to be 0-0. The ticket prints half-time history coverage;
markets without enough history are skipped, so not every listed type will appear.

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
selection is only an unconfirmed proposal. Component odds are never multiplied
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
It grades the frozen market/pick using current stored final/half-time results,
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

`collect-results <date>` queries all three existing providers in priority order
(Forebet, FootballResultsOnline, OpenFootball), even when an earlier source
returns some results. It merges by the existing date + normalized home/away
identity, never fuzzy-matching aliases or reversing teams. Duplicate final scores
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
  It picks the top eligible selection in each league before considering a second
  selection from any league (maximum two per league) to fill the requested count.
  Within a league, source agreement share ranks first, followed by supporting-vote
  count and the AI-safe ranking. Unknown or unlisted league labels are excluded.
  Existing odds and calibration thresholds still apply; if fewer than the
  requested number qualify, the ticket stays shorter. This ranking is not a
  guarantee of improved accuracy.
  League simulations are saved as `ml-league` or `ml-league-safe`, separately
  from standard AI simulations, and included in `ticket-report`.

### Daily tickets without historical filters

```powershell
dotnet run --no-build -- 2026-10-26
dotnet run --no-build -- daily-ticket-league 2026-10-26 15
dotnet run --no-build -- daily-ticket 2026-10-26 15
```

Both commands read only matches dated on the requested day and their saved
prediction snapshots (latest per source). They do not scrape prediction sites,
read historical results, train/load ML models, or apply historical accuracy,
sample-size or EV filters. They fetch current Maxbet prices, require unique
fixture/date matching, merge duplicate stored fixtures, and exclude started
matches. No database rows are modified by these commands.

- `daily-ticket-league` accepts only the supported leagues listed above. It
  takes one candidate per league in priority order, then second choices, then
  further rounds if needed to reach the requested count. Unlike `ai-rank ...
  league`, it has no two-per-league cap.
- `daily-ticket` has no league filter, league priority or per-league cap. It
  ranks all eligible daily fixtures together.

Ranking uses known average source probability for the majority 1/2 prediction
first, then that probability descending, source agreement, supporting-source
count and lower confirmed odds. Fixtures without source probabilities remain
eligible but rank after those with known probabilities. Thus Inter at 70.7%
ranks ahead of Milan at 56.7% when both have 10/10 agreement. These are
**source forecasts**, not calibrated ML or whole-bet probabilities.

Price bands are retained: 1.70-2.00 selects the consensus straight win.
Below 1.70, confirmed winner combinations are considered, including `1 & T1 2+`
and `2 & T2 2+` (winner scores at least two goals). Total-goal combinations need
a daily Over 2.5 majority; the team-goal variant uses winner consensus only and
does not pretend total goals predict team goals. Below 1.40, half-time/full-time
is also allowed. Above 2.00, daily majority signals may allow BTTS, 3+, BTTS & 3+
or half-goal combinations. BTTS requires odds >=1.65; other alternatives >=1.70.
Among allowed alternatives the lowest confirmed qualifying price is selected.
No composite price is manufactured from component prices.

The count defaults to 15. If there are insufficient unstarted fixtures with
valid daily signals, confirmed prices and (for the league variant) supported
leagues, the output explicitly reports the shortfall. Add `why` to see
rejections, for example `daily-ticket-league 2026-10-26 15 why`.
Excel files are `daily-ticket-league-DATE-COUNT.xlsx` and
`daily-ticket-DATE-COUNT.xlsx`, with daily source evidence and an Info sheet.
They intentionally have no historical Accuracy, EV or whole-ticket win
probability, and do not enter the historical-calibration simulation ledger.
Older `daily-league-safe` simulations remain readable by `ticket-report`.
`ai-rank` and other existing historical ticket commands retain their behavior.

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