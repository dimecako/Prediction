using Microsoft.ML;

namespace Prediction.ML;

public sealed class GoalsModelTrainer
{
    private readonly MLContext _mlContext;

    public GoalsModelTrainer()
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

        Console.WriteLine($"[ML-GOALS] Training matches (all 11 sources): {rows.Count}");

        var (trainRows, testRows) = MlMatchRanker.SplitChronologically(rows, 0.20);
        var trainData = _mlContext.Data.LoadFromEnumerable(trainRows);
        var testData = _mlContext.Data.LoadFromEnumerable(testRows);

        var pipeline = BuildPipeline(_mlContext);

        var evalModel = pipeline.Fit(trainData);
        var predictions = evalModel.Transform(testData);
        var metrics = _mlContext.MulticlassClassification.Evaluate(
            predictions,
            labelColumnName: "Label");

        Console.WriteLine($"[ML-GOALS] Out-of-time validation ({trainRows.Count} train / {testRows.Count} chronological test)");
        Console.WriteLine($"[ML-GOALS] MicroAccuracy: {metrics.MicroAccuracy:P2}");
        Console.WriteLine($"[ML-GOALS] MacroAccuracy: {metrics.MacroAccuracy:P2}");
        Console.WriteLine($"[ML-GOALS] LogLoss: {metrics.LogLoss:F4}");
        MlMatchRanker.PrintProbabilityCalibration(
            _mlContext, evalModel, testData, testRows, x => x.ActualGoals, "ML-GOALS O/U 2.5");

        var fullData = _mlContext.Data.LoadFromEnumerable(rows);
        var fullModel = pipeline.Fit(fullData);

        if (saveToDisk)
        {
            const string modelPath = "GoalsModel.zip";
            _mlContext.Model.Save(fullModel, fullData.Schema, modelPath);
            Console.WriteLine($"[ML-GOALS] Model saved: {modelPath}");
        }

        return fullModel;
    }

    internal static IEstimator<ITransformer> BuildPipeline(MLContext mlContext) =>
        mlContext.Transforms.Conversion.MapValueToKey(
            outputColumnName: "Label",
            inputColumnName: nameof(FootballMatchTrainingRow.ActualGoals))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("ForebetGoalsEncoded", nameof(FootballMatchTrainingRow.ForebetGoals)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("StatareaGoalsEncoded", nameof(FootballMatchTrainingRow.StatareaGoals)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("VitibetGoalsEncoded", nameof(FootballMatchTrainingRow.VitibetGoals)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("PredictZGoalsEncoded", nameof(FootballMatchTrainingRow.PredictZGoals)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("ZuluBetGoalsEncoded", nameof(FootballMatchTrainingRow.ZuluBetGoals)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("WinDrawWinGoalsEncoded", nameof(FootballMatchTrainingRow.WinDrawWinGoals)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("FeedincoGoalsEncoded", nameof(FootballMatchTrainingRow.FeedincoGoals)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("SoccerVistaGoalsEncoded", nameof(FootballMatchTrainingRow.SoccerVistaGoals)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("FootyStatsGoalsEncoded", nameof(FootballMatchTrainingRow.FootyStatsGoals)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("OddslotGoalsEncoded", nameof(FootballMatchTrainingRow.OddslotGoals)))
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("TheDatabeticsGoalsEncoded", nameof(FootballMatchTrainingRow.TheDatabeticsGoals)))
            .Append(mlContext.Transforms.Concatenate(
                "Features",
                "ForebetGoalsEncoded",
                "StatareaGoalsEncoded",
                "VitibetGoalsEncoded",
                "PredictZGoalsEncoded",
                "ZuluBetGoalsEncoded",
                "WinDrawWinGoalsEncoded",
                "FeedincoGoalsEncoded",
                "SoccerVistaGoalsEncoded",
                "FootyStatsGoalsEncoded",
                "OddslotGoalsEncoded",
                "TheDatabeticsGoalsEncoded",
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
