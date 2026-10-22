using Microsoft.ML;
using Microsoft.EntityFrameworkCore;
using Prediction.Data;
using Prediction.Entities;

namespace Prediction.ML;

public sealed class FootballModelTrainer
{
    private readonly FootballDbContext _db;

    public FootballModelTrainer(FootballDbContext db)
    {
        _db = db;
    }

    public async Task<List<FootballTrainingRow>> LoadTrainingDataAsync()
    {
        var matches = await _db.Matches
            .AsNoTracking()
            .Include(x => x.Result)
            .Include(x => x.PredictionSnapshots)
            .Where(x =>
                x.Result != null &&
                x.PredictionSnapshots.Any())
            .ToListAsync();

        var rows = new List<FootballTrainingRow>();

        foreach (var match in matches)
        {
            if (match.Result == null)
                continue;

            foreach (var snapshot in match.PredictionSnapshots)
            {
                if (string.IsNullOrWhiteSpace(snapshot.PredictedResult))
                    continue;

                rows.Add(new FootballTrainingRow
                {
                    MatchDate = match.MatchDate.ToDateTime(TimeOnly.MinValue),

                    HomeTeam = match.HomeTeam,
                    AwayTeam = match.AwayTeam,

                    Source = snapshot.Source,

                    PredictedResult = snapshot.PredictedResult ?? "",
                    PredictedBtts = snapshot.Btts ?? "",
                    PredictedGoals = snapshot.Goals ?? "",

                    Confidence = (float)(snapshot.Confidence ?? 0),

                    ActualResult = match.Result.Result,
                    ActualBtts = match.Result.Btts ? "YES" : "NO",

                    ActualGoals = match.Result.TotalGoals > 2
                        ? "O2.5"
                        : "U2.5"
                });
            }
        }

        return rows;
    }

    public async Task<List<FootballMatchTrainingRow>> LoadMatchTrainingDataAsync()
    {
        var matches = await _db.Matches
            .AsNoTracking()
            .Include(x => x.Result)
            .Include(x => x.PredictionSnapshots)
            .Where(x =>
                x.Result != null &&
                x.PredictionSnapshots.Any())
            .OrderBy(x => x.MatchDate)
            .ThenBy(x => x.Id)
            .ToListAsync();

        return BuildMatchTrainingRows(matches);
    }

    public static List<FootballMatchTrainingRow> BuildMatchTrainingRows(IEnumerable<Match> matches)
    {
        var rows = new List<FootballMatchTrainingRow>();

        foreach (var match in matches.OrderBy(x => x.MatchDate).ThenBy(x => x.Id))
        {
            var row = BuildMatchFeatureRow(match);
            if (row != null)
                rows.Add(row);
        }

        return rows;
    }

    public static FootballMatchTrainingRow? BuildMatchFeatureRow(Match match)
    {
        var latestSnapshots = Prediction.Services.CalibrationAnalyzer.LatestPerSource(match);
        if (latestSnapshots.Count == 0)
            return null;

        var latestBySource = latestSnapshots.ToDictionary(
            x => x.Source.Trim(),
            x => x,
            StringComparer.OrdinalIgnoreCase);

        var resultVotes = latestSnapshots
            .Where(x => x.PredictedResult is "1" or "X" or "2")
            .ToList();
        var bttsVotes = latestSnapshots
            .Where(x => x.Btts is "YES" or "NO")
            .ToList();
        var goalsVotes = latestSnapshots
            .Select(x => ProductionConsensusAggregator.NormalizeGoalsTo25(x.Goals))
            .Where(x => x is "O2.5" or "U2.5")
            .ToList();

        var consensus = Prediction.Services.CalibrationAnalyzer.BuildResultConsensus(latestSnapshots);

        var row = new FootballMatchTrainingRow
        {
            MatchDate = match.MatchDate.ToDateTime(TimeOnly.MinValue),
            HomeTeam = match.HomeTeam,
            AwayTeam = match.AwayTeam,
            SourceCount = latestBySource.Count,
            HomeVoteShare = resultVotes.Count > 0
                ? (float)resultVotes.Count(x => x.PredictedResult == "1") / resultVotes.Count
                : 0f,
            DrawVoteShare = resultVotes.Count > 0
                ? (float)resultVotes.Count(x => x.PredictedResult == "X") / resultVotes.Count
                : 0f,
            AwayVoteShare = resultVotes.Count > 0
                ? (float)resultVotes.Count(x => x.PredictedResult == "2") / resultVotes.Count
                : 0f,
            AverageWinnerConfidence = (float)((consensus?.AverageProbability ?? 0.0) / 100.0),
            BttsYesShare = bttsVotes.Count > 0
                ? (float)bttsVotes.Count(x => x.Btts == "YES") / bttsVotes.Count
                : 0f,
            Over25Share = goalsVotes.Count > 0
                ? (float)goalsVotes.Count(x => x == "O2.5") / goalsVotes.Count
                : 0f,
            ActualResult = match.Result?.Result ?? "1",
            ActualBtts = match.Result == null ? "NO" : (match.Result.Btts ? "YES" : "NO"),
            ActualGoals = match.Result == null ? "U2.5" : (match.Result.TotalGoals > 2 ? "O2.5" : "U2.5")
        };

        ApplySource(latestBySource, "Forebet",
            (result, confidence, btts, goals) =>
            {
                row.ForebetResult = result;
                row.ForebetConfidence = confidence;
                row.ForebetBtts = btts;
                row.ForebetGoals = goals;
            });

        ApplySource(latestBySource, "Statarea",
            (result, confidence, btts, goals) =>
            {
                row.StatareaResult = result;
                row.StatareaConfidence = confidence;
                row.StatareaBtts = btts;
                row.StatareaGoals = goals;
            });

        ApplySource(latestBySource, "Vitibet",
            (result, confidence, btts, goals) =>
            {
                row.VitibetResult = result;
                row.VitibetConfidence = confidence;
                row.VitibetBtts = btts;
                row.VitibetGoals = goals;
            });

        ApplySource(latestBySource, "PredictZ",
            (result, confidence, btts, goals) =>
            {
                row.PredictZResult = result;
                row.PredictZConfidence = confidence;
                row.PredictZBtts = btts;
                row.PredictZGoals = goals;
            });

        ApplySource(latestBySource, "ZuluBet",
            (result, confidence, btts, goals) =>
            {
                row.ZuluBetResult = result;
                row.ZuluBetConfidence = confidence;
                row.ZuluBetBtts = btts;
                row.ZuluBetGoals = goals;
            });

        ApplySource(latestBySource, "WinDrawWin",
            (result, confidence, btts, goals) =>
            {
                row.WinDrawWinResult = result;
                row.WinDrawWinConfidence = confidence;
                row.WinDrawWinBtts = btts;
                row.WinDrawWinGoals = goals;
            });

        ApplySource(latestBySource, "Feedinco",
            (result, confidence, btts, goals) =>
            {
                row.FeedincoResult = result;
                row.FeedincoConfidence = confidence;
                row.FeedincoBtts = btts;
                row.FeedincoGoals = goals;
            });

        ApplySource(latestBySource, "SoccerVista",
            (result, confidence, btts, goals) =>
            {
                row.SoccerVistaResult = result;
                row.SoccerVistaConfidence = confidence;
                row.SoccerVistaBtts = btts;
                row.SoccerVistaGoals = goals;
            });

        ApplySource(latestBySource, "FootyStats",
            (result, confidence, btts, goals) =>
            {
                row.FootyStatsResult = result;
                row.FootyStatsConfidence = confidence;
                row.FootyStatsBtts = btts;
                row.FootyStatsGoals = goals;
            });

        ApplySource(latestBySource, "Oddslot",
            (result, confidence, btts, goals) =>
            {
                row.OddslotResult = result;
                row.OddslotConfidence = confidence;
                row.OddslotBtts = btts;
                row.OddslotGoals = goals;
            });

        ApplySource(latestBySource, "TheDatabetics",
            (result, confidence, btts, goals) =>
            {
                row.TheDatabeticsResult = result;
                row.TheDatabeticsConfidence = confidence;
                row.TheDatabeticsBtts = btts;
                row.TheDatabeticsGoals = goals;
            });

        return row;
    }

    private static void ApplySource(
        Dictionary<string, PredictionSnapshot> predictions,
        string source,
        Action<string, float, string, string> apply)
    {
        if (!predictions.TryGetValue(source, out var prediction))
            return;

        apply(
            prediction.PredictedResult ?? "",
            (float)(prediction.Confidence ?? 0),
            prediction.Btts ?? "",
            ProductionConsensusAggregator.NormalizeGoalsTo25(prediction.Goals) ?? "");
    }
}
