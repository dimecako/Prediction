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
            .ToListAsync();

        var rows = new List<FootballMatchTrainingRow>();

        foreach (var match in matches)
        {
            if (match.Result == null)
                continue;

            var latestBySource = match.PredictionSnapshots
                .Where(x => !string.IsNullOrWhiteSpace(x.PredictedResult))
                .GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.CapturedAtUtc).First(),
                    StringComparer.OrdinalIgnoreCase);

            if (latestBySource.Count == 0)
                continue;

            var row = new FootballMatchTrainingRow
            {
                MatchDate = match.MatchDate.ToDateTime(TimeOnly.MinValue),
                HomeTeam = match.HomeTeam,
                AwayTeam = match.AwayTeam,
                SourceCount = latestBySource.Count,
                ActualResult = match.Result.Result,
                ActualBtts = match.Result.Btts ? "YES" : "NO",
                ActualGoals = match.Result.TotalGoals > 2
                    ? "O2.5"
                    : "U2.5"
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

            rows.Add(row);
        }

        return rows;
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
            prediction.Goals ?? "");
    }
}
