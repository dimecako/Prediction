using Prediction.Entities;

namespace Prediction.Services;

public sealed record LeaguePerformance(string League, string Market, int Total, int Correct)
{
    public double Accuracy => (double)Correct / Total;
    public double Safe => CalibrationAnalyzer.WilsonLower(Correct, Total);
}

public static class LeaguePerformanceAnalyzer
{
    public static IReadOnlyList<LeaguePerformance> Analyze(IEnumerable<Match> matches)
    {
        var grades = new List<(string League, string Market, bool Correct)>();
        foreach (var match in matches.Where(x => x.Result != null))
        {
            var snapshots = WeightedConsensusCalculator.GetEligiblePredictions(match);
            string league = TicketPerformanceAnalyzer.LeagueName(match.League);
            var consensus = CalibrationAnalyzer.BuildResultConsensus(snapshots);
            if (consensus != null)
                grades.Add((league, "1X2", consensus.Pick == match.Result!.Result));
            var btts = CalibrationAnalyzer.BuildBinaryConsensus(snapshots.Select(x => x.Btts));
            if (btts != null)
                grades.Add((league, "BTTS", (btts.Value.Pick == "YES") == match.Result!.Btts));
            var goals = CalibrationAnalyzer.BuildBinaryConsensus(snapshots.Select(x =>
                ProductionConsensusAggregator.NormalizeGoalsTo25(x.Goals)));
            if (goals != null)
                grades.Add((league, "Goals O/U 2.5", (goals.Value.Pick == "O2.5") == (match.Result!.TotalGoals > 2)));
        }
        return grades.GroupBy(x => (x.League, x.Market))
            .Select(g => new LeaguePerformance(g.Key.League, g.Key.Market, g.Count(), g.Count(x => x.Correct)))
            .OrderBy(x => x.League, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Market).ToList();
    }
}
