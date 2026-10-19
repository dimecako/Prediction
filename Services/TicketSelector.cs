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
    public const double MinAccuracy = 0.55;
    public const double MinSignalAccuracy = 0.60;
    public const double MinBaseOdds = 1.40;
    public const double KeepFixOdds = 1.70;
    public const double MinFinalOdds = 1.50;

    public static TicketDecision Select(
        Match match, Tally calibration, Func<TicketCandidate, double?> getOdds, bool safe)
    {
        var candidates = TicketMarkets.BuildCandidates(match).ToList();
        var fix = candidates.FirstOrDefault(x => x.Market == "1X2");
        if (fix == null)
            return new(null, null, null, "Nema nedvosmislen 1/2 signal so Prob.");
        double? baseOdds = getOdds(fix);
        if (!baseOdds.HasValue || !double.IsFinite(baseOdds.Value) || baseOdds < MinBaseOdds)
            return new(null, null, baseOdds, "Nema potvrdena osnovna kvota >=1.40.");
        bool Reliable(TicketCandidate c, double minimum)
        {
            var stat = calibration.Get(c.Key);
            return stat.Total >= MinHistory && (double)stat.Correct / stat.Total >= minimum;
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
        if (!Reliable(fix, MinAccuracy))
            return new(null, null, baseOdds, "Pobedata nema >=55% tocnost i 100+ primeri.");
        if (baseOdds >= KeepFixOdds)
            return new(Score(fix), null, baseOdds, "Osnovna kvota >=1.70: ostanuva fix.");

        var btts = TicketMarkets.BinarySignal(match, "BTTS");
        var goals = TicketMarkets.BinarySignal(match, "Goals");
        bool bttsYes = btts is { Pick: "YES" } && Reliable(btts, MinSignalAccuracy);
        bool goalsReliable = goals != null && Reliable(goals, MinSignalAccuracy);
        bool over = goals is { Pick: "O2.5" } && goalsReliable;
        bool SignalsAllow(TicketCandidate c)
        {
            if (c.Market == "BTTS")
                return bttsYes;
            if (c.Market == "Goals")
                return over;
            if (c.Pick == "BTTS & 3+")
                return bttsYes && over;
            // HT/FT alone uses no BTTS or Goals component.
            if (c.Pick == $"{fix.Pick}-{fix.Pick}")
                return true;
            return goalsReliable;
        }
        var eligible = candidates.Where(x => x.Market != "1X2" &&
                Reliable(x, MinAccuracy) && SignalsAllow(x))
            .Select(Score).ToList();
        var confirmed = eligible.Where(x => x.Odds >= MinFinalOdds)
            .OrderByDescending(x => !safe && x.Accuracy * x.Odds > 1)
            .ThenByDescending(x => safe ? x.Safe : x.Safe * x.Odds!.Value)
            .ThenByDescending(x => x.Accuracy).FirstOrDefault();
        if (confirmed != null)
            return new(confirmed, null, baseOdds, "Potvrdena alternativa so kvota >=1.50.");
        var proposal = eligible.Where(x => x.Odds == null)
            .OrderByDescending(x => x.Safe).ThenByDescending(x => x.Accuracy).FirstOrDefault();
        return new(null, proposal, baseOdds, proposal == null
            ? "Nema dozvolena alternativa so potrebna tocnost, signali i kvota."
            : "Predlog: nedostasuva potvrdena kvota; NE e del od potvrdeniot tiket.");
    }
}
