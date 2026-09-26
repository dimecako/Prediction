using Microsoft.ML;

namespace Prediction.ML;

public sealed class GoalsModelTrainer
{
    private readonly MLContext _mlContext;

    public GoalsModelTrainer()
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

        Console.WriteLine($"[ML-GOALS] Training matches: {rows.Count}");

        var data = _mlContext.Data.LoadFromEnumerable(rows);

        var split = _mlContext.Data.TrainTestSplit(
            data,
            testFraction: 0.20,
            seed: 42);

        var pipeline = _mlContext.Transforms.Conversion.MapValueToKey(
            outputColumnName: "Label",
            inputColumnName: nameof(FootballMatchTrainingRow.ActualGoals))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "ForebetGoalsEncoded",
                nameof(FootballMatchTrainingRow.ForebetGoals)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "StatareaGoalsEncoded",
                nameof(FootballMatchTrainingRow.StatareaGoals)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding(
                "VitibetGoalsEncoded",
                nameof(FootballMatchTrainingRow.VitibetGoals)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding("PredictZGoalsEncoded", nameof(FootballMatchTrainingRow.PredictZGoals)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding("ZuluBetGoalsEncoded", nameof(FootballMatchTrainingRow.ZuluBetGoals)))
            .Append(_mlContext.Transforms.Categorical.OneHotEncoding("WinDrawWinGoalsEncoded", nameof(FootballMatchTrainingRow.WinDrawWinGoals)))
            .Append(_mlContext.Transforms.Concatenate(
                "Features",
                "ForebetGoalsEncoded",
                "StatareaGoalsEncoded",
                "VitibetGoalsEncoded",
                "PredictZGoalsEncoded",
                "ZuluBetGoalsEncoded",
                "WinDrawWinGoalsEncoded",
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

        Console.WriteLine("[ML-GOALS] Evaluation");
        Console.WriteLine($"[ML-GOALS] MicroAccuracy: {metrics.MicroAccuracy:P2}");
        Console.WriteLine($"[ML-GOALS] MacroAccuracy: {metrics.MacroAccuracy:P2}");
        Console.WriteLine($"[ML-GOALS] LogLoss: {metrics.LogLoss:F4}");

        const string modelPath = "GoalsModel.zip";
        _mlContext.Model.Save(model, split.TrainSet.Schema, modelPath);
        Console.WriteLine($"[ML-GOALS] Model saved: {modelPath}");

}
}
