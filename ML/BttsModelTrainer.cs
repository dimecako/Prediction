using Microsoft.ML;

namespace Prediction.ML;

public sealed class BttsModelTrainer
{
    private readonly MLContext _mlContext;

    public BttsModelTrainer()
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

        Console.WriteLine($"[ML-BTTS] Training matches (all 11 sources): {rows.Count}");

        var (trainRows, testRows) = MlMatchRanker.SplitChronologically(rows, 0.20);
        var trainData = _mlContext.Data.LoadFromEnumerable(trainRows);
        var testData = _mlContext.Data.LoadFromEnumerable(testRows);

        var pipeline = BuildPipeline(_mlContext);

        var evalModel = pipeline.Fit(trainData);
        var predictions = evalModel.Transform(testData);
        var metrics = _mlContext.MulticlassClassification.Evaluate(
            predictions,
            labelColumnName: "Label");

        Console.WriteLine($"[ML-BTTS] Out-of-time validation ({trainRows.Count} train / {testRows.Count} chronological test)");
        Console.WriteLine($"[ML-BTTS] MicroAccuracy: {metrics.MicroAccuracy:P2}");
        Console.WriteLine($"[ML-BTTS] MacroAccuracy: {metrics.MacroAccuracy:P2}");
        Console.WriteLine($"[ML-BTTS] LogLoss: {metrics.LogLoss:F4}");
        MlMatchRanker.PrintProbabilityCalibration(
            _mlContext, evalModel, testData, testRows, x => x.ActualBtts, "ML-BTTS YES/NO");

        var fullData = _mlContext.Data.LoadFromEnumerable(rows);
        var fullModel = pipeline.Fit(fullData);

        if (saveToDisk)
        {
            const string modelPath = "BttsModel.zip";
            _mlContext.Model.Save(fullModel, fullData.Schema, modelPath);
            Console.WriteLine($"[ML-BTTS] Model saved: {modelPath}");
        }

        return fullModel;
    }

    internal static IEstimator<ITransformer> BuildPipeline(MLContext mlContext) =>
        mlContext.Transforms.Conversion.MapValueToKey(
            outputColumnName: "Label",
            inputColumnName: nameof(FootballMatchTrainingRow.ActualBtts))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("ForebetBttsEncoded", nameof(FootballMatchTrainingRow.ForebetBtts)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("StatareaBttsEncoded", nameof(FootballMatchTrainingRow.StatareaBtts)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("VitibetBttsEncoded", nameof(FootballMatchTrainingRow.VitibetBtts)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("PredictZBttsEncoded", nameof(FootballMatchTrainingRow.PredictZBtts)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("ZuluBetBttsEncoded", nameof(FootballMatchTrainingRow.ZuluBetBtts)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("WinDrawWinBttsEncoded", nameof(FootballMatchTrainingRow.WinDrawWinBtts)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("FeedincoBttsEncoded", nameof(FootballMatchTrainingRow.FeedincoBtts)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("SoccerVistaBttsEncoded", nameof(FootballMatchTrainingRow.SoccerVistaBtts)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("FootyStatsBttsEncoded", nameof(FootballMatchTrainingRow.FootyStatsBtts)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("OddslotBttsEncoded", nameof(FootballMatchTrainingRow.OddslotBtts)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("TheDatabeticsBttsEncoded", nameof(FootballMatchTrainingRow.TheDatabeticsBtts)))
            .Append(mlContext.Transforms.Concatenate(
                "Features",
                "ForebetBttsEncoded",
                "StatareaBttsEncoded",
                "VitibetBttsEncoded",
                "PredictZBttsEncoded",
                "ZuluBetBttsEncoded",
                "WinDrawWinBttsEncoded",
                "FeedincoBttsEncoded",
                "SoccerVistaBttsEncoded",
                "FootyStatsBttsEncoded",
                "OddslotBttsEncoded",
                "TheDatabeticsBttsEncoded",
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
                nameof(FootballMatchTrainingRow.BttsYesShare),
                nameof(FootballMatchTrainingRow.Over25Share)))
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                labelColumnName: "Label",
                featureColumnName: "Features"))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue(
                outputColumnName: "PredictedLabel"));
}
