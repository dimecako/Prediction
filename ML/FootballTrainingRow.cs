namespace Prediction.ML;

public sealed class FootballTrainingRow
{
    public DateTime MatchDate { get; set; }

    public string HomeTeam { get; set; } = "";
    public string AwayTeam { get; set; } = "";

    public string Source { get; set; } = "";

    public string PredictedResult { get; set; } = "";
    public string PredictedBtts { get; set; } = "";
    public string PredictedGoals { get; set; } = "";

    public float Confidence { get; set; }

    public string ActualResult { get; set; } = "";
    public string ActualBtts { get; set; } = "";
    public string ActualGoals { get; set; } = "";
}
