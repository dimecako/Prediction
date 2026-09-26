using Microsoft.ML;

namespace Prediction.ML;

public sealed class ResultModelTrainer
{
    private readonly MLContext _mlContext;

    public ResultModelTrainer()
    {
        _mlContext = new MLContext(seed: 42);
    }

    public void Train(List<FootballMatchTrainingRow> rows)
    {
        if (rows.Count < 10)
        {
            throw new InvalidOperationException(
                "Not enough match-level training data.");
        }

        Console.WriteLine(
            $"[ML-RESULT] Training matches: {rows.Count}");

        var data = _mlContext.Data.LoadFromEnumerable(rows);

        var split = _mlContext.Data.TrainTestSplit(
            data,
            testFraction: 0.20,
            seed: 42);

        var pipeline = _mlContext.Transforms.Conversion.MapValueToKey(
            outputColumnName: "Label",
            inputColumnName: nameof(FootballMatchTrainingRow.ActualResult))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "ForebetEncoded",
                nameof(FootballMatchTrainingRow.ForebetResult)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "StatareaEncoded",
                nameof(FootballMatchTrainingRow.StatareaResult)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "VitibetEncoded",
                nameof(FootballMatchTrainingRow.VitibetResult)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "PredictZEncoded",
                nameof(FootballMatchTrainingRow.PredictZResult)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "ZuluBetEncoded",
                nameof(FootballMatchTrainingRow.ZuluBetResult)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "WinDrawWinEncoded",
                nameof(FootballMatchTrainingRow.WinDrawWinResult)))
            .Append(_mlContext.Transforms.Concatenate(
                "Features",
                "ForebetEncoded",
                "StatareaEncoded",
                "VitibetEncoded",
                "PredictZEncoded",
                "ZuluBetEncoded",
                "WinDrawWinEncoded",
                nameof(FootballMatchTrainingRow.ForebetConfidence),
                nameof(FootballMatchTrainingRow.StatareaConfidence),
                nameof(FootballMatchTrainingRow.VitibetConfidence),
                nameof(FootballMatchTrainingRow.PredictZConfidence),
                nameof(FootballMatchTrainingRow.ZuluBetConfidence),
                nameof(FootballMatchTrainingRow.WinDrawWinConfidence),
                nameof(FootballMatchTrainingRow.SourceCount)))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                labelColumnName: "Label",
                featureColumnName: "Features"))
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue(
                outputColumnName: "PredictedLabel"));

        var model = pipeline.Fit(split.TrainSet);

        var predictions = model.Transform(split.TestSet);

        var metrics = _mlContext.MulticlassClassification.Evaluate(
            predictions,
            labelColumnName: "Label");

        Console.WriteLine("[ML-RESULT] 1/X/2 evaluation");
        Console.WriteLine($"[ML-RESULT] MicroAccuracy: {metrics.MicroAccuracy:P2}");
        Console.WriteLine($"[ML-RESULT] MacroAccuracy: {metrics.MacroAccuracy:P2}");
        Console.WriteLine($"[ML-RESULT] LogLoss: {metrics.LogLoss:F4}");

        const string modelPath = "ResultModel.zip";

        _mlContext.Model.Save(
            model,
            split.TrainSet.Schema,
            modelPath);

        Console.WriteLine($"[ML-RESULT] Model saved: {modelPath}");
    }
}
