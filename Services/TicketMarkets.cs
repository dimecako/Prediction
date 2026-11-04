using Prediction.Entities;

namespace Prediction.Services;

public sealed record TicketCandidate(string Market, string Pick, string Key, string Detail)
{
    public string DisplayPick => (Market, Pick) switch
    {
        ("1X2", _) => $"fix {Pick}",
        ("BTTS", "YES") => "BTTS",
        ("Goals", "O2.5") => "3+",
        _ => Pick
    };
}

public sealed record TicketSourceAgreement(int MatchingVotes, int TotalVotes)
{
    public double Share => TotalVotes == 0 ? 0 : (double)MatchingVotes / TotalVotes;
}

public static class TicketMarkets
{
    public static readonly IReadOnlyList<string> HalfGoalPicks = Array.AsReadOnly(new[]
    {
        "1-2 prvo & 1-2 vtoro",
        "1-3 prvo & 1-3 vtoro",
        FirstHalf1To3And3PlusPick
    });

    public const string FirstHalf1To3And3PlusPick = "1-3 prvo & 3+";

    public static IEnumerable<TicketCandidate> BuildCalibrationCandidates(Match match, bool includeTeamGoals = false)
    {
        foreach (var candidate in BuildCandidates(match, includeTeamGoals))
            yield return candidate;
        var snapshots = CalibrationAnalyzer.LatestPerSource(match);
        foreach (var group in snapshots.Where(x => x.Btts is "YES" or "NO").GroupBy(x => x.Btts!))
            yield return new("BTTS", group.Key, $"SIGNAL|BTTS|{group.Key}|{Math.Min(group.Count(), 3)}", "");
        var goals = snapshots.Select(x => ProductionConsensusAggregator.NormalizeGoalsTo25(x.Goals));
        foreach (var group in goals.Where(x => x is "O2.5" or "U2.5").GroupBy(x => x!))
            yield return new("Goals", group.Key, $"SIGNAL|Goals|{group.Key}|{Math.Min(group.Count(), 3)}", "");
    }

    public static TicketCandidate? BinarySignal(Match match, string market)
    {
        var snapshots = CalibrationAnalyzer.LatestPerSource(match);
        var votes = market switch
        {
            "BTTS" => snapshots.Select(x => x.Btts is "YES" or "NO" ? x.Btts : null),
            "Goals" => snapshots.Select(x => ProductionConsensusAggregator.NormalizeGoalsTo25(x.Goals)),
            _ => throw new ArgumentException($"Unsupported signal market: {market}", nameof(market))
        };
        var consensus = CalibrationAnalyzer.BuildBinaryConsensus(votes);
        if (consensus == null)
            return null;
        int matching = votes.Count(x => x == consensus.Value.Pick);
        return new(market, consensus.Value.Pick,
            $"SIGNAL|{market}|{consensus.Value.Pick}|{Math.Min(matching, 3)}", "");
    }

    public static TicketSourceAgreement SourceAgreement(Match match, TicketCandidate candidate)
    {
        var snapshots = CalibrationAnalyzer.LatestPerSource(match);

        TicketSourceAgreement ForResult(string pick)
        {
            var votes = snapshots.Where(x => x.PredictedResult is "1" or "X" or "2").ToList();
            return new(votes.Count(x => x.PredictedResult == pick), votes.Count);
        }

        TicketSourceAgreement ForBinary(string market, string pick)
        {
            var votes = BinaryVotes(snapshots, market);
            return new(votes.Count(x => x == pick), votes.Count);
        }

        TicketSourceAgreement Minimum(TicketSourceAgreement first, TicketSourceAgreement second) =>
            first.Share <= second.Share ? first : second;

        return candidate.Market switch
        {
            "1X2" => ForResult(candidate.Pick),
            "BTTS" => ForBinary("BTTS", candidate.Pick),
            "Goals" => ForBinary("Goals", candidate.Pick),
            "HalfGoals" => ForBinary("Goals", "O2.5"),
            "Combo" when candidate.Pick == "BTTS & 3+" =>
                Minimum(ForBinary("BTTS", "YES"), ForBinary("Goals", "O2.5")),
            "Combo" when candidate.Pick is "1 & T1 2+" or "2 & T2 2+" =>
                ForResult(candidate.Pick[..1]),
            "Combo" when candidate.Pick.StartsWith("1 & ", StringComparison.Ordinal) ||
                         candidate.Pick.StartsWith("2 & ", StringComparison.Ordinal) =>
                Minimum(ForResult(candidate.Pick[..1]), MajorityBinaryAgreement(snapshots, "Goals")),
            "Combo" when candidate.Pick is "1-1" or "2-2" or "1-1 & 2+" or "2-2 & 2+" =>
                ForResult(candidate.Pick[..1]),
            _ => new(0, 0)
        };
    }

