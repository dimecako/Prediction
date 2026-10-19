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
  actual odds **>=1.40** for the predicted home/away win. Odds of 1.20 or 1.30 and
  unknown base odds are excluded, even from proposals.
- The win signal needs **>=55% measured historical accuracy with >=100 cases**.
- Base win odds **>=1.70** keep the straight home/away win; no combination replaces it.
- Base win odds **1.40 to <1.70** require an eligible alternative with actual odds
  **>=1.50**, >=55% whole-selection historical accuracy and >=100 cases.
- If that alternative uses BTTS YES, its majority BTTS signal also needs
  **>=60% measured accuracy with >=100 cases**. `BTTS & 3+` needs both BTTS YES and
  Over 2.5 to pass that threshold; `3+` needs Over 2.5.
- Goal-range, first/second-half goal and HT/FT-with-goals selections require a
  reliable majority Over/Under 2.5 signal (>=60%, >=100 cases), plus the
  whole-selection threshold. An Under signal is evidence for calibration, not a
  new permitted Under bet. Plain HT/FT uses only the winner and whole-selection
  thresholds. No BTTS/Goals threshold is required when that component is unused.
- Missing, tied or insufficient binary signals do not pass. These percentages
  are historical hit rates, **not source agreement** or guaranteed match probabilities.
- If no qualifying priced alternative exists, an otherwise eligible combination
  with unknown odds may appear only as a proposal with `n/a`, outside the confirmed
  ticket and its combined odds/return. Known odds below 1.50 are not treated as
  unknown. There is no fallback to a low-priced straight win or a below-threshold pick.

The allowed final selections remain:

- `fix 1`, `fix 2`;
- `1 & 1+ prvo`, `2 & 1+ prvo`;
- `1 & 1+ prvo & 1+ vtoro`, `2 & 1+ prvo & 1+ vtoro`;
- `1 & 2-5 golovi`, `1 & 2-6 golovi`, `2 & 2-5 golovi`, `2 & 2-6 golovi`;
- `BTTS`, `BTTS & 3+`, `3+`;
- `1-1`, `2-2`, `1-1 & 2+`, `2-2 & 2+`.

`1-1` / `2-2` mean half-time/full-time home win / away win, **not exact scores**.
`1+ prvo` / `1+ vtoro` require at least one total goal in the first / second half.
`2+` / `3+` require at least two / three full-time goals; goal ranges are inclusive.
Draws, double chance, BTTS NO and other totals are excluded in all four commands.
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
Combined markets currently have no verified odds mapping: they can only be
unconfirmed proposals. Component odds are never multiplied to invent a combined-market
price. The console and Excel include base odds and winner, BTTS YES/NO and Over/Under
2.5 historical signal accuracy/sample sizes. Excel separates `Tiket` from
`Predlozi - nepotvrdeni`; proposals do not contribute to combined odds or chance.
EV = historical accuracy x odds - 1; picks with EV > 0
are ranked first. `safe` ranks by safety only. The chance shown is measured
history, not a guarantee; combined chance assumes independent matches, and EV is
optimistic because accuracy is averaged per signal, not per league.

### Tests

```powershell
dotnet test .\Tests\Prediction.Tests.csproj
```