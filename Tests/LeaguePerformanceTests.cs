using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class LeaguePerformanceTests
{
    [Fact]
    public void AccuracyIsSplitByLeagueAndMarketAndUnknownLabelsAreNotInvented()
    {
        var home = Fixture("League A", "1", 2, 1);
        var away = Fixture("League B", "1", 0, 1);
        var unknown = Fixture(null, "1", 1, 0);
        var rows = LeaguePerformanceAnalyzer.Analyze(new[] { home, away, unknown });
        Assert.Equal(9, rows.Count);
        Assert.Equal(1, rows.Single(x => x.League == "League A" && x.Market == "1X2").Correct);
        Assert.Equal(0, rows.Single(x => x.League == "League B" && x.Market == "1X2").Correct);
        Assert.Contains(rows, x => x.League == TicketPerformanceAnalyzer.UnknownLeague);
        Assert.Equal(1, rows.Single(x => x.League == "League A" && x.Market == "BTTS").Correct);
        Assert.Equal(0, rows.Single(x => x.League == "League B" && x.Market == "Goals O/U 2.5").Correct);
    }

    [Fact]
    public void PostCutoffCapturesCannotChangeHistoricalAccuracy()
    {
        var match = Fixture("League A", "1", 2, 0);
        match.PredictionSnapshots.Add(new()
        {
            Source = "Forebet", PredictedResult = "2", Btts = "YES", Goals = "O2.5",
            CapturedAtUtc = WeightedConsensusCalculator.GetCutoffUtc(match.MatchDate)
        });
        var row = LeaguePerformanceAnalyzer.Analyze(new[] { match }).Single(x => x.Market == "1X2");
        Assert.Equal(1, row.Total);
        Assert.Equal(1, row.Correct);
    }

    [Fact]
    public void SameDayOnlyHistoryIsExplicitlyAbsent()
    {
        var match = Fixture("League A", "1", 2, 0);
        match.PredictionSnapshots.Single().CapturedAtUtc =
            WeightedConsensusCalculator.GetCutoffUtc(match.MatchDate).AddHours(1);
        Assert.Empty(LeaguePerformanceAnalyzer.Analyze(new[] { match }));
    }

    [Fact]
    public void LatestPreDatePerSourceIsUsedAndTiesAbstain()
    {
        var match = Fixture("League A", "1", 2, 0);
        match.PredictionSnapshots.Add(new()
        {
            Source = "FOREBET", PredictedResult = "2",
            CapturedAtUtc = match.PredictionSnapshots.First().CapturedAtUtc.AddMinutes(1)
        });
        var rows = LeaguePerformanceAnalyzer.Analyze(new[] { match });
        Assert.Equal(0, rows.Single(x => x.Market == "1X2").Correct);
        match.PredictionSnapshots.Add(new()
        {
            Source = "Statarea", PredictedResult = "1",
            CapturedAtUtc = match.PredictionSnapshots.First().CapturedAtUtc
        });
        Assert.DoesNotContain(LeaguePerformanceAnalyzer.Analyze(new[] { match }), x => x.Market == "1X2");
    }

    [Fact]
    public void UngradedMatchesAndEmptyInputDoNotProduceAccuracy()
    {
        var match = Fixture("League A", "1", 2, 0);
        match.Result = null;
        Assert.Empty(LeaguePerformanceAnalyzer.Analyze(new[] { match }));
        Assert.Empty(LeaguePerformanceAnalyzer.Analyze([]));
    }

    private static Match Fixture(string? league, string pick, int home, int away) => new()
    {
        League = league, MatchDate = new(2030, 1, 2),
        Result = new()
        {
            HomeGoals = home, AwayGoals = away, TotalGoals = home + away,
            Btts = home > 0 && away > 0, Result = home > away ? "1" : home < away ? "2" : "X"
        },
        PredictionSnapshots = new List<PredictionSnapshot>
        {
            new()
            {
                Source = "Forebet", PredictedResult = pick, Btts = "YES", Goals = "O2.5",
                CapturedAtUtc = new(2030, 1, 1, 20, 0, 0, DateTimeKind.Utc)
            }
        }
    };
}
