using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;
using Prediction.Services;

public partial class Program
{
    private const int MinReliableSample = 30;

    private static async Task RunCalibrationAsync()
    {
        string? connection = Environment.GetEnvironmentVariable("ConnectionStrings__FootballDb");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                "ConnectionStrings__FootballDb environment variable is not configured.");

        var options = new DbContextOptionsBuilder<FootballDbContext>()
            .UseNpgsql(connection)
            .Options;

        await using var db = new FootballDbContext(options);

        var matches = await db.Matches
            .AsNoTracking()
            .Where(x => x.Result != null)
            .Include(x => x.Result)
            .Include(x => x.PredictionSnapshots)
            .OrderBy(x => x.MatchDate)
            .ToListAsync();

        Console.WriteLine();
        Console.WriteLine("[CALIBRATION] Measures how often picks were really correct, grouped by the");
        Console.WriteLine("[CALIBRATION] signal shown in the report. Uses the latest snapshot per source.");

        if (matches.Count == 0)
        {
            Console.WriteLine("[CALIBRATION] No matches with results in the database. Run collect-results first.");
            return;
        }

        int days = matches.Select(x => x.MatchDate).Distinct().Count();
        Console.WriteLine(
            $"[CALIBRATION] Matches with results: {matches.Count} | Days: {days} | " +
            $"{matches[0].MatchDate:yyyy-MM-dd} .. {matches[^1].MatchDate:yyyy-MM-dd}");

        var results = matches.Select(x => x.Result!).ToList();
        Console.WriteLine(
            $"[CALIBRATION] Real outcomes: 1={Share(results, "1")} X={Share(results, "X")} 2={Share(results, "2")} | " +
            $"BTTS yes={results.Count(x => x.Btts) * 100.0 / results.Count:F1}% | " +
            $"Over 2.5={results.Count(x => x.TotalGoals > 2) * 100.0 / results.Count:F1}%");

        var byLevel = new Tally();
        var byProb = new Tally();
        var bySourceProb = new Dictionary<string, Tally>(StringComparer.OrdinalIgnoreCase);
        var bttsByAgreement = new Tally();
        var goalsByAgreement = new Tally();
        int conflicts = 0;
        int withSnapshots = 0;

        foreach (var match in matches)
        {
            var snapshots = CalibrationAnalyzer.LatestPerSource(match);
            if (snapshots.Count == 0)
                continue;

            withSnapshots++;
            var result = match.Result!;

            foreach (var snapshot in snapshots.Where(x => x.PredictedResult is "1" or "X" or "2"))
            {
                if (!bySourceProb.TryGetValue(snapshot.Source, out var tally))
                    bySourceProb[snapshot.Source] = tally = new Tally();

                tally.Add(
                    CalibrationAnalyzer.ProbBucket(CalibrationAnalyzer.ToPercent(snapshot.Confidence)),
                    snapshot.PredictedResult == result.Result);
            }

            var consensus = CalibrationAnalyzer.BuildResultConsensus(snapshots);
            if (consensus == null)
            {
                if (snapshots.Any(x => x.PredictedResult is "1" or "X" or "2"))
                    conflicts++;
            }
            else
            {
                bool correct = consensus.Pick == result.Result;
                byLevel.Add(consensus.Level, correct);
                byProb.Add(CalibrationAnalyzer.ProbBucket(consensus.AverageProbability), correct);
            }

            var btts = CalibrationAnalyzer.BuildBinaryConsensus(
                snapshots.Select(x => x.Btts is "YES" or "NO" ? x.Btts : null));
            if (btts != null && btts.Value.Votes >= 3)
                bttsByAgreement.Add(
                    CalibrationAnalyzer.AgreementBucket(btts.Value.Share),
                    btts.Value.Pick == (result.Btts ? "YES" : "NO"));

            var goals = CalibrationAnalyzer.BuildBinaryConsensus(
                snapshots.Select(x => ProductionConsensusAggregator.NormalizeGoalsTo25(x.Goals)));
            if (goals != null && goals.Value.Votes >= 3)
                goalsByAgreement.Add(
                    CalibrationAnalyzer.AgreementBucket(goals.Value.Share),
                    goals.Value.Pick == (result.TotalGoals > 2 ? "O2.5" : "U2.5"));
        }

        Console.WriteLine($"[CALIBRATION] Matches with snapshots: {withSnapshots} | 1X2 conflicts skipped: {conflicts}");

        if (withSnapshots == 0)
        {
            Console.WriteLine("[CALIBRATION] No predictions stored for matches with results.");
            return;
        }

        PrintTally("1X2 by CONFIDENCE LABEL (what the report shows)", byLevel, CalibrationAnalyzer.Levels);
        PrintTally("1X2 by average Prob. of the picked outcome", byProb, CalibrationAnalyzer.ProbBuckets);

        Console.WriteLine();
        Console.WriteLine("[CALIBRATION] 1X2 per source, by that source's own Prob.");
        foreach (var source in bySourceProb.OrderByDescending(x => x.Value.Total))
        {
            double accuracy = source.Value.Correct * 100.0 / source.Value.Total;
            Console.WriteLine(
                $"  {source.Key,-16} total {source.Value.Correct}/{source.Value.Total} ({accuracy:F1}%)");
            foreach (var row in source.Value.Rows(CalibrationAnalyzer.ProbBuckets))
                Console.WriteLine($"      Prob {row.Key,-6} {Format(row.Total, row.Correct)}");
        }

        PrintTally("BTTS by source agreement (3+ sources)", bttsByAgreement, CalibrationAnalyzer.AgreementBuckets);
        PrintTally("Goals O/U 2.5 by source agreement (3+ sources)", goalsByAgreement, CalibrationAnalyzer.AgreementBuckets);

        Console.WriteLine();
        Console.WriteLine(
            $"[CALIBRATION] Groups under {MinReliableSample} matches are marked (small sample) and must not be trusted.");
        Console.WriteLine(
            "[CALIBRATION] 'Safe' is the 95% lower bound of the accuracy: the realistic worst case.");
        Console.WriteLine(
            "[CALIBRATION] Snapshots taken after kickoff are not filtered out, so results may be slightly optimistic.");

        static string Share(IReadOnlyList<MatchResult> list, string outcome) =>
            $"{list.Count(x => x.Result == outcome) * 100.0 / list.Count:F1}%";

        static void PrintTally(string title, Tally tally, IEnumerable<string> order)
        {
            Console.WriteLine();
            Console.WriteLine($"[CALIBRATION] {title}");

            foreach (var row in tally.Rows(order))
                Console.WriteLine($"  {row.Key,-14} {Format(row.Total, row.Correct)}");

            if (tally.Total > 0)
                Console.WriteLine($"  {"ALL",-14} {Format(tally.Total, tally.Correct)}");
        }

        static string Format(int total, int correct)
        {
            double accuracy = correct * 100.0 / total;
            double safe = CalibrationAnalyzer.WilsonLower(correct, total) * 100.0;
            string note = total < MinReliableSample ? " (small sample)" : "";
            return $"{correct,4}/{total,-4} Accuracy={accuracy,5:F1}%  Safe={safe,5:F1}%{note}";
        }
    }
}