    private static TicketSourceAgreement MajorityBinaryAgreement(
        IReadOnlyList<PredictionSnapshot> snapshots, string market)
    {
        var votes = BinaryVotes(snapshots, market);
        if (votes.Count == 0)
            return new(0, 0);

        var groups = votes.GroupBy(x => x).OrderByDescending(x => x.Count()).ToList();
        if (groups.Count > 1 && groups[0].Count() == groups[1].Count())
            return new(0, votes.Count);
        return new(groups[0].Count(), votes.Count);
    }

    private static List<string?> BinaryVotes(
        IReadOnlyList<PredictionSnapshot> snapshots, string market) =>
        market switch
        {
            "BTTS" => snapshots.Select(x => x.Btts is "YES" or "NO" ? x.Btts : null)
                .Where(x => x != null).ToList(),
            "Goals" => snapshots.Select(x => ProductionConsensusAggregator.NormalizeGoalsTo25(x.Goals))
                .Where(x => x is "O2.5" or "U2.5").ToList(),
            _ => throw new ArgumentException($"Unsupported signal market: {market}", nameof(market))
        };

    public static IEnumerable<TicketCandidate> BuildCandidates(
        Match match, bool includeTeamGoals = false, bool requireProbability = true)
    {
        var snapshots = CalibrationAnalyzer.LatestPerSource(match);
        var consensus = CalibrationAnalyzer.BuildResultConsensus(snapshots);
        if (consensus is { Pick: "1" or "2" } &&
            (!requireProbability || consensus.AverageProbability.HasValue))
        {
            string side = consensus.Pick;
            string bucket = consensus.AverageProbability is double probability
                ? CalibrationAnalyzer.ProbBucket(probability) : "unknown";
            string probabilityText = consensus.AverageProbability is double p ? $"{p:F1}%" : "n/a";
            string detail = $"Prob={probabilityText} {consensus.MatchingVotes}/{consensus.TotalVotes}";
            yield return new("1X2", side, $"1X2|{side}|{bucket}", detail);
            foreach (string pick in new[]
            {
                $"{side} & 1+ prvo",
                $"{side} & 1+ prvo & 1+ vtoro",
                $"{side} & 2-5 golovi",
                $"{side} & 2-6 golovi",
                $"{side}-{side}",
                $"{side}-{side} & 2+"
            })
                yield return new("Combo", pick, $"COMBO|{pick}|{bucket}", detail);
            if (includeTeamGoals)
            {
                string pick = $"{side} & T{side} 2+";
                yield return new("Combo", pick, $"COMBO|{pick}|{bucket}", detail);
            }
        }

        int bttsVotes = snapshots.Count(x => x.Btts == "YES");
        int overVotes = snapshots.Count(x => x.Goals?.Trim().ToUpperInvariant() is "O2.5" or "O3.5");
        if (bttsVotes > 0)
        {
            string key = $"BTTS|YES|{Math.Min(bttsVotes, 3)}";
            yield return new("BTTS", "YES", key, $"{bttsVotes} sources");
            if (overVotes > 0)
                yield return new("Combo", "BTTS & 3+",
                    $"COMBO|BTTS & 3+|{Math.Min(bttsVotes, 3)}|{Math.Min(overVotes, 3)}",
                    $"{bttsVotes} BTTS / {overVotes} 3+ sources");
        }
        if (overVotes > 0)
            yield return new("Goals", "O2.5", $"GOALS|O2.5|{Math.Min(overVotes, 3)}", $"{overVotes} sources");
        foreach (string pick in HalfGoalPicks)
            yield return new("HalfGoals", pick, $"HALFGOALS|{pick}", "");
    }

