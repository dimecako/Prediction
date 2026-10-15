using Prediction.Entities;

namespace Prediction.Services;

public enum ConsensusMarket
{
    Result,
    Btts,
    Goals
}

public sealed record SourceMarketWeight(
    string Source,
    ConsensusMarket Market,
    int Correct,
    int Total)
{
    public double Weight => (Correct + 5.0) / (Total + 10.0);
}

public sealed record ConsensusVote(
    string? Pick,
    int SupportingSources,
    int TotalSources,
    double SupportWeight,
    double TotalWeight)
{
    public double? SupportShare =>
        Pick != null && TotalWeight > 0
            ? SupportWeight / TotalWeight
            : null;
}

public sealed record ConsensusPrediction(
    ConsensusVote Result,
    ConsensusVote Btts,
    ConsensusVote Goals)
{
    public PredictionSnapshot ToSnapshot() => new()
    {
        PredictedResult = Result.Pick,
        Btts = Btts.Pick,
        Goals = Goals.Pick
    };
}

public static class WeightedConsensusCalculator
{
    public static DateTime GetCutoffUtc(DateOnly date) =>
        DateTime.SpecifyKind(
            date.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Utc);

    public static IReadOnlyList<PredictionSnapshot> GetEligiblePredictions(Match match)
    {
        DateTime cutoff = GetCutoffUtc(match.MatchDate);

        return match.PredictionSnapshots
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Source) &&
                x.CapturedAtUtc < cutoff)
            .GroupBy(x => x.Source.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(x => x.CapturedAtUtc)
                .ThenByDescending(x => x.Id)
                .First())
            .Select(x => new PredictionSnapshot
            {
                Id = x.Id,
                MatchId = x.MatchId,
                Source = x.Source.Trim(),
                CapturedAtUtc = x.CapturedAtUtc,
                PredictedResult = Normalize(x.PredictedResult, ConsensusMarket.Result),
                Btts = Normalize(x.Btts, ConsensusMarket.Btts),
                Goals = Normalize(x.Goals, ConsensusMarket.Goals)
            })
            .ToList();
    }

    public static IReadOnlyList<SourceMarketWeight> BuildWeights(
        IEnumerable<Match> history,
        DateOnly predictionDate)
    {
        DateTime cutoff = GetCutoffUtc(predictionDate);
        var grades = new List<(string Source, ConsensusMarket Market, bool Correct)>();

        foreach (var match in history)
        {
            if (match.MatchDate >= predictionDate ||
                match.Result == null ||
                match.Result.CollectedAtUtc >= cutoff)
                continue;

            foreach (var prediction in GetEligiblePredictions(match))
            {
                var grade = PredictionGrader.Grade(prediction, match.Result);
                AddGrade(prediction.Source, ConsensusMarket.Result, grade.CorrectResult);
                AddGrade(prediction.Source, ConsensusMarket.Btts, grade.CorrectBtts);
                AddGrade(prediction.Source, ConsensusMarket.Goals, grade.CorrectGoals);
            }
        }

        return grades
            .GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .SelectMany(source => source.GroupBy(x => x.Market)
                .Select(market => new SourceMarketWeight(
                    source.Key,
                    market.Key,
                    market.Count(x => x.Correct),
                    market.Count())))
            .OrderBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Market)
            .ToList();

        void AddGrade(string source, ConsensusMarket market, bool? correct)
        {
            if (correct.HasValue)
                grades.Add((source, market, correct.Value));
        }
    }

    public static ConsensusPrediction Calculate(
        Match match,
        IReadOnlyList<SourceMarketWeight> weights,
        bool weighted = true)
    {
        var predictions = GetEligiblePredictions(match);

        return new ConsensusPrediction(
            Vote(ConsensusMarket.Result, x => x.PredictedResult),
            Vote(ConsensusMarket.Btts, x => x.Btts),
            Vote(ConsensusMarket.Goals, x => x.Goals));

        ConsensusVote Vote(
            ConsensusMarket market,
            Func<PredictionSnapshot, string?> select)
        {
            var sourceWeights = weights
                .Where(x => x.Market == market)
                .ToDictionary(x => x.Source, x => x.Weight, StringComparer.OrdinalIgnoreCase);

            var votes = predictions
                .Where(x => select(x) != null)
                .Select(x => new
                {
                    Pick = select(x)!,
                    Weight = weighted
                        ? sourceWeights.GetValueOrDefault(x.Source, 0.5)
                        : 1.0
                })
                .ToList();

            var groups = votes
                .GroupBy(x => x.Pick)
                .Select(g => new
                {
                    Pick = g.Key,
                    Count = g.Count(),
                    Weight = g.Sum(x => x.Weight)
                })
                .OrderByDescending(x => x.Weight)
                .ThenBy(x => x.Pick, StringComparer.Ordinal)
                .ToList();

            if (groups.Count == 0)
                return new ConsensusVote(null, 0, 0, 0, 0);

            var winner = groups[0];
            bool tied = groups.Count > 1 &&
                Math.Abs(winner.Weight - groups[1].Weight) <= 1e-12;

            return new ConsensusVote(
                tied ? null : winner.Pick,
                winner.Count,
                votes.Count,
                winner.Weight,
                votes.Sum(x => x.Weight));
        }
    }

    private static string? Normalize(string? value, ConsensusMarket market)
    {
        string? normalized = value?.Trim().ToUpperInvariant();

        return market switch
        {
            ConsensusMarket.Result when normalized is "1" or "X" or "2" => normalized,
            ConsensusMarket.Btts when normalized is "YES" or "NO" => normalized,
            ConsensusMarket.Goals when normalized is
                "O0.5" or "U0.5" or "O1.5" or "U1.5" or
                "O2.5" or "U2.5" or "O3.5" or "U3.5" => normalized,
            _ => null
        };
    }
}
