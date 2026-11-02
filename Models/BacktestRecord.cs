public class BacktestRecord
{
    public string Date { get; set; }

    public string Source { get; set; }

    public string HomeTeam { get; set; }
    public string AwayTeam { get; set; }

    public string PredictedResult { get; set; }
    public string PredictedBtts { get; set; }
    public string PredictedGoals { get; set; }

    public int ActualHomeGoals { get; set; }
    public int ActualAwayGoals { get; set; }

    public string ActualResult { get; set; }
    public string ActualBtts { get; set; }
    public string ActualGoals { get; set; }

    public bool CorrectResult { get; set; }
    public bool CorrectBtts { get; set; }
    public bool CorrectGoals { get; set; }
}