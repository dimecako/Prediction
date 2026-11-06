using Prediction.Entities;

namespace Prediction.Services;

public sealed record ScoredTicketCandidate(
    TicketCandidate Candidate, double Accuracy, double Safe, int Sample, double? Odds);

public sealed record TicketDecision(
    ScoredTicketCandidate? Confirmed,
    ScoredTicketCandidate? Proposal,
    double? BaseOdds,
    string Reason);

public static class TicketSelector
{
    public static bool HasRequiredData(Match match, Func<TicketCandidate, double?> getOdds)
    {
        var votes = CalibrationAnalyzer.LatestPerSource(match)
            .Where(x => x.PredictedResult is "1" or "X" or "2").ToList();
        if (votes.Count == 0 || votes.Any(x => x.Confidence is double value &&
                (!double.IsFinite(value) || value is < 0 or > 1)))
            return false;
        return votes.Select(x => x.PredictedResult!).Distinct()
            .Any(pick => getOdds(new("1X2", pick, "", "")) is double price &&
                double.IsFinite(price) && price > 1);
    }

    public const int MinHistory = 100;
    public const int MinComboHistory = 50;
    public const double MinAccuracy = 0.55;
    public const double MinSignalAccuracy = 0.60;
    public const double MinBttsSignalAccuracy = 0.55;
    public const double MinComboExpectedReturn = 1.05;
    public const double MinBaseOdds = 1.40;
    public const double KeepFixOdds = 1.70;
    public const double MaxFixOdds = 2.00;
    public const double MinFinalOdds = 1.70;
    public const double MinBttsOdds = 1.65;

    public static TicketDecision Select(
        Match match, Tally calibration, Func<TicketCandidate, double?> getOdds, bool safe,
        bool includeTeamGoals = false, bool winnerGoalsOnly = false)
    {
        if (!HasRequiredData(match, getOdds))
            return new(null, null, null, "Missing or invalid source votes, probabilities or base prices.");
        var candidates = TicketMarkets.BuildCandidates(match, includeTeamGoals)
            .Where(TicketQualityPolicy.IsAllowed).ToList();
        var fix = candidates.FirstOrDefault(x => x.Market == "1X2");
        if (fix == null)
            return new(null, null, null, "Nema nedvosmislen 1/2 signal so Prob.");
        double? baseOdds = getOdds(fix);
        if (!baseOdds.HasValue || !double.IsFinite(baseOdds.Value) || baseOdds <= 1)
            return new(null, null, baseOdds, "Nema validna potvrdena osnovna kvota.");
        bool Reliable(TicketCandidate c, double minimum, int minSamples = MinHistory, bool ftBaseline = false)
        {
            var stat = calibration.Get(ftBaseline ? TicketQualityPolicy.FtBaselinePrefix + c.Key : c.Key);
            return stat.Total >= minSamples && (double)stat.Correct / stat.Total >= minimum;
        }
        ScoredTicketCandidate Score(TicketCandidate c)
        {
            double? odds = getOdds(c);
            if (odds.HasValue && (!double.IsFinite(odds.Value) || odds <= 1))
                throw new InvalidOperationException($"Invalid odds for {c.DisplayPick}.");
            return TicketQualityPolicy.ScoreForGeneration(c, calibration, odds);
        }
        bool highOdds = baseOdds > MaxFixOdds;
        if ((winnerGoalsOnly || baseOdds < KeepFixOdds) &&
            !Reliable(fix, MinAccuracy, ftBaseline: true))
            return new(null, null, baseOdds, "FT baseline winner needs >=55% accuracy and 100+ cases.");
        if (!winnerGoalsOnly && !highOdds && baseOdds >= KeepFixOdds && !Reliable(fix, MinAccuracy))
            return new(null, null, baseOdds, "Pobedata nema >=55% tocnost i 100+ primeri.");
        if (!winnerGoalsOnly && baseOdds >= KeepFixOdds && baseOdds <= MaxFixOdds)
            return TicketQualityPolicy.HasCoverage(match, fix) &&
                TicketQualityPolicy.Passes(Score(fix), KeepFixOdds)
                ? new(Score(fix), null, baseOdds, "Exact-market history and conservative return passed.")
                : new(null, null, baseOdds, "Fix rejected: insufficient coverage, history or expected return.");

        var btts = TicketMarkets.BinarySignal(match, "BTTS");
        var goals = TicketMarkets.BinarySignal(match, "Goals");
        bool bttsYes = btts is { Pick: "YES" } && Reliable(btts, MinBttsSignalAccuracy);
        bool goalsReliable = goals != null && Reliable(goals, MinSignalAccuracy);
        bool ftGoalsReliable = goals != null && Reliable(goals, MinSignalAccuracy, ftBaseline: true);
        bool over = goals is { Pick: "O2.5" } && goalsReliable;
        bool SignalsAllow(TicketCandidate c)
        {
            if (highOdds && !winnerGoalsOnly)
                return (c.Market == "Goals" && over) ||
                    (c.Market == "BTTS" && bttsYes) ||
                    (c.Pick == "BTTS & 3+" && bttsYes && over);
            if (!TicketQualityPolicy.IsWinnerGoals(c) ||
                !c.Pick.StartsWith($"{fix.Pick} & ", StringComparison.Ordinal))
                return false;
            if (c.Pick == $"{fix.Pick} & T{fix.Pick} 2+")
                return true;
            return ftGoalsReliable;
        }
        bool CandidatePassesThreshold(ScoredTicketCandidate x)
        {
            return TicketQualityPolicy.IsWinnerGoals(x.Candidate) ? TicketQualityPolicy.PassesFtBaseline(x) :
                TicketQualityPolicy.HasCoverage(match, x.Candidate) &&
                TicketQualityPolicy.Passes(x, x.Candidate.Market == "BTTS" ? MinBttsOdds : MinFinalOdds);
        }
        var eligible = candidates.Where(x => x.Market != "1X2" &&
                SignalsAllow(x))
            .Select(Score)
            .Where(CandidatePassesThreshold)
            .ToList();
        var confirmed = eligible.Where(x => x.Odds >=
                (highOdds && x.Candidate.Market == "BTTS" ? MinBttsOdds : MinFinalOdds))
            .OrderByDescending(x => !safe && x.Accuracy * x.Odds > 1)
            .ThenByDescending(x => safe ? x.Safe : x.Safe * x.Odds!.Value)
            .ThenByDescending(x => x.Accuracy).FirstOrDefault();
        if (confirmed != null)
            return new(confirmed, null, baseOdds, "Potvrdena alternativa so potrebna kvota.");
        return new(null, null, baseOdds,
            "No exact-market candidate passed coverage, sample, accuracy and conservative-return thresholds.");
    }

    public static TicketDecision SelectFirstHalf1To3And3Plus(
        Match match, Tally calibration, Func<TicketCandidate, double?> getOdds)
    {
        return new(null, null, null, "Retired: half-time selections are disabled. Historical settlement remains available.");
    }
}
