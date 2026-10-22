using Microsoft.ML;
using Microsoft.ML.Data;
using Prediction.Entities;
using Prediction.Services;

namespace Prediction.ML;

public sealed record MlMatchProbabilities(
    IReadOnlyDictionary<string, double> ResultProbabilities,
    IReadOnlyDictionary<string, double> BttsProbabilities,
    IReadOnlyDictionary<string, double> GoalsProbabilities)
{
    public double? GetCandidateProbability(TicketCandidate candidate) => (candidate.Market, candidate.Pick) switch
    {
        ("1X2", var side) when ResultProbabilities.TryGetValue(side, out double p) => p,
        ("BTTS", var pick) when BttsProbabilities.TryGetValue(pick, out double p) => p,
        ("Goals", var pick) when GoalsProbabilities.TryGetValue(pick, out double p) => p,
        ("Combo", "BTTS & 3+") when BttsProbabilities.TryGetValue("YES", out double btts) &&
                                    GoalsProbabilities.TryGetValue("O2.5", out double over) => (btts + over) / 2.0,
        ("Combo", var pick) when pick.Length > 0 && ResultProbabilities.TryGetValue(pick[..1], out double win) =>
            GoalsProbabilities.TryGetValue("O2.5", out double over) ? (win + over) / 2.0 : win,
        ("HalfGoals", _) when GoalsProbabilities.TryGetValue("O2.5", out double over) => over,
        _ => null
    };
}

public sealed class MlMatchRanker
{
    private readonly MLContext _mlContext = new(seed: 42);
    private readonly PredictionEngine<FootballMatchTrainingRow, FootballPrediction> _resultEngine;
    private readonly PredictionEngine<FootballMatchTrainingRow, FootballPrediction> _bttsEngine;
    private readonly PredictionEngine<FootballMatchTrainingRow, FootballPrediction> _goalsEngine;
    private readonly string[] _resultLabels;
    private readonly string[] _bttsLabels;
    private readonly string[] _goalsLabels;

    private MlMatchRanker(
        ITransformer resultModel,
        DataViewSchema resultSchema,
        ITransformer bttsModel,
        DataViewSchema bttsSchema,
        ITransformer goalsModel,
        DataViewSchema goalsSchema)
    {
        _resultEngine = _mlContext.Model.CreatePredictionEngine<FootballMatchTrainingRow, FootballPrediction>(resultModel, resultSchema);
        _bttsEngine = _mlContext.Model.CreatePredictionEngine<FootballMatchTrainingRow, FootballPrediction>(bttsModel, bttsSchema);
        _goalsEngine = _mlContext.Model.CreatePredictionEngine<FootballMatchTrainingRow, FootballPrediction>(goalsModel, goalsSchema);
        _resultLabels = GetSlotLabels(_resultEngine.OutputSchema);
        _bttsLabels = GetSlotLabels(_bttsEngine.OutputSchema);
        _goalsLabels = GetSlotLabels(_goalsEngine.OutputSchema);
    }

    public static MlMatchRanker? TrainFromHistory(IEnumerable<Match> history)
    {
        var rows = FootballModelTrainer.BuildMatchTrainingRows(history);
        if (rows.Count < 10)
            return null;

        var ml = new MLContext(seed: 42);
        var data = ml.Data.LoadFromEnumerable(rows);
        var resultModel = ResultModelTrainer.BuildPipeline(ml).Fit(data);
        var bttsModel = BttsModelTrainer.BuildPipeline(ml).Fit(data);
        var goalsModel = GoalsModelTrainer.BuildPipeline(ml).Fit(data);

        return new MlMatchRanker(
            resultModel, data.Schema,
            bttsModel, data.Schema,
            goalsModel, data.Schema);
    }

    public MlMatchProbabilities? Predict(Match match)
    {
        var row = FootballModelTrainer.BuildMatchFeatureRow(match);
        if (row == null)
            return null;

        var resPred = _resultEngine.Predict(row);
        var bttsPred = _bttsEngine.Predict(row);
        var goalsPred = _goalsEngine.Predict(row);

        return new MlMatchProbabilities(
            MapScores(_resultLabels, resPred.Score),
            MapScores(_bttsLabels, bttsPred.Score),
            MapScores(_goalsLabels, goalsPred.Score));
    }

    public static (List<FootballMatchTrainingRow> Train, List<FootballMatchTrainingRow> Test) SplitChronologically(
        IReadOnlyList<FootballMatchTrainingRow> rows,
        double testFraction = 0.20)
    {
        var ordered = rows
            .OrderBy(x => x.MatchDate)
            .ThenBy(x => x.HomeTeam, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.AwayTeam, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int testCount = Math.Clamp((int)Math.Round(ordered.Count * testFraction), 1, ordered.Count - 1);
        int trainCount = ordered.Count - testCount;

        return (
            ordered.Take(trainCount).ToList(),
            ordered.Skip(trainCount).ToList());
    }

    internal static void PrintProbabilityCalibration(
        MLContext mlContext,
        ITransformer model,
        IDataView testData,
        IReadOnlyList<FootballMatchTrainingRow> testRows,
        Func<FootballMatchTrainingRow, string> actualSelector,
        string label)
    {
        var predictions = mlContext.Data.CreateEnumerable<FootballPrediction>(
            model.Transform(testData), reuseRowObject: false).ToList();

        var tally = new Tally();
        for (int i = 0; i < Math.Min(predictions.Count, testRows.Count); i++)
        {
            var pred = predictions[i];
            if (pred.Score == null || pred.Score.Length == 0 || string.IsNullOrWhiteSpace(pred.PredictedLabel))
                continue;

            var probs = NormalizeProbabilities(pred.Score);
            double topProb = probs.Max() * 100.0;
            bool correct = pred.PredictedLabel == actualSelector(testRows[i]);
            tally.Add(CalibrationAnalyzer.ProbBucket(topProb), correct);
        }

        Console.WriteLine($"[{label}] Out-of-time calibration by ML probability:");
        foreach (var row in tally.Rows(CalibrationAnalyzer.ProbBuckets))
        {
            double acc = row.Correct * 100.0 / row.Total;
            double safe = CalibrationAnalyzer.WilsonLower(row.Correct, row.Total) * 100.0;
            Console.WriteLine($"  Prob {row.Key,-6} {row.Correct,4}/{row.Total,-4} Accuracy={acc,5:F1}%  Safe={safe,5:F1}%");
        }
    }

    private static string[] GetSlotLabels(DataViewSchema schema)
    {
        var scoreColumn = schema.GetColumnOrNull("Score");
        if (scoreColumn.HasValue &&
            scoreColumn.Value.Annotations.Schema.GetColumnOrNull("SlotNames") is not null)
        {
            VBuffer<ReadOnlyMemory<char>> slotNames = default;
            scoreColumn.Value.GetSlotNames(ref slotNames);
            return slotNames.DenseValues().Select(x => x.ToString()).ToArray();
        }
        return [];
    }

    private static Dictionary<string, double> MapScores(string[] labels, float[] scores)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (scores == null || scores.Length == 0)
            return result;

        double[] normalized = NormalizeProbabilities(scores);
        int count = Math.Min(labels.Length, normalized.Length);
        for (int i = 0; i < count; i++)
        {
            if (!string.IsNullOrWhiteSpace(labels[i]))
                result[labels[i]] = normalized[i];
        }
        return result;
    }

    private static double[] NormalizeProbabilities(float[] scores)
    {
        double sum = scores.Sum(x => (double)x);
        if (scores.All(x => x >= 0f && x <= 1f) && Math.Abs(sum - 1.0) < 0.05 && sum > 0)
            return scores.Select(x => x / sum).ToArray();

        double max = scores.Max();
        double[] exp = scores.Select(x => Math.Exp(x - max)).ToArray();
        double expSum = exp.Sum();
        return exp.Select(x => x / expSum).ToArray();
    }
}
