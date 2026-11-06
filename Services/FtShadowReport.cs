using System.Globalization;
using Prediction.Entities;

namespace Prediction.Services;

public sealed record FtShadowStats(int Offered, int Qualified, int Selected, int Won, int Lost, int Pending,
    decimal SinglesProfit, double? Accuracy, double? Lower, double? Upper, decimal? SinglesRoi)
{
    public int Graded => Won + Lost;
}

public sealed record FtShadowComparison(string Pick, FtShadowStats Baseline, FtShadowStats Challenger,
    int SharedSelected, int BaselineOnlySelected, int ChallengerOnlySelected);

public static class FtShadowReport
{
    public static IReadOnlyList<FtShadowComparison> Compare(IEnumerable<FtShadowCapture> captures,
        IReadOnlyDictionary<long, MatchResult> results)
    {
        var runs = captures.ToList();
        if (runs.Select(r => (r.Experiment, r.Date)).Distinct().Count() != runs.Count)
            throw new ArgumentException("A shadow report cannot count the same experiment/date twice.");
        var rows = runs.SelectMany(r => r.Fixtures.SelectMany(f => f.Offers.Select(o =>
        {
            bool? correct = results.TryGetValue(f.MatchId, out var result)
                ? TicketMarkets.IsCorrect(new("Combo", o.Pick, o.CalibrationKey, ""), result) : null;
            return (Offer: o, Correct: correct);
        }))).ToList();
        var types = new[] { "ALL", "1 & 2-5 golovi", "2 & 2-5 golovi", "1 & 2-6 golovi",
            "2 & 2-6 golovi", "1 & T1 2+", "2 & T2 2+" };
        return types.Select(type =>
        {
            var group = rows.Where(r => type == "ALL" || r.Offer.Pick == type).ToList();
            FtShadowStats Stats(bool strict)
            {
                var selected = group.Where(r => strict ? r.Offer.StrictPosition.HasValue :
                    r.Offer.BaselinePosition.HasValue).ToList();
                int won = selected.Count(r => r.Correct == true), lost = selected.Count(r => r.Correct == false);
                int graded = won + lost;
                decimal profit = selected.Where(r => r.Correct.HasValue)
                    .Sum(r => r.Correct == true ? Convert.ToDecimal(r.Offer.Odds!.Value) - 1m : -1m);
                return new(group.Count, group.Count(r => strict ? r.Offer.StrictQualified : r.Offer.BaselineQualified),
                    selected.Count, won, lost, selected.Count - graded, profit,
                    graded == 0 ? null : (double)won / graded,
                    graded == 0 ? null : CalibrationAnalyzer.WilsonLower(won, graded),
                    graded == 0 ? null : 1 - CalibrationAnalyzer.WilsonLower(lost, graded),
                    graded == 0 ? null : profit / graded);
            }
            return new FtShadowComparison(type, Stats(false), Stats(true),
                group.Count(r => r.Offer.BaselinePosition.HasValue && r.Offer.StrictPosition.HasValue),
                group.Count(r => r.Offer.BaselinePosition.HasValue && !r.Offer.StrictPosition.HasValue),
                group.Count(r => !r.Offer.BaselinePosition.HasValue && r.Offer.StrictPosition.HasValue));
        }).ToList();
    }

    public static void Write(IReadOnlyList<FtShadowCapture> captures, IReadOnlyDictionary<long, MatchResult> results,
        Action<string> log)
    {
        log($"[FT-SHADOW] Experiment={FtShadowEvaluation.Experiment}; prospective frozen paired simulation only.");
        log("[FT-SHADOW] Active tickets/rules are NOT modified. Baseline and challenger see the SAME offers, prices and snapshots.");
        if (captures.Count == 0)
        {
            log("[FT-SHADOW] No frozen prospective runs in range. No historical backfill or inferred improvement.");
            return;
        }
        log($"[FT-SHADOW] Captures={captures.Count}; dates={captures.Min(r => r.Date):yyyy-MM-dd}..{captures.Max(r => r.Date):yyyy-MM-dd}; " +
            $"first capture UTC={captures.Min(r => r.CapturedAtUtc):O}; fixtures={captures.Sum(r => r.Fixtures.Count)}; " +
            $"unmatched={captures.Sum(r => r.Unmatched)} ambiguous={captures.Sum(r => r.Ambiguous)} " +
            $"started={captures.Sum(r => r.Started)} known-results={captures.Sum(r => r.KnownResults)} duplicates={captures.Sum(r => r.Duplicates)}.");
        foreach (var run in captures.OrderBy(r => r.Date))
            log($"[FT-SHADOW-DAY] {run.Date:yyyy-MM-dd}; captured={run.CapturedAtUtc:O}; requested={run.RequestedCount}; " +
                $"baseline={run.Fixtures.Sum(f => f.Offers.Count(o => o.BaselinePosition.HasValue))}; " +
                $"challenger={run.Fixtures.Sum(f => f.Offers.Count(o => o.StrictPosition.HasValue))}; history={run.HistoricalMatches}.");
        foreach (var comparison in Compare(captures, results))
        {
            Print("BASELINE", comparison.Baseline);
            Print("CHALLENGER", comparison.Challenger);
            log($"[FT-SHADOW-PAIR] {comparison.Pick}; identical selected events={comparison.SharedSelected}; " +
                $"baseline-only={comparison.BaselineOnlySelected}; challenger-only={comparison.ChallengerOnlySelected}.");
            bool enoughForReview = captures.Count >= 30 && comparison.Baseline.Graded >= 100 &&
                comparison.Challenger.Graded >= 100;
            log($"[FT-SHADOW-STATUS] {comparison.Pick}: {(enoughForReview ? "MANUAL REVIEW ONLY" : "INSUFFICIENT EVIDENCE")}; " +
                "30 captured dates and 100 graded selections PER arm/type are a review floor, NOT a significance test or proof of superiority. No auto-promotion.");
            void Print(string arm, FtShadowStats stats)
            {
                string Percent(double? value) => value.HasValue
                    ? value.Value.ToString("P1", CultureInfo.InvariantCulture) : "n/a";
                log($"[FT-SHADOW-{arm}] {comparison.Pick}; offered={stats.Offered}; qualified={stats.Qualified}; " +
                    $"selected={stats.Selected}; won={stats.Won} lost={stats.Lost} pending={stats.Pending}; " +
                    $"accuracy={Percent(stats.Accuracy)}; Wilson95={Percent(stats.Lower)}..{Percent(stats.Upper)}; " +
                    $"coverage={(stats.Offered == 0 ? "n/a" : ((double)stats.Selected / stats.Offered).ToString("P1", CultureInfo.InvariantCulture))}; " +
                    $"hypothetical singles profit={stats.SinglesProfit.ToString("F2", CultureInfo.InvariantCulture)}; " +
                    $"singles ROI={(stats.SinglesRoi.HasValue ? stats.SinglesRoi.Value.ToString("P1", CultureInfo.InvariantCulture) : "n/a")}.");
            }
        }
        log("[FT-SHADOW] Counts are selected unique fixtures per arm/day, not repeated ticket occurrences. Missing FT stays Pending.");
        log("[FT-SHADOW] ROI assumes flat 1-unit SINGLES, NOT accumulator return. Outcomes/types/arms overlap; Wilson intervals are marginal descriptive estimates, not a paired improvement test.");
        log("[FT-SHADOW] Greater accuracy alone is not promotion evidence: compare coverage, pending results, ROI and future untouched dates. Human review required.");
    }
}
