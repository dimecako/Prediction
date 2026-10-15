using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Services;

public partial class Program
{
    private static async Task RunWeightedConsensusAsync(string[] args)
    {
        if (args.Length != 2 ||
            !DateOnly.TryParseExact(
                args[1],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            Console.Error.WriteLine(
                "[WEIGHTED] Invalid arguments. Use: weighted-consensus yyyy-MM-dd");
            Environment.ExitCode = 1;
            return;
        }

        string? connection = Environment.GetEnvironmentVariable("ConnectionStrings__FootballDb");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                "ConnectionStrings__FootballDb environment variable is not configured.");

        var options = new DbContextOptionsBuilder<FootballDbContext>()
            .UseNpgsql(connection)
            .Options;

        await using var db = new FootballDbContext(options);
        await PrintWeightedConsensusAsync(db, date, showFixtures: true);
    }

    private static async Task PrintWeightedConsensusAsync(
        FootballDbContext db,
        DateOnly date,
        bool showFixtures)
    {
        DateTime cutoff = WeightedConsensusCalculator.GetCutoffUtc(date);

        var history = await db.Matches
            .AsNoTracking()
            .Where(x =>
                x.MatchDate < date &&
                x.Result != null &&
                x.Result.CollectedAtUtc < cutoff)
            .Include(x => x.Result)
            .Include(x => x.PredictionSnapshots.Where(p => p.CapturedAtUtc < cutoff))
            .ToListAsync();

        var weights = WeightedConsensusCalculator.BuildWeights(history, date);
        var matches = await db.Matches
            .AsNoTracking()
            .Where(x => x.MatchDate == date)
            .Include(x => x.Result)
            .Include(x => x.PredictionSnapshots.Where(p => p.CapturedAtUtc < cutoff))
            .OrderBy(x => x.HomeTeam)
            .ThenBy(x => x.AwayTeam)
            .ToListAsync();

        Console.WriteLine();
        Console.WriteLine($"[WEIGHTED] Date: {date:yyyy-MM-dd} | Cutoff: {cutoff:O}");
        Console.WriteLine(
            "[WEIGHTED] Strict mode: snapshots before match-day UTC midnight; " +
            "history and collected results before target-day UTC midnight.");
        Console.WriteLine(
            $"[WEIGHTED] Historical graded source/market samples: {weights.Sum(x => x.Total)}");

        if (weights.Count == 0)
            Console.WriteLine(
                "[WEIGHTED] No eligible history. All source/market weights use the neutral prior 0.500.");

        foreach (var weight in weights)
        {
            Console.WriteLine(
                $"[WEIGHT] {weight.Source} | {weight.Market} | " +
                $"{weight.Correct}/{weight.Total} | Weight={weight.Weight:F3}");
        }

        var equalStats = new ConsensusBacktestStats();
        var weightedStats = new ConsensusBacktestStats();
        int eligibleFixtures = 0;
        int fixturesWithResults = 0;

        foreach (var match in matches)
        {
            var equal = WeightedConsensusCalculator.Calculate(match, weights, weighted: false);
            var weighted = WeightedConsensusCalculator.Calculate(match, weights);

            if (weighted.Result.TotalSources > 0 ||
                weighted.Btts.TotalSources > 0 ||
                weighted.Goals.TotalSources > 0)
                eligibleFixtures++;

            if (showFixtures)
            {
                Console.WriteLine($"[CONSENSUS] {match.HomeTeam} vs {match.AwayTeam}");
                PrintVote("Result", equal.Result, weighted.Result);
                PrintVote("BTTS", equal.Btts, weighted.Btts);
                PrintVote("Goals", equal.Goals, weighted.Goals);
            }

            if (match.Result == null)
                continue;

            fixturesWithResults++;
            equalStats.Add(equal, match.Result);
            weightedStats.Add(weighted, match.Result);
        }

        Console.WriteLine(
            $"[WEIGHTED] Fixtures: {matches.Count} | With eligible votes: {eligibleFixtures} | " +
            $"With results: {fixturesWithResults}");

        if (eligibleFixtures == 0)
            Console.WriteLine(
                "[WEIGHTED] No eligible predictions. Same-day/late snapshots and unsupported values are excluded.");

        Console.WriteLine(
            "[CONSENSUS BACKTEST] Equal and weighted use the SAME strict snapshots. " +
            "Ties/no votes abstain; accuracy excludes abstentions. Coverage = graded/fixtures with results.");
        equalStats.Print("Equal", fixturesWithResults);
        weightedStats.Print("Weighted", fixturesWithResults);
        Console.WriteLine(
            "[WEIGHTED] Support share is vote agreement, NOT a calibrated probability or betting edge.");

        static void PrintVote(string market, ConsensusVote equal, ConsensusVote weighted)
        {
            string share = weighted.SupportShare.HasValue
                ? $"{weighted.SupportShare.Value:P1}"
                : "-";
            Console.WriteLine(
                $"  {market}: Equal={Label(equal)} | Weighted={Label(weighted)} | " +
                $"Support={share} | Sources={weighted.TotalSources}");
        }

        static string Label(ConsensusVote vote) =>
            vote.Pick ?? (vote.TotalSources == 0 ? "NO VOTES" : "CONFLICT");
    }

    private sealed class ConsensusBacktestStats
    {
        private readonly Dictionary<ConsensusMarket, (int Correct, int Total)> stats = new();

        public void Add(ConsensusPrediction prediction, MatchResult result)
        {
            var grade = PredictionGrader.Grade(prediction.ToSnapshot(), result);
            AddGrade(ConsensusMarket.Result, grade.CorrectResult);
            AddGrade(ConsensusMarket.Btts, grade.CorrectBtts);
            AddGrade(ConsensusMarket.Goals, grade.CorrectGoals);
        }

        private void AddGrade(ConsensusMarket market, bool? correct)
        {
            if (!correct.HasValue)
                return;

            var stat = stats.GetValueOrDefault(market);
            stats[market] = (stat.Correct + (correct.Value ? 1 : 0), stat.Total + 1);
        }

        public void Print(string label, int fixtures)
        {
            foreach (var market in Enum.GetValues<ConsensusMarket>())
            {
                var stat = stats.GetValueOrDefault(market);
                string accuracy = stat.Total > 0
                    ? $"{stat.Correct * 100.0 / stat.Total:F1}%"
                    : "-";
                string coverage = fixtures > 0
                    ? $"{stat.Total * 100.0 / fixtures:F1}%"
                    : "-";

                Console.WriteLine(
                    $"[CONSENSUS BACKTEST] {label} {market}: " +
                    $"{stat.Correct}/{stat.Total} | Accuracy={accuracy} | " +
                    $"Coverage={stat.Total}/{fixtures} ({coverage})");
            }
        }
    }
}
