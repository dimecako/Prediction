using Microsoft.ML;

namespace Prediction.ML;

public sealed class BttsModelTrainer
{
    private readonly MLContext _mlContext;

    public BttsModelTrainer()
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

        Console.WriteLine($"[ML-BTTS] Training matches: {rows.Count}");

        var data = _mlContext.Data.LoadFromEnumerable(rows);

        var split = _mlContext.Data.TrainTestSplit(
            data,
            testFraction: 0.20,
            seed: 42);

        var pipeline = _mlContext.Transforms.Conversion.MapValueToKey(
            outputColumnName: "Label",
            inputColumnName: nameof(FootballMatchTrainingRow.ActualBtts))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "ForebetBttsEncoded",
                nameof(FootballMatchTrainingRow.ForebetBtts)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "StatareaBttsEncoded",
                nameof(FootballMatchTrainingRow.StatareaBtts)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "VitibetBttsEncoded",
                nameof(FootballMatchTrainingRow.VitibetBtts)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "PredictZBttsEncoded",
                nameof(FootballMatchTrainingRow.PredictZBtts)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "ZuluBetBttsEncoded",
                nameof(FootballMatchTrainingRow.ZuluBetBtts)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "WinDrawWinBttsEncoded",
                nameof(FootballMatchTrainingRow.WinDrawWinBtts)))
            .Append(_mlContext.Transforms.Concatenate(
                "Features",
                "ForebetBttsEncoded",
                "StatareaBttsEncoded",
                "VitibetBttsEncoded",
                "PredictZBttsEncoded",
                "ZuluBetBttsEncoded",
                "WinDrawWinBttsEncoded",
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

        Console.WriteLine("[ML-BTTS] Evaluation");
        Console.WriteLine($"[ML-BTTS] MicroAccuracy: {metrics.MicroAccuracy:P2}");
        Console.WriteLine($"[ML-BTTS] MacroAccuracy: {metrics.MacroAccuracy:P2}");
        Console.WriteLine($"[ML-BTTS] LogLoss: {metrics.LogLoss:F4}");

        const string modelPath = "BttsModel.zip";
        _mlContext.Model.Save(model, split.TrainSet.Schema, modelPath);
        Console.WriteLine($"[ML-BTTS] Model saved: {modelPath}");
    }
}
