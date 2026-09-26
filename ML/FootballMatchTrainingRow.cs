namespace Prediction.ML;

public sealed class FootballMatchTrainingRow
{
    public DateTime MatchDate { get; set; }

    public string HomeTeam { get; set; } = "";
    public string AwayTeam { get; set; } = "";

    public string ForebetResult { get; set; } = "";
    public float ForebetConfidence { get; set; }
    public string ForebetBtts { get; set; } = "";
    public string ForebetGoals { get; set; } = "";

    public string StatareaResult { get; set; } = "";
    public float StatareaConfidence { get; set; }
    public string StatareaBtts { get; set; } = "";
    public string StatareaGoals { get; set; } = "";

    public string VitibetResult { get; set; } = "";
    public float VitibetConfidence { get; set; }
    public string VitibetBtts { get; set; } = "";
    public string VitibetGoals { get; set; } = "";

    public string PredictZResult { get; set; } = "";
    public float PredictZConfidence { get; set; }
    public string PredictZBtts { get; set; } = "";
    public string PredictZGoals { get; set; } = "";

    public string ZuluBetResult { get; set; } = "";
    public float ZuluBetConfidence { get; set; }
    public string ZuluBetBtts { get; set; } = "";
    public string ZuluBetGoals { get; set; } = "";

    public string WinDrawWinResult { get; set; } = "";
    public float WinDrawWinConfidence { get; set; }
    public string WinDrawWinBtts { get; set; } = "";
    public string WinDrawWinGoals { get; set; } = "";

    public float SourceCount { get; set; }

    public string ActualResult { get; set; } = "";
    public string ActualBtts { get; set; } = "";
    public string ActualGoals { get; set; } = "";
}
