using Prediction.Entities;

namespace Prediction.Services;

public sealed record MarketConsensus(string? Pick, int Matching, int Total, double? AverageProbability, int Priced)
{
    public double? Agreement => Pick == null ? null : (double)Matching / Total;
}

public sealed record PredictionConsensus(Match Match, MarketConsensus Result, MarketConsensus Btts, MarketConsensus Goals)
{
    public static readonly string[] SortKeys =
        ["result-agreement", "result-probability", "btts-agreement", "btts-probability", "goals-agreement", "goals-probability"];

    public static PredictionConsensus Build(Match match)
    {
        var snapshots = CalibrationAnalyzer.LatestPerSource(match);
        return new(match,
            Market(snapshots, x => x.PredictedResult, ["1", "X", "2"], x => x.Confidence),
            Market(snapshots, x => x.Btts, ["YES", "NO"], _ => null),
            Market(snapshots, x => x.Goals, ["O0.5", "U0.5", "O1.5", "U1.5", "O2.5", "U2.5", "O3.5", "U3.5"], _ => null));
    }

    public static IReadOnlyList<PredictionConsensus> Order(IEnumerable<Match> matches,
        string sort = "result-agreement", bool ascending = false)
    {
        if (!SortKeys.Contains(sort))
            throw new ArgumentException("Unsupported prediction sort.", nameof(sort));
        var rows = matches.Select(Build);
        MarketConsensus MarketFor(PredictionConsensus row) => sort.StartsWith("btts-", StringComparison.Ordinal)
            ? row.Btts : sort.StartsWith("goals-", StringComparison.Ordinal) ? row.Goals : row.Result;
        double? Value(PredictionConsensus row) => sort.EndsWith("-probability", StringComparison.Ordinal)
            ? MarketFor(row).AverageProbability : MarketFor(row).Agreement;
        var knownFirst = rows.OrderBy(x => !Value(x).HasValue);
        var ordered = ascending ? knownFirst.ThenBy(Value) : knownFirst.ThenByDescending(Value);
        return ordered.ThenByDescending(x => MarketFor(x).Matching)
            .ThenByDescending(x => MarketFor(x).AverageProbability)
            .ThenBy(x => x.Match.HomeTeam, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Match.AwayTeam, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Match.Id).ToList();
    }

    private static MarketConsensus Market(IReadOnlyList<PredictionSnapshot> snapshots,
        Func<PredictionSnapshot, string?> pick, string[] allowed, Func<PredictionSnapshot, double?> probability)
    {
        var votes = snapshots.Select(x => (Snapshot: x, Pick: pick(x)?.Trim().ToUpperInvariant()))
            .Where(x => x.Pick != null && allowed.Contains(x.Pick)).ToList();
        var groups = votes.GroupBy(x => x.Pick).OrderByDescending(x => x.Count()).ToList();
        if (groups.Count == 0 || groups.Count > 1 && groups[0].Count() == groups[1].Count())
            return new(null, 0, votes.Count, null, 0);
        var winner = groups[0];
        var probabilities = winner.Select(x => probability(x.Snapshot))
            .Where(x => x.HasValue && double.IsFinite(x.Value) && x.Value is >= 0 and <= 1)
            .Select(x => x!.Value).ToList();
        return new(winner.Key, winner.Count(), votes.Count,
            probabilities.Count == 0 ? null : probabilities.Average(), probabilities.Count);
    }
}
