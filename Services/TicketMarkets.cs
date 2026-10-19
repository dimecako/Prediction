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

public static class TicketMarkets
{
    public static IEnumerable<TicketCandidate> BuildCalibrationCandidates(Match match)
    {
        foreach (var candidate in BuildCandidates(match))
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

    public static IEnumerable<TicketCandidate> BuildCandidates(Match match)
    {
        var snapshots = CalibrationAnalyzer.LatestPerSource(match);
        var consensus = CalibrationAnalyzer.BuildResultConsensus(snapshots);
        if (consensus is { Pick: "1" or "2", AverageProbability: double probability })
        {
            string side = consensus.Pick;
            string bucket = CalibrationAnalyzer.ProbBucket(probability);
            string detail = $"Prob={probability:F1}% {consensus.MatchingVotes}/{consensus.TotalVotes}";
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
    }

    // A missing half-time score is ungraded, not a lost bet or an inferred 0-0.
    public static bool? IsCorrect(TicketCandidate candidate, MatchResult result)
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
        if (candidate.Market != "Combo")
            throw new ArgumentException($"Unsupported ticket market: {candidate.Market}");
        if (candidate.Pick == "BTTS & 3+")
            return result.HomeGoals > 0 && result.AwayGoals > 0 && total >= 3;

        string side = candidate.Pick[..1];
        if (side is not ("1" or "2"))
            throw new ArgumentException($"Unsupported ticket pick: {candidate.Pick}");
        string condition = candidate.Pick[1..];
        if (condition == " & 2-5 golovi")
            return winner == side && total is >= 2 and <= 5;
        if (condition == " & 2-6 golovi")
            return winner == side && total is >= 2 and <= 6;
        if (condition != " & 1+ prvo" && condition != " & 1+ prvo & 1+ vtoro" &&
            condition != $"-{side}" && condition != $"-{side} & 2+")
            throw new ArgumentException($"Unsupported ticket pick: {candidate.Pick}");
        if (result.HalfTimeHomeGoals is not int home || result.HalfTimeAwayGoals is not int away)
            return null;
        if (home < 0 || away < 0 || home > result.HomeGoals || away > result.AwayGoals)
            throw new InvalidOperationException("Invalid stored half-time score.");
        string htWinner = home > away ? "1" : home < away ? "2" : "X";
        return winner == side && condition switch
        {
            " & 1+ prvo" => home + away >= 1,
            " & 1+ prvo & 1+ vtoro" => home + away >= 1 && total - home - away >= 1,
            _ => htWinner == side && (!condition.EndsWith(" & 2+", StringComparison.Ordinal) || total >= 2)
        };
    }
}
