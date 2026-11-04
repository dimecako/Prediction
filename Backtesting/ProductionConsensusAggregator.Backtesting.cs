using System;

public partial class ProductionConsensusAggregator
{
       private string GetGoalMarket(string score)
    {
        if (string.IsNullOrWhiteSpace(score) || !score.Contains("-")) return "-";
        var p = score.Split('-');
        if (!int.TryParse(p[0], out int hg) || !int.TryParse(p[1], out int ag)) return "-";
        int totalGoals = hg + ag;
        if (totalGoals >= 4) return "O3.5";
        if (totalGoals >= 3) return "O2.5";
        return "U2.5";
    }

    private string GetBttsMarket(string score)
    {
        if (string.IsNullOrWhiteSpace(score) || !score.Contains("-")) return "-";
        var p = score.Split('-');
        if (!int.TryParse(p[0], out int hg) || !int.TryParse(p[1], out int ag)) return "-";
        return hg > 0 && ag > 0 ? "YES" : "NO";
    }

    private string GetResultMarket(int homeGoals, int awayGoals)
    {
        if (homeGoals > awayGoals)
            return "1";

        if (homeGoals < awayGoals)
            return "2";

        return "X";
    }

    private BacktestRecord CreateBacktestRecord(SiteMatch prediction, string date, int homeGoals, int awayGoals)
    {
        string actualScore =
            $"{homeGoals}-{awayGoals}";

        string actualResult =
            GetResultMarket(homeGoals, awayGoals);

        string actualBtts =
            GetBttsMarket(actualScore);

        string actualGoals =
            GetGoalMarket(actualScore);

        return new BacktestRecord
        {
            Date = date,

            Source = prediction.SiteName,

            HomeTeam = prediction.HomeTeam,
            AwayTeam = prediction.AwayTeam,

            PredictedResult = prediction.Tip,
            PredictedBtts = prediction.BttsMarket,
            PredictedGoals = prediction.GoalsMarket,

            ActualHomeGoals = homeGoals,
            ActualAwayGoals = awayGoals,

            ActualResult = actualResult,
            ActualBtts = actualBtts,
            ActualGoals = actualGoals,

            CorrectResult =
                prediction.Tip == actualResult,

            CorrectBtts =
                prediction.BttsMarket == actualBtts,

            CorrectGoals =
                prediction.GoalsMarket == actualGoals
        };
    }   
}