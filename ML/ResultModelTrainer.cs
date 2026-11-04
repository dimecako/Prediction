using Microsoft.ML;

namespace Prediction.ML;

public sealed class ResultModelTrainer
{
    private readonly MLContext _mlContext;

    public ResultModelTrainer()
    {
        _mlContext = new MLContext(seed: 42);
    }

    public ITransformer Train(List<FootballMatchTrainingRow> rows, bool saveToDisk = true)
    {
        if (rows.Count < 10)
        {
            throw new InvalidOperationException(
                "Not enough match-level training data.");
        }

        Console.WriteLine(
            $"[ML-RESULT] Training matches (all 11 sources): {rows.Count}");

        var (trainRows, testRows) = MlMatchRanker.SplitChronologically(rows, 0.20);
        var trainData = _mlContext.Data.LoadFromEnumerable(trainRows);
        var testData = _mlContext.Data.LoadFromEnumerable(testRows);

        var pipeline = BuildPipeline(_mlContext);

        var evalModel = pipeline.Fit(trainData);
        var predictions = evalModel.Transform(testData);
        var metrics = _mlContext.MulticlassClassification.Evaluate(
            predictions,
            labelColumnName: "Label");

        Console.WriteLine($"[ML-RESULT] Out-of-time validation ({trainRows.Count} train / {testRows.Count} chronological test)");
        Console.WriteLine($"[ML-RESULT] MicroAccuracy: {metrics.MicroAccuracy:P2}");
        Console.WriteLine($"[ML-RESULT] MacroAccuracy: {metrics.MacroAccuracy:P2}");
        Console.WriteLine($"[ML-RESULT] LogLoss: {metrics.LogLoss:F4}");
        MlMatchRanker.PrintProbabilityCalibration(
            _mlContext, evalModel, testData, testRows, x => x.ActualResult, "ML-RESULT 1X2");

        var fullData = _mlContext.Data.LoadFromEnumerable(rows);
        var fullModel = pipeline.Fit(fullData);

        if (saveToDisk)
        {
            const string modelPath = "ResultModel.zip";
            _mlContext.Model.Save(fullModel, fullData.Schema, modelPath);
            Console.WriteLine($"[ML-RESULT] Model saved: {modelPath}");
        }

        return fullModel;
    }

    internal static IEstimator<ITransformer> BuildPipeline(MLContext mlContext) =>
        mlContext.Transforms.Conversion.MapValueToKey(
            outputColumnName: "Label",
            inputColumnName: nameof(FootballMatchTrainingRow.ActualResult))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("ForebetEncoded", nameof(FootballMatchTrainingRow.ForebetResult)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("StatareaEncoded", nameof(FootballMatchTrainingRow.StatareaResult)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("VitibetEncoded", nameof(FootballMatchTrainingRow.VitibetResult)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("PredictZEncoded", nameof(FootballMatchTrainingRow.PredictZResult)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("ZuluBetEncoded", nameof(FootballMatchTrainingRow.ZuluBetResult)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("WinDrawWinEncoded", nameof(FootballMatchTrainingRow.WinDrawWinResult)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("FeedincoEncoded", nameof(FootballMatchTrainingRow.FeedincoResult)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("SoccerVistaEncoded", nameof(FootballMatchTrainingRow.SoccerVistaResult)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("FootyStatsEncoded", nameof(FootballMatchTrainingRow.FootyStatsResult)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("OddslotEncoded", nameof(FootballMatchTrainingRow.OddslotResult)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("TheDatabeticsEncoded", nameof(FootballMatchTrainingRow.TheDatabeticsResult)))
            .Append(mlContext.Transforms.Concatenate(
                "Features",
                "ForebetEncoded",
                "StatareaEncoded",
                "VitibetEncoded",
                "PredictZEncoded",
                "ZuluBetEncoded",
                "WinDrawWinEncoded",
                "FeedincoEncoded",
                "SoccerVistaEncoded",
                "FootyStatsEncoded",
                "OddslotEncoded",
                "TheDatabeticsEncoded",
                nameof(FootballMatchTrainingRow.ForebetConfidence),
                nameof(FootballMatchTrainingRow.StatareaConfidence),
                nameof(FootballMatchTrainingRow.VitibetConfidence),
                nameof(FootballMatchTrainingRow.PredictZConfidence),
                nameof(FootballMatchTrainingRow.ZuluBetConfidence),
                nameof(FootballMatchTrainingRow.WinDrawWinConfidence),
                nameof(FootballMatchTrainingRow.FeedincoConfidence),
                nameof(FootballMatchTrainingRow.SoccerVistaConfidence),
                nameof(FootballMatchTrainingRow.FootyStatsConfidence),
                nameof(FootballMatchTrainingRow.OddslotConfidence),
                nameof(FootballMatchTrainingRow.TheDatabeticsConfidence),
                nameof(FootballMatchTrainingRow.SourceCount),
                nameof(FootballMatchTrainingRow.HomeVoteShare),
                nameof(FootballMatchTrainingRow.DrawVoteShare),
                nameof(FootballMatchTrainingRow.AwayVoteShare),
                nameof(FootballMatchTrainingRow.AverageWinnerConfidence)))
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                labelColumnName: "Label",
                featureColumnName: "Features"))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue(
                outputColumnName: "PredictedLabel"));
}