    // Calibration keeps complete half-time samples; settlement may resolve a proven final-total loss.
    public static bool? IsCorrect(TicketCandidate candidate, MatchResult result, bool allowFinalOnlyLoss = false)
    {
        int total = result.HomeGoals + result.AwayGoals;
        string winner = result.HomeGoals > result.AwayGoals ? "1" :
            result.HomeGoals < result.AwayGoals ? "2" : "X";
        if (candidate.Market == "1X2")
            return candidate.Pick == winner;
        if (candidate.Market == "BTTS" && candidate.Pick is "YES" or "NO")
            return (result.HomeGoals > 0 && result.AwayGoals > 0) == (candidate.Pick == "YES");
        if (candidate.Market == "Goals" && candidate.Pick is "O2.5" or "U2.5")
            return (total >= 3) == (candidate.Pick == "O2.5");
        if (candidate.Market == "HalfGoals")
        {
            if (!HalfGoalPicks.Contains(candidate.Pick))
                throw new ArgumentException($"Unsupported ticket pick: {candidate.Pick}");
            if (result.HalfTimeHomeGoals is not int htHome || result.HalfTimeAwayGoals is not int htAway)
                return !allowFinalOnlyLoss ? null : candidate.Pick switch
                {
                    "1-2 prvo & 1-2 vtoro" when total is < 2 or > 4 => false,
                    "1-3 prvo & 1-3 vtoro" when total is < 2 or > 6 => false,
                    FirstHalf1To3And3PlusPick when total < 3 => false,
                    _ => null
                };
            ValidateHalfTime(result, htHome, htAway);
            int first = htHome + htAway;
            int second = total - first;
            return candidate.Pick switch
            {
                "1-2 prvo & 1-2 vtoro" => first is >= 1 and <= 2 && second is >= 1 and <= 2,
                "1-3 prvo & 1-3 vtoro" => first is >= 1 and <= 3 && second is >= 1 and <= 3,
                "1-3 prvo & 3+" => first is >= 1 and <= 3 && total >= 3,
                _ => throw new ArgumentException($"Unsupported ticket pick: {candidate.Pick}")
            };
        }
        if (candidate.Market != "Combo")
            throw new ArgumentException($"Unsupported ticket market: {candidate.Market}");
        if (candidate.Pick == "BTTS & 3+")
            return result.HomeGoals > 0 && result.AwayGoals > 0 && total >= 3;

        string side = candidate.Pick[..1];
        if (side is not ("1" or "2"))
            throw new ArgumentException($"Unsupported ticket pick: {candidate.Pick}");
        string condition = candidate.Pick[1..];
        if (condition == $" & T{side} 2+")
            return winner == side && (side == "1" ? result.HomeGoals : result.AwayGoals) >= 2;
        if (condition == " & 2-5 golovi")
            return winner == side && total is >= 2 and <= 5;
        if (condition == " & 2-6 golovi")
            return winner == side && total is >= 2 and <= 6;
        if (condition != " & 1+ prvo" && condition != " & 1+ prvo & 1+ vtoro" &&
            condition != $"-{side}" && condition != $"-{side} & 2+")
            throw new ArgumentException($"Unsupported ticket pick: {candidate.Pick}");
        if (result.HalfTimeHomeGoals is not int home || result.HalfTimeAwayGoals is not int away)
            return null;
        ValidateHalfTime(result, home, away);
        string htWinner = home > away ? "1" : home < away ? "2" : "X";
        return winner == side && condition switch
        {
            " & 1+ prvo" => home + away >= 1,
            " & 1+ prvo & 1+ vtoro" => home + away >= 1 && total - home - away >= 1,
            _ => htWinner == side && (!condition.EndsWith(" & 2+", StringComparison.Ordinal) || total >= 2)
        };
    }

    private static void ValidateHalfTime(MatchResult result, int home, int away)
    {
        if (home < 0 || away < 0 || home > result.HomeGoals || away > result.AwayGoals)
            throw new InvalidOperationException("Invalid stored half-time score.");
    }
}
