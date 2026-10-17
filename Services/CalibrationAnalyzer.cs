using Prediction.Entities;

namespace Prediction.Services;

public sealed class Tally
{
    private readonly Dictionary<string, (int Total, int Correct)> items = new();

    public int Total => items.Values.Sum(x => x.Total);
    public int Correct => items.Values.Sum(x => x.Correct);

    public (int Total, int Correct) Get(string key) => items.GetValueOrDefault(key);

    public void Add(string key, bool correct)
    {
        var current = items.GetValueOrDefault(key);
        items[key] = (current.Total + 1, current.Correct + (correct ? 1 : 0));
    }

    public IEnumerable<(string Key, int Total, int Correct)> Rows(IEnumerable<string> order) =>
        order.Where(items.ContainsKey)
            .Select(k => (k, items[k].Total, items[k].Correct));
}

public sealed record ResultConsensus(
    string Pick,
    int MatchingVotes,
    int TotalVotes,
    double? AverageProbability,
    string Level);

public static class CalibrationAnalyzer
{
    public static readonly string[] ProbBuckets =
        { "<50", "50-55", "55-60", "60-65", "65-70", "70+", "n/a" };

    public static readonly string[] AgreementBuckets =
        { "<60", "60-70", "70-80", "80-90", "90-100", "100" };

    public static readonly string[] Levels =
        { "VERY STRONG", "STRONG", "MEDIUM", "WEAK", "SINGLE SOURCE" };

    public static string ProbBucket(double? percent) => percent switch
    {
        null => "n/a",
        < 50 => "<50",
        < 55 => "50-55",
        < 60 => "55-60",
        < 65 => "60-65",
        < 70 => "65-70",
        _ => "70+"
    };

    public static string AgreementBucket(double share) => share switch
    {
        >= 1.0 => "100",
        >= 0.9 => "90-100",
        >= 0.8 => "80-90",
        >= 0.7 => "70-80",
        >= 0.6 => "60-70",
        _ => "<60"
    };

    // Lower bound of the 95% Wilson interval: a conservative accuracy estimate for small samples.
    public static double WilsonLower(int correct, int total)
    {
        if (total == 0)
            return 0;

        const double z = 1.96;
        double p = (double)correct / total;
        double denominator = 1 + z * z / total;
        double centre = p + z * z / (2 * total);
        double margin = z * Math.Sqrt((p * (1 - p) + z * z / (4.0 * total)) / total);

        return (centre - margin) / denominator;
    }

    public static IReadOnlyList<PredictionSnapshot> LatestPerSource(Match match) =>
        match.PredictionSnapshots
            .Where(x => !string.IsNullOrWhiteSpace(x.Source))
            .GroupBy(x => x.Source.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(x => x.CapturedAtUtc)
                .ThenByDescending(x => x.Id)
                .First())
            .ToList();

    public static double? ToPercent(double? confidence) =>
        confidence.HasValue ? confidence.Value * 100.0 : null;

    // Mirrors the report: majority 1/X/2 tip, average source probability for that tip,
    // confidence level capped by that probability. Ties are conflicts and return null.
    public static ResultConsensus? BuildResultConsensus(
        IReadOnlyList<PredictionSnapshot> snapshots)
    {
        var votes = snapshots
            .Where(x => x.PredictedResult is "1" or "X" or "2")
            .ToList();

        if (votes.Count == 0)
            return null;

        var groups = votes
            .GroupBy(x => x.PredictedResult!)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (groups.Count > 1 && groups[0].Count() == groups[1].Count())
            return null;

        string pick = groups[0].Key;
        int matching = groups[0].Count();

        var probabilities = votes
            .Where(x => x.PredictedResult == pick && x.Confidence.HasValue)
            .Select(x => x.Confidence!.Value * 100.0)
            .ToList();

        double? average = probabilities.Count > 0 ? probabilities.Average() : null;

        string level = ProductionConsensusAggregator.CapConfidenceByProbability(
            ProductionConsensusAggregator.GetConfidenceLevel(matching, votes.Count, false),
            average);

        return new ResultConsensus(pick, matching, votes.Count, average, level);
    }

    // Share of sources that back the majority side; Goals are compared on the 2.5 line only.
    public static (string Pick, double Share, int Votes)? BuildBinaryConsensus(
        IEnumerable<string?> votes)
    {
        var list = votes.Where(x => x != null).Select(x => x!).ToList();

        if (list.Count == 0)
            return null;

        var groups = list
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (groups.Count > 1 && groups[0].Count() == groups[1].Count())
            return null;

        return (groups[0].Key, (double)groups[0].Count() / list.Count, list.Count);
    }
}
