using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class WeightedConsensusCalculatorTests
{
    private static readonly DateOnly TargetDate = new(2026, 10, 15);

    [Fact]
    public void WeightUsesSmoothedAccuracy()
    {
        Assert.Equal(0.5, new SourceMarketWeight("New", ConsensusMarket.Result, 0, 0).Weight);
        Assert.Equal(6.0 / 11, new SourceMarketWeight("Small", ConsensusMarket.Result, 1, 1).Weight);
        Assert.Equal(0.75, new SourceMarketWeight("Known", ConsensusMarket.Result, 85, 110).Weight);
    }

    [Fact]
    public void NoHistoryMatchesEqualConsensusInEveryMarket()
    {
        var match = Fixture(TargetDate,
            Snapshot("A", "1", "YES", "O2.5"),
            Snapshot("B", "1", "NO", "O2.5"),
            Snapshot("C", "2", "YES", "U2.5"));

        var weighted = WeightedConsensusCalculator.Calculate(match, []);
        var equal = WeightedConsensusCalculator.Calculate(match, [], weighted: false);

        Assert.Equal("1", weighted.Result.Pick);
        Assert.Equal("YES", weighted.Btts.Pick);
        Assert.Equal("O2.5", weighted.Goals.Pick);
        Assert.Equal(equal.ToSnapshot().PredictedResult, weighted.ToSnapshot().PredictedResult);
        Assert.Equal(equal.Btts.Pick, weighted.Btts.Pick);
        Assert.Equal(equal.Goals.Pick, weighted.Goals.Pick);
        Assert.Equal(2.0 / 3, weighted.Result.SupportShare!.Value, 12);
    }

    [Fact]
    public void MarketSpecificWeightsCanChangeWinners()
    {
        var match = Fixture(TargetDate,
            Snapshot("A", "1", "YES", "O2.5"),
            Snapshot("B", "2", "NO", "U2.5"));
        SourceMarketWeight[] weights =
        [
            new("A", ConsensusMarket.Result, 9, 10),
            new("B", ConsensusMarket.Result, 1, 10),
            new("A", ConsensusMarket.Btts, 1, 10),
            new("B", ConsensusMarket.Btts, 9, 10),
            new("A", ConsensusMarket.Goals, 9, 10),
            new("B", ConsensusMarket.Goals, 1, 10)
        ];

        var weighted = WeightedConsensusCalculator.Calculate(match, weights);
        var equal = WeightedConsensusCalculator.Calculate(match, weights, weighted: false);

        Assert.Null(equal.Result.Pick);
        Assert.Equal("1", weighted.Result.Pick);
        Assert.Equal("NO", weighted.Btts.Pick);
        Assert.Equal("O2.5", weighted.Goals.Pick);
        Assert.Equal(0.7, weighted.Result.SupportShare!.Value, 12);
    }

    [Fact]
    public void ReliableMinorityCanOutweighRawMajority()
    {
        var match = Fixture(TargetDate,
            Snapshot("Reliable", "X"),
            Snapshot("WeakA", "1"),
            Snapshot("WeakB", "1"));
        SourceMarketWeight[] weights =
        [
            new("Reliable", ConsensusMarket.Result, 90, 100),
            new("WeakA", ConsensusMarket.Result, 10, 100),
            new("WeakB", ConsensusMarket.Result, 10, 100)
        ];

        Assert.Equal("1", WeightedConsensusCalculator.Calculate(match, weights, false).Result.Pick);
        var weighted = WeightedConsensusCalculator.Calculate(match, weights);
        Assert.Equal("X", weighted.Result.Pick);
        Assert.Equal(1, weighted.Result.SupportingSources);
        Assert.Equal(3, weighted.Result.TotalSources);
        Assert.Equal(95.0 / 125, weighted.Result.SupportShare!.Value, 12);
    }

    [Fact]
    public void LatestEligibleSnapshotIsUsedOncePerSourceWithStableTieBreak()
    {
        var older = Snapshot(" forebet ", "2");
        older.Id = 1;
        older.CapturedAtUtc = Cutoff(TargetDate).AddHours(-3);
        var latest = Snapshot("Forebet", " 1 ", " yes ", " o2.5 ");
        latest.Id = 3;
        var sameTime = Snapshot("FOREBET", "X");
        sameTime.Id = 2;
        var late = Snapshot("Forebet", "2");
        late.Id = 4;
        late.CapturedAtUtc = Cutoff(TargetDate);

        var match = Fixture(TargetDate, older, latest, sameTime, late);
        var eligible = Assert.Single(WeightedConsensusCalculator.GetEligiblePredictions(match));

        Assert.Equal(3, eligible.Id);
        var vote = WeightedConsensusCalculator.Calculate(match, []);
        Assert.Equal("1", vote.Result.Pick);
        Assert.Equal("YES", vote.Btts.Pick);
        Assert.Equal("O2.5", vote.Goals.Pick);
        Assert.Equal(1, vote.Result.TotalSources);
    }

    [Fact]
    public void SameDayAndFutureSnapshotsAreExcludedEvenWhenKickoffIsLater()
    {
        var boundary = Snapshot("A", "1");
        boundary.CapturedAtUtc = Cutoff(TargetDate);
        var later = Snapshot("B", "2");
        later.CapturedAtUtc = Cutoff(TargetDate).AddHours(1);
        var match = Fixture(TargetDate, boundary, later);
        match.KickoffUtc = Cutoff(TargetDate).AddHours(18);

        var vote = WeightedConsensusCalculator.Calculate(match, []);

        Assert.Null(vote.Result.Pick);
        Assert.Null(vote.Result.SupportShare);
        Assert.Equal(0, vote.Result.TotalSources);
    }

    [Fact]
    public void InvalidAndMissingMarketsAbstainIndependently()
    {
        var match = Fixture(TargetDate,
            Snapshot("A", "1X", "50%", "O4.5"),
            Snapshot("B", "-", "YES", null),
            Snapshot("C", " ", null, "U1.5"),
            Snapshot(" ", "1", "NO", "O2.5"));

        var prediction = WeightedConsensusCalculator.Calculate(match, []);

        Assert.Null(prediction.Result.Pick);
        Assert.Equal(0, prediction.Result.TotalSources);
        Assert.Equal("YES", prediction.Btts.Pick);
        Assert.Equal(1, prediction.Btts.TotalSources);
        Assert.Equal("U1.5", prediction.Goals.Pick);
    }

    [Fact]
    public void LatestMissingMarketDoesNotResurrectAnOlderPrediction()
    {
        var older = Snapshot("A", "1");
        older.CapturedAtUtc = Cutoff(TargetDate).AddHours(-2);
        var latest = Snapshot("A", null);

        Assert.Null(WeightedConsensusCalculator.Calculate(
            Fixture(TargetDate, older, latest), []).Result.Pick);
    }

    [Fact]
    public void TiesAbstainAndAreNotGraded()
    {
        var match = Fixture(TargetDate,
            Snapshot("A", "1", "YES", "O2.5"),
            Snapshot("B", "2", "NO", "U2.5"));
        var prediction = WeightedConsensusCalculator.Calculate(match, []);
        var grade = PredictionGrader.Grade(prediction.ToSnapshot(), Result(Cutoff(TargetDate)));

        Assert.Null(prediction.Result.Pick);
        Assert.Null(prediction.Btts.Pick);
        Assert.Null(prediction.Goals.Pick);
        Assert.Equal(2, prediction.Result.TotalSources);
        Assert.Null(prediction.Result.SupportShare);
        Assert.Null(grade.CorrectResult);
        Assert.Null(grade.CorrectBtts);
        Assert.Null(grade.CorrectGoals);
    }

    [Fact]
    public void WeightsExcludeTargetFutureAndLateCollectedResults()
    {
        var past = Historical(TargetDate.AddDays(-2), "A", "1", "YES", "O2.5");
        var target = Historical(TargetDate, "Target", "1");
        var future = Historical(TargetDate.AddDays(1), "Future", "1");
        var boundary = Historical(TargetDate.AddDays(-1), "Boundary", "1");
        boundary.Result!.CollectedAtUtc = Cutoff(TargetDate);
        var late = Historical(TargetDate.AddDays(-1), "Late", "1");
        late.Result!.CollectedAtUtc = Cutoff(TargetDate).AddSeconds(1);
        var noResult = Fixture(TargetDate.AddDays(-1), Snapshot("NoResult", "1"));

        var weights = WeightedConsensusCalculator.BuildWeights(
            [past, target, future, boundary, late, noResult], TargetDate);

        Assert.Equal(3, weights.Count);
        Assert.All(weights, x =>
        {
            Assert.Equal("A", x.Source);
            Assert.Equal(1, x.Total);
            Assert.Equal(1, x.Correct);
            Assert.Equal(6.0 / 11, x.Weight);
        });
    }

    [Fact]
    public void HistoryExcludesSameDaySnapshotsAndCountsLatestSourceOnly()
    {
        var day = TargetDate.AddDays(-2);
        var older = Snapshot("a", "2", "NO", "U2.5", day);
        older.CapturedAtUtc = Cutoff(day).AddHours(-2);
        var latest = Snapshot(" A ", "1", "YES", "O2.5", day);
        var afterMidnight = Snapshot("A", "2", "NO", "U2.5", day);
        afterMidnight.CapturedAtUtc = Cutoff(day);
        var match = Fixture(day, older, latest, afterMidnight);
        match.Result = Result(Cutoff(day).AddHours(20));

        var weights = WeightedConsensusCalculator.BuildWeights([match], TargetDate);

        Assert.Equal(3, weights.Count);
        Assert.All(weights, x =>
        {
            Assert.Equal(1, x.Total);
            Assert.Equal(1, x.Correct);
        });
    }

    [Fact]
    public void UnknownSourceAndMissingHistoricalMarketUseNeutralPrior()
    {
        var match = Fixture(TargetDate,
            Snapshot("KNOWN", "1", "YES"),
            Snapshot("Unknown", "2", "NO"));
        SourceMarketWeight[] weights = [new("Known", ConsensusMarket.Result, 9, 10)];
        var prediction = WeightedConsensusCalculator.Calculate(match, weights);

        Assert.Equal("1", prediction.Result.Pick);
        Assert.Null(prediction.Btts.Pick);
        Assert.Equal(0.7 / 1.2, prediction.Result.SupportShare!.Value, 12);
    }

    [Theory]
    [InlineData("O0.5", 0, false)]
    [InlineData("U0.5", 0, true)]
    [InlineData("O1.5", 1, false)]
    [InlineData("U1.5", 1, true)]
    [InlineData("O2.5", 3, true)]
    [InlineData("U2.5", 3, false)]
    [InlineData("O3.5", 3, false)]
    [InlineData("U3.5", 3, true)]
    public void HistoricalGoalsUseActualThreshold(string goals, int totalGoals, bool correct)
    {
        var match = Historical(TargetDate.AddDays(-2), "A", null, null, goals);
        match.Result!.TotalGoals = totalGoals;

        var weight = Assert.Single(WeightedConsensusCalculator.BuildWeights([match], TargetDate));

        Assert.Equal(ConsensusMarket.Goals, weight.Market);
        Assert.Equal(1, weight.Total);
        Assert.Equal(correct ? 1 : 0, weight.Correct);
    }

    [Fact]
    public void DifferentGoalsLinesAreSeparateVotesNotMerged()
    {
        var match = Fixture(TargetDate,
            Snapshot("A", null, null, "O2.5"),
            Snapshot("B", null, null, "O3.5"));

        Assert.Null(WeightedConsensusCalculator.Calculate(match, []).Goals.Pick);
    }

    [Fact]
    public void AddingFutureInformationCannotChangeEarlierWeightsOrPick()
    {
        var history = Historical(TargetDate.AddDays(-2), "A", "1");
        var fixture = Fixture(TargetDate, Snapshot("A", "1"), Snapshot("B", "2"));
        var before = WeightedConsensusCalculator.BuildWeights([history], TargetDate);
        var late = Snapshot("A", "2", date: history.MatchDate);
        late.CapturedAtUtc = Cutoff(TargetDate).AddDays(1);
        history.PredictionSnapshots.Add(late);
        var future = Historical(TargetDate.AddDays(1), "B", "2");
        var after = WeightedConsensusCalculator.BuildWeights([history, future], TargetDate);

        Assert.Equal(before.ToArray(), after.ToArray());
        Assert.Equal(
            WeightedConsensusCalculator.Calculate(fixture, before),
            WeightedConsensusCalculator.Calculate(fixture, after));
    }

    [Theory]
    [InlineData(1, 1, false, "SINGLE SOURCE")]
    [InlineData(2, 4, true, "CONFLICT")]
    [InlineData(4, 8, true, "CONFLICT")]
    [InlineData(4, 4, false, "VERY STRONG")]
    [InlineData(5, 5, false, "VERY STRONG")]
    [InlineData(8, 9, false, "VERY STRONG")]
    [InlineData(9, 9, false, "VERY STRONG")]
    [InlineData(11, 11, false, "VERY STRONG")]
    [InlineData(3, 3, false, "STRONG")]
    [InlineData(3, 4, false, "STRONG")]
    [InlineData(6, 9, false, "STRONG")]
    [InlineData(7, 9, false, "STRONG")]
    [InlineData(2, 2, false, "MEDIUM")]
    [InlineData(2, 3, false, "MEDIUM")]
    [InlineData(3, 5, false, "MEDIUM")]
    [InlineData(5, 9, false, "MEDIUM")]
    [InlineData(2, 4, false, "WEAK")]
    [InlineData(3, 6, false, "WEAK")]
    [InlineData(4, 9, false, "WEAK")]
    public void ReportConfidenceScalesAcrossAllSourceCounts(
        int matchingVotes,
        int totalVotes,
        bool conflict,
        string expected)
    {
        Assert.Equal(
            expected,
            ProductionConsensusAggregator.GetConfidenceLevel(
                matchingVotes,
                totalVotes,
                conflict));
    }

    [Theory]
    [InlineData("VERY STRONG", 68.6, "VERY STRONG")]
    [InlineData("VERY STRONG", 65.0, "VERY STRONG")]
    [InlineData("VERY STRONG", 64.0, "STRONG")]
    [InlineData("VERY STRONG", 58.0, "STRONG")]
    [InlineData("VERY STRONG", 56.1, "MEDIUM")]
    [InlineData("VERY STRONG", 49.9, "WEAK")]
    [InlineData("VERY STRONG", null, "MEDIUM")]
    [InlineData("STRONG", 70.0, "STRONG")]
    [InlineData("STRONG", 55.0, "MEDIUM")]
    [InlineData("MEDIUM", 40.0, "WEAK")]
    [InlineData("MEDIUM", 52.0, "MEDIUM")]
    [InlineData("WEAK", 90.0, "WEAK")]
    [InlineData("CONFLICT", 90.0, "CONFLICT")]
    [InlineData("SINGLE SOURCE", 30.0, "SINGLE SOURCE")]
    public void ConfidenceIsCappedByProbability(
        string level,
        double? probability,
        string expected)
    {
        Assert.Equal(
            expected,
            ProductionConsensusAggregator.CapConfidenceByProbability(level, probability));
    }

    private static Match Fixture(DateOnly date, params PredictionSnapshot[] predictions) => new()
    {
        MatchDate = date,
        HomeTeam = "Home",
        AwayTeam = "Away",
        PredictionSnapshots = predictions.ToList()
    };

    private static PredictionSnapshot Snapshot(
        string source,
        string? result,
        string? btts = null,
        string? goals = null,
        DateOnly? date = null) => new()
    {
        Source = source,
        PredictedResult = result,
        Btts = btts,
        Goals = goals,
        CapturedAtUtc = Cutoff(date ?? TargetDate).AddHours(-1)
    };

    private static Match Historical(
        DateOnly date,
        string source,
        string? result,
        string? btts = null,
        string? goals = null)
    {
        var match = Fixture(date, Snapshot(source, result, btts, goals, date));
        match.Result = Result(Cutoff(date).AddHours(20));
        return match;
    }

    private static MatchResult Result(DateTime collectedAt) => new()
    {
        Result = "1",
        HomeGoals = 2,
        AwayGoals = 1,
        Btts = true,
        TotalGoals = 3,
        CollectedAtUtc = collectedAt
    };

    private static DateTime Cutoff(DateOnly date) =>
        WeightedConsensusCalculator.GetCutoffUtc(date);
}
