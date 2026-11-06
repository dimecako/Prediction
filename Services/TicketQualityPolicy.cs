using System.Globalization;
using Prediction.Entities;

namespace Prediction.Services;

public static class TicketQualityPolicy
{
    public const string Version = "exact-market-v2";
    public const string FtBaselinePrefix = "FT-BASELINE|";
    public const int MaximumSelections = 15;

    public static bool IsWinnerGoals(TicketCandidate candidate) =>
        candidate.Market == "Combo" && candidate.Pick is
            "1 & T1 2+" or "2 & T2 2+" or "1 & 2-5 golovi" or "2 & 2-5 golovi" or
            "1 & 2-6 golovi" or "2 & 2-6 golovi";

    public static bool PassesFtBaseline(ScoredTicketCandidate candidate) =>
        IsWinnerGoals(candidate.Candidate) && candidate.Sample >= TicketSelector.MinComboHistory &&
        double.IsFinite(candidate.Accuracy) && candidate.Accuracy is >= 0 and <= 1 &&
        candidate.Odds is double odds && double.IsFinite(odds) && odds >= TicketSelector.MinFinalOdds &&
        (candidate.Accuracy >= TicketSelector.MinAccuracy ||
            candidate.Accuracy * odds >= TicketSelector.MinComboExpectedReturn);

    public static ScoredTicketCandidate ScoreForGeneration(TicketCandidate candidate, Tally history, double? odds) =>
        Score(IsWinnerGoals(candidate) ? candidate with { Key = FtBaselinePrefix + candidate.Key } : candidate,
            history, odds);

    public static bool IsAllowed(TicketCandidate candidate) => candidate.Market switch
    {
        "1X2" => candidate.Pick is "1" or "2",
        "Goals" => candidate.Pick == "O2.5",
        "BTTS" => candidate.Pick == "YES",
        "Combo" => candidate.Pick is "BTTS & 3+" or "1 & T1 2+" or "2 & T2 2+" or
            "1 & 2-5 golovi" or "2 & 2-5 golovi" or "1 & 2-6 golovi" or "2 & 2-6 golovi",
        _ => false
    };

    public static bool HasCoverage(Match match, TicketCandidate candidate)
    {
        bool Covered(TicketCandidate signal)
        {
            var agreement = TicketMarkets.SourceAgreement(match, signal);
            return agreement.TotalVotes >= 3 && agreement.Share >= 0.60;
        }
        if (candidate.Market != "Combo")
            return Covered(candidate);
        if (candidate.Pick == "BTTS & 3+")
            return Covered(new("BTTS", "YES", "", "")) && Covered(new("Goals", "O2.5", "", ""));
        if (candidate.Pick is "1 & T1 2+" or "2 & T2 2+")
            return Covered(new("1X2", candidate.Pick[..1], "", ""));
        return (candidate.Pick is "1 & 2-5 golovi" or "2 & 2-5 golovi" or
                "1 & 2-6 golovi" or "2 & 2-6 golovi") &&
            Covered(new("1X2", candidate.Pick[..1], "", "")) &&
            TicketMarkets.BinarySignal(match, "Goals") is TicketCandidate goals && Covered(goals);
    }

    public static bool Passes(ScoredTicketCandidate candidate, double minimumOdds)
    {
        int minimumSample = candidate.Candidate.Market == "Combo"
            ? TicketSelector.MinComboHistory : TicketSelector.MinHistory;
        return IsAllowed(candidate.Candidate) && candidate.Sample >= minimumSample &&
            double.IsFinite(candidate.Accuracy) && candidate.Accuracy is >= TicketSelector.MinAccuracy and <= 1 &&
            double.IsFinite(candidate.Safe) && candidate.Safe >= 0 && candidate.Safe <= candidate.Accuracy &&
            candidate.Odds is double odds && double.IsFinite(odds) && odds >= minimumOdds &&
            candidate.Accuracy * odds >= TicketSelector.MinComboExpectedReturn &&
            candidate.Safe * odds >= 1.0;
    }

    public static ScoredTicketCandidate Score(TicketCandidate candidate, Tally history, double? odds)
    {
        var stat = history.Get(candidate.Key);
        return new(candidate, stat.Total == 0 ? 0 : (double)stat.Correct / stat.Total,
            CalibrationAnalyzer.WilsonLower(stat.Correct, stat.Total), stat.Total, odds);
    }

