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
        bool includeTeamGoals = false)
    {
        var candidates = TicketMarkets.BuildCandidates(match, includeTeamGoals).ToList();
        var fix = candidates.FirstOrDefault(x => x.Market == "1X2");
        if (fix == null)
            return new(null, null, null, "Nema nedvosmislen 1/2 signal so Prob.");
        double? baseOdds = getOdds(fix);
        if (!baseOdds.HasValue || !double.IsFinite(baseOdds.Value) || baseOdds <= 1)
            return new(null, null, baseOdds, "Nema validna potvrdena osnovna kvota.");
        bool Reliable(TicketCandidate c, double minimum, int minSamples = MinHistory)
        {
            var stat = calibration.Get(c.Key);
            return stat.Total >= minSamples && (double)stat.Correct / stat.Total >= minimum;
        }
        ScoredTicketCandidate Score(TicketCandidate c)
        {
            var stat = calibration.Get(c.Key);
            double? odds = getOdds(c);
            if (odds.HasValue && (!double.IsFinite(odds.Value) || odds <= 1))
                throw new InvalidOperationException($"Invalid odds for {c.DisplayPick}.");
            return new(c, (double)stat.Correct / stat.Total,
                CalibrationAnalyzer.WilsonLower(stat.Correct, stat.Total), stat.Total, odds);
        }
        bool highOdds = baseOdds > MaxFixOdds;
        if (!highOdds && !Reliable(fix, MinAccuracy))
            return new(null, null, baseOdds, "Pobedata nema >=55% tocnost i 100+ primeri.");
        if (baseOdds >= KeepFixOdds && baseOdds <= MaxFixOdds)
            return new(Score(fix), null, baseOdds, "Osnovna kvota 1.70-2.00: samo fix.");

        var btts = TicketMarkets.BinarySignal(match, "BTTS");
        var goals = TicketMarkets.BinarySignal(match, "Goals");
        bool bttsYes = btts is { Pick: "YES" } && Reliable(btts, MinBttsSignalAccuracy);
        bool goalsReliable = goals != null && Reliable(goals, MinSignalAccuracy);
        bool over = goals is { Pick: "O2.5" } && goalsReliable;
        bool SignalsAllow(TicketCandidate c)
        {
            if (highOdds)
                return (c.Market == "HalfGoals" && over) ||
                    (c.Market == "Goals" && over) ||
                    (c.Market == "BTTS" && bttsYes) ||
                    (c.Pick == "BTTS & 3+" && bttsYes && over);
            if (baseOdds < MinBaseOdds)
            {
                if (c.Pick == $"{fix.Pick}-{fix.Pick}")
                    return true;
                if (c.Pick == $"{fix.Pick}-{fix.Pick} & 2+")
                    return goalsReliable;
            }
            if (c.Market != "Combo" || !c.Pick.StartsWith($"{fix.Pick} & ", StringComparison.Ordinal))
                return false;
            if (c.Pick == $"{fix.Pick} & T{fix.Pick} 2+")
                return true;
            return goalsReliable;
        }
        bool CandidatePassesThreshold(ScoredTicketCandidate x)
        {
            bool isComposite = x.Candidate.Market is "Combo" or "HalfGoals";
            int minSamples = isComposite ? MinComboHistory : MinHistory;
            if (x.Sample < minSamples)
                return false;
            if (x.Accuracy >= MinAccuracy)
                return true;
            return isComposite && x.Odds.HasValue && x.Accuracy * x.Odds.Value >= MinComboExpectedReturn;
        }
        var eligible = candidates.Where(x => x.Market != "1X2" &&
                calibration.Get(x.Key).Total > 0 && SignalsAllow(x))
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
        var proposal = eligible.Where(x => x.Odds == null && x.Accuracy >= MinAccuracy)
            .OrderByDescending(x => x.Safe).ThenByDescending(x => x.Accuracy).FirstOrDefault();
        return new(null, proposal, baseOdds, proposal == null
            ? "Nema dozvolena alternativa so potrebna tocnost, signali i kvota."
            : "Predlog: nedostasuva potvrdena kvota; NE e del od potvrdeniot tiket.");
    }

    public static TicketDecision SelectFirstHalf1To3And3Plus(
        Match match, Tally calibration, Func<TicketCandidate, double?> getOdds)
    {
        var fix = TicketMarkets.BuildCandidates(match).FirstOrDefault(x => x.Market == "1X2");
        double? baseOdds = (fix != null ? getOdds(fix) : null)
            ?? getOdds(new("Goals", "O2.5", "", ""))
            ?? getOdds(new("1X2", "1", "", ""))
            ?? getOdds(new("1X2", "2", "", ""));
        if (!baseOdds.HasValue || !double.IsFinite(baseOdds.Value) || baseOdds <= 1)
            return new(null, null, baseOdds, "Nema validna potvrdena osnovna kvota.");

        var goals = TicketMarkets.BinarySignal(match, "Goals");
        var goalsStat = goals != null ? calibration.Get(goals.Key) : default;
        if (goals is not { Pick: "O2.5" } ||
            goalsStat.Total < MinHistory ||
            (double)goalsStat.Correct / goalsStat.Total < MinSignalAccuracy)
        {
            return new(null, null, baseOdds, "Nema doverliv Goals Over 2.5 signal (>=60%, n>=100).");
        }

        string key = $"HALFGOALS|{TicketMarkets.FirstHalf1To3And3PlusPick}";
        var stat = calibration.Get(key);
        if (stat.Total < MinComboHistory)
            return new(null, null, baseOdds, "Nedovolna istorija so poluvreme za 1-3 prvo & 3+ (n<50).");

        var candidate = new TicketCandidate("HalfGoals", TicketMarkets.FirstHalf1To3And3PlusPick, key, "");
        double? odds = getOdds(candidate);
        if (odds.HasValue && (!double.IsFinite(odds.Value) || odds <= 1))
            throw new InvalidOperationException($"Invalid odds for {candidate.DisplayPick}.");

        var scored = new ScoredTicketCandidate(
            candidate,
            (double)stat.Correct / stat.Total,
            CalibrationAnalyzer.WilsonLower(stat.Correct, stat.Total),
            stat.Total,
            odds);

        if (odds.HasValue)
        {
            if (odds.Value < MinFinalOdds)
                return new(null, null, baseOdds, "Kvotata za 1-3 prvo & 3+ e pod 1.70.");
            return new(scored, null, baseOdds, "Potvrden izbor 1-3 prvo & 3+ so silen Over 2.5 signal.");
        }

        return new(null, scored, baseOdds,
            "Predlog: nedostasuva potvrdena kvota za 1-3 prvo & 3+; NE e del od potvrdeniot tiket.");
    }
}
