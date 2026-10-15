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

### Tests

```powershell
dotnet test .\Tests\Prediction.Tests.csproj
```