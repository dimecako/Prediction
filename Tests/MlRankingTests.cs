using Prediction.Entities;
using Prediction.ML;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class MlRankingTests
{
    private static readonly string[] AllSources =
    [
        "Forebet", "Statarea", "Vitibet", "PredictZ", "ZuluBet", "WinDrawWin",
        "Feedinco", "SoccerVista", "FootyStats", "Oddslot", "TheDatabetics"
    ];

    [Fact]
    public void BuildMatchFeatureRowPopulatesAllElevenSourcesAndConsensusShares()
    {
        var match = new Match
        {
            MatchDate = new DateOnly(2026, 10, 20),
            HomeTeam = "Home",
            AwayTeam = "Away",
            Result = new MatchResult { HomeGoals = 2, AwayGoals = 1, TotalGoals = 3, Btts = true, Result = "1" }
        };

        foreach (string src in AllSources)
        {
            match.PredictionSnapshots.Add(new PredictionSnapshot
            {
                Source = src,
                PredictedResult = "1",
                Confidence = 0.72,
                Btts = "YES",
                Goals = "O3.5",
                CapturedAtUtc = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc)
            });
        }

        var row = FootballModelTrainer.BuildMatchFeatureRow(match);
        Assert.NotNull(row);
        Assert.Equal(11f, row.SourceCount);
        Assert.Equal(1f, row.HomeVoteShare);
        Assert.Equal(0f, row.DrawVoteShare);
        Assert.Equal(0f, row.AwayVoteShare);
        Assert.Equal(1f, row.BttsYesShare);
        Assert.Equal(1f, row.Over25Share);
        Assert.Equal("1", row.FeedincoResult);
        Assert.Equal("1", row.SoccerVistaResult);
        Assert.Equal("1", row.FootyStatsResult);
        Assert.Equal("1", row.OddslotResult);
        Assert.Equal("1", row.TheDatabeticsResult);
        Assert.Equal("O2.5", row.FeedincoGoals);
        Assert.Equal("O2.5", row.TheDatabeticsGoals);
    }

    [Fact]
    public void ChronologicalSplitNeverLeaksFutureMatchesIntoTrainingSet()
    {
        var rows = Enumerable.Range(1, 20).Select(day => new FootballMatchTrainingRow
        {
            MatchDate = new DateTime(2026, 10, 21 - day),
            HomeTeam = $"Home {day}",
            AwayTeam = $"Away {day}",
            ActualResult = "1",
            ActualBtts = "YES",
            ActualGoals = "O2.5"
        }).ToList();

        var (train, test) = MlMatchRanker.SplitChronologically(rows, 0.20);
        Assert.Equal(16, train.Count);
        Assert.Equal(4, test.Count);
        Assert.True(train.Max(x => x.MatchDate) <= test.Min(x => x.MatchDate));
    }

    [Fact]
    public void MlMatchRankerTrainsAndProducesNormalizedCandidateProbabilities()
    {
        var history = new List<Match>();
        for (int i = 0; i < 30; i++)
        {
            string outcome = i % 3 == 0 ? "1" : i % 3 == 1 ? "2" : "X";
            int hg = outcome == "1" ? 2 : outcome == "2" ? 0 : 1;
            int ag = outcome == "1" ? 1 : outcome == "2" ? 2 : 1;
            var m = new Match
            {
                Id = i + 1,
                MatchDate = new DateOnly(2026, 9, 1).AddDays(i),
                HomeTeam = $"TeamH{i}",
                AwayTeam = $"TeamA{i}",
                Result = new MatchResult
                {
                    HomeGoals = hg,
                    AwayGoals = ag,
                    TotalGoals = hg + ag,
                    Btts = hg > 0 && ag > 0,
                    Result = outcome
                }
            };
            foreach (string src in AllSources.Take(6))
            {
                m.PredictionSnapshots.Add(new PredictionSnapshot
                {
                    Source = src,
                    PredictedResult = outcome,
                    Confidence = outcome == "1" ? 0.75 : 0.65,
                    Btts = hg > 0 && ag > 0 ? "YES" : "NO",
                    Goals = hg + ag > 2 ? "O2.5" : "U2.5",
                    CapturedAtUtc = DateTime.UtcNow.AddDays(-10)
                });
            }
            history.Add(m);
        }

        var ranker = MlMatchRanker.TrainFromHistory(history);
        Assert.NotNull(ranker);

        var target = history[0];
        var probs = ranker.Predict(target);
        Assert.NotNull(probs);
        Assert.InRange(probs.ResultProbabilities.Values.Sum(), 0.99, 1.01);
        Assert.InRange(probs.BttsProbabilities.Values.Sum(), 0.99, 1.01);
        Assert.InRange(probs.GoalsProbabilities.Values.Sum(), 0.99, 1.01);

        double? winProb = probs.GetCandidateProbability(new TicketCandidate("1X2", "1", "1X2|1|70+", ""));
        Assert.NotNull(winProb);
        Assert.InRange(winProb.Value, 0.0, 1.0);
    }
}