    public static string Evidence(ScoredTicketCandidate score) => string.Create(CultureInfo.InvariantCulture,
        $"Policy={Version}; FT policy={(score.Candidate.Key.StartsWith(FtBaselinePrefix, StringComparison.Ordinal) ? "previous eligibility baseline; strict challenger not promoted" : "strict exact-market")}; Exact history={score.Accuracy:P1} (n={score.Sample}); Wilson95 lower={score.Safe:P1}; Historical return={score.Accuracy * score.Odds:F3}; historical estimates, not guaranteed probabilities");

    public static List<Match> PrepareHistory(IEnumerable<Match> matches, DateOnly target, Action<string> log)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");
        var cutoff = TimeZoneInfo.ConvertTimeToUtc(target.ToDateTime(TimeOnly.MinValue), zone);
        var input = matches.ToList();
        var conflicts = input.Where(m => m.MatchDate < target && m.Result != null &&
                m.Result.CollectedAtUtc != default && m.Result.CollectedAtUtc < cutoff)
            .GroupBy(m => (m.MatchDate, Home: m.HomeTeam.Trim().ToUpperInvariant(),
                Away: m.AwayTeam.Trim().ToUpperInvariant()))
            .Where(g => g.Select(m => (m.Result!.HomeGoals, m.Result.AwayGoals)).Distinct().Count() > 1)
            .Select(g => g.Key).ToHashSet();
        var prepared = new List<Match>();
        foreach (var match in input)
        {
            if (match.MatchDate >= target || match.Result == null ||
                match.Result.CollectedAtUtc == default || match.Result.CollectedAtUtc >= cutoff ||
                conflicts.Contains((match.MatchDate, match.HomeTeam.Trim().ToUpperInvariant(),
                    match.AwayTeam.Trim().ToUpperInvariant())))
                continue;
            var startOfDay = TimeZoneInfo.ConvertTimeToUtc(match.MatchDate.ToDateTime(TimeOnly.MinValue), zone);
            // Legacy midnight kickoffs are date placeholders, not evidence of a prematch snapshot.
            var kickoff = match.KickoffUtc is DateTime real &&
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(real, zone)) == match.MatchDate &&
                TimeZoneInfo.ConvertTimeFromUtc(real, zone).TimeOfDay != TimeSpan.Zero
                ? real : startOfDay;
            var snapshots = match.PredictionSnapshots.Where(s => s.CapturedAtUtc != default && s.CapturedAtUtc < kickoff &&
                s.CapturedAtUtc < cutoff).ToList();
            if (snapshots.Count == 0 || snapshots.Any(s => s.Confidence is double value &&
                    (!double.IsFinite(value) || value is < 0 or > 1)))
                continue;
            prepared.Add(new Match
            {
                Id = match.Id, ExternalMatchId = match.ExternalMatchId, HomeTeam = match.HomeTeam,
                AwayTeam = match.AwayTeam, MatchDate = match.MatchDate, KickoffUtc = match.KickoffUtc,
                League = match.League, Result = match.Result, PredictionSnapshots = snapshots
            });
        }
        var unique = prepared.GroupBy(m => (m.MatchDate, Home: m.HomeTeam.Trim().ToUpperInvariant(),
                Away: m.AwayTeam.Trim().ToUpperInvariant()))
            .Where(g => g.Select(m => (m.Result!.HomeGoals, m.Result.AwayGoals)).Distinct().Count() == 1)
            .Select(g => g.OrderByDescending(m => CalibrationAnalyzer.LatestPerSource(m).Count)
                .ThenBy(m => m.Id).First()).ToList();
        log($"[QUALITY] {Version}; pre-date, prematch history={unique.Count}/{input.Count}. " +
            "Late/unknown/invalid snapshots, unavailable results and duplicate/conflicting fixtures excluded.");
        return unique;
    }

    public static Tally Calibrate(IEnumerable<Match> history)
    {
        var tally = new Tally();
        foreach (var match in history)
            foreach (var candidate in TicketMarkets.BuildCalibrationCandidates(match, includeTeamGoals: true)
                         .Where(c => IsAllowed(c) || c.Key.StartsWith("SIGNAL|Goals|", StringComparison.Ordinal))
                         .DistinctBy(c => c.Key))
                if (match.Result != null && TicketMarkets.IsCorrect(candidate, match.Result) is bool correct)
                {
                    if (IsWinnerGoals(candidate) || candidate.Market == "1X2" ||
                        candidate.Key.StartsWith("SIGNAL|Goals|", StringComparison.Ordinal))
                        tally.Add(FtBaselinePrefix + candidate.Key, correct);
                    if ((IsAllowed(candidate) || candidate.Key.StartsWith("SIGNAL|Goals|", StringComparison.Ordinal)) &&
                        HasCoverage(match, candidate))
                        tally.Add(candidate.Key, correct);
                }
        return tally;
    }
}
