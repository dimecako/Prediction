using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class TicketSelectorTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidBaseOddsDoNotProduceSelections(double odds)
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? odds : 2, false);
        Assert.Null(decision.Confirmed);
        Assert.Null(decision.Proposal);
    }

    [Fact]
    public void EmptyHistoryOrMissingPricesNeverProducesTicket()
    {
        var match = Fixture();
        Assert.Null(TicketSelector.Select(match, new(), _ => 1.8, false).Confirmed);
        Assert.Null(TicketSelector.Select(match, Calibrate(match), _ => null, false).Confirmed);
    }

    [Theory]
    [InlineData(54, 100, 2.0, false)]
    [InlineData(70, 99, 1.8, false)]
    [InlineData(55, 100, 1.8, false)]
    [InlineData(60, 100, 1.8, false)]
    [InlineData(70, 100, 1.8, true)]
    public void FixRequiresSampleAccuracyValueAndConservativeReturn(int correct, int total, double odds, bool accepted)
    {
        var match = Fixture();
        var tally = Calibrate(match, correct, total);
        var decision = TicketSelector.Select(match, tally, _ => odds, true);
        Assert.Equal(accepted, decision.Confirmed != null);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OneOrTwoSourcesDoNotQualifyEvenWithPerfectHistory(int sources)
    {
        var match = Fixture(sources: sources);
        Assert.Null(TicketSelector.Select(match, Calibrate(match), _ => 1.8, true).Confirmed);
    }

    [Theory]
    [InlineData("1-1")]
    [InlineData("1-1 & 2+")]
    [InlineData("2-2")]
    [InlineData("1 & 1+ prvo")]
    [InlineData("1 & 1+ prvo & 1+ vtoro")]
    [InlineData("1-3 prvo & 3+")]
    [InlineData("1-3 prvo & 1-3 vtoro")]
    public void NoHalfTimeSelectionCanBeGenerated(string pick)
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? 1.3 : c.Pick == pick ? 2 : null, false);
        Assert.Null(decision.Confirmed);
        Assert.Null(decision.Proposal);
    }

    [Fact]
    public void RetiredDedicatedSelectorNeverFetchesOdds()
    {
        var match = Fixture();
        var decision = TicketSelector.SelectFirstHalf1To3And3Plus(match, Calibrate(match),
            _ => throw new InvalidOperationException("Retired selector must not fetch odds"));
        Assert.Null(decision.Confirmed);
        Assert.Contains("Retired", decision.Reason);
    }

    [Theory]
    [InlineData(27, 50, 2.5, true)]
    [InlineData(40, 49, 1.8, false)]
    [InlineData(28, 50, 1.8, true)]
    [InlineData(40, 50, 1.8, true)]
    public void FtBaselineRestoresPreviousSampleAndAccuracyOrReturnEligibility(
        int correct, int total, double odds, bool accepted)
    {
        var match = Fixture();
        var tally = Calibrate(match, comboCorrect: correct, comboTotal: total);
        var decision = TicketSelector.Select(match, tally,
            c => c.Market == "1X2" ? 1.4 : c.Pick == "1 & 2-5 golovi" ? odds : null, false);
        Assert.Equal(accepted, decision.Confirmed != null);
    }

    [Theory]
    [InlineData("YES", "BTTS")]
    [InlineData("O2.5", "Goals")]
    public void StandaloneSignalsNeedExactSelectedEventHistory(string pick, string market)
    {
        var match = Fixture();
        var tally = Calibrate(match);
        var decision = TicketSelector.Select(match, tally,
            c => c.Market == "1X2" ? 2.2 : c.Pick == pick ? 1.8 : null, true);
        Assert.Equal(market, decision.Confirmed!.Candidate.Market);
        var key = decision.Confirmed.Candidate.Key;
        var weak = Calibrate(match);
        for (int i = 0; i < 300; i++)
            weak.Add(key, false);
        Assert.Null(TicketSelector.Select(match, weak,
            c => c.Market == "1X2" ? 2.2 : c.Pick == pick ? 1.8 : null, true).Confirmed);
    }

    [Fact]
    public void StrongBinarySignalDoesNotReplaceMissingExactBttsHistory()
    {
        var match = Fixture();
        var tally = new Tally();
        var signal = TicketMarkets.BinarySignal(match, "BTTS")!;
        for (int i = 0; i < 200; i++) tally.Add(signal.Key, true);
        Assert.Null(TicketSelector.Select(match, tally,
            c => c.Market == "1X2" ? 2.2 : c.Market == "BTTS" ? 1.8 : null, false).Confirmed);
    }

    [Fact]
    public void TeamGoalsRequireHistoryForTeamGoalsNotOver25()
    {
        var match = Fixture();
        var tally = Calibrate(match);
        double? Odds(TicketCandidate c) => c.Market == "1X2" ? 1.4 :
            c.Pick == "1 & T1 2+" ? 1.8 : null;
        Assert.Null(TicketSelector.Select(match, tally, Odds, true, true).Confirmed);
        var key = TicketMarkets.BuildCandidates(match, true).Single(c => c.Pick == "1 & T1 2+").Key;
        for (int i = 0; i < 100; i++) tally.Add(TicketQualityPolicy.FtBaselinePrefix + key, i < 80);
        Assert.Equal("1 & T1 2+", TicketSelector.Select(match, tally, Odds, true, true).Confirmed!.Candidate.Pick);
    }

    [Theory]
    [InlineData("1", "1 & 2-5 golovi", 1.3)]
    [InlineData("1", "1 & 2-6 golovi", 1.8)]
    [InlineData("1", "1 & T1 2+", 2.4)]
    [InlineData("2", "2 & 2-5 golovi", 1.3)]
    [InlineData("2", "2 & 2-6 golovi", 1.8)]
    [InlineData("2", "2 & T2 2+", 2.4)]
    public void FtOnlyCommandCanSelectEveryAllowedCombinationAcrossBasePriceBands(
        string side, string pick, double baseOdds)
    {
        var match = Fixture(side);
        var tally = Calibrate(match);
        var candidate = TicketMarkets.BuildCandidates(match, true).Single(c => c.Pick == pick);
        for (int i = 0; i < 100; i++)
            tally.Add(TicketQualityPolicy.FtBaselinePrefix + candidate.Key, i < 80);
        var decision = TicketSelector.Select(match, tally,
            c => c.Market == "1X2" ? baseOdds : c.Pick == pick ? 1.8 : null,
            true, includeTeamGoals: true, winnerGoalsOnly: true);
        Assert.Equal(pick, decision.Confirmed!.Candidate.Pick);
        Assert.StartsWith(TicketQualityPolicy.FtBaselinePrefix, decision.Confirmed.Candidate.Key);
        Assert.Contains("previous eligibility baseline", TicketQualityPolicy.Evidence(decision.Confirmed));
    }

    [Theory]
    [InlineData("1X2", "1")]
    [InlineData("BTTS", "YES")]
    [InlineData("Goals", "O2.5")]
    [InlineData("Combo", "BTTS & 3+")]
    [InlineData("Combo", "1-1")]
    [InlineData("HalfGoals", "1-3 prvo & 3+")]
    public void FtOnlyNeverFallsBackToOtherMarkets(string market, string pick)
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? 1.8 : c.Market == market && c.Pick == pick ? 2 : null,
            true, includeTeamGoals: true, winnerGoalsOnly: true);
        Assert.Null(decision.Confirmed);
        Assert.Null(decision.Proposal);
    }

    [Fact]
    public void RestoredFtBaselineDoesNotRequireNewThreeSourceOrConservativeReturnGates()
    {
        var match = Fixture(sources: 2);
        var tally = Calibrate(match, comboCorrect: 28, comboTotal: 50);
        var decision = TicketSelector.Select(match, tally,
            c => c.Market == "1X2" ? 1.4 : c.Pick == "1 & 2-5 golovi" ? 1.8 : null, true);
        var score = Assert.IsType<ScoredTicketCandidate>(decision.Confirmed);
        Assert.True(TicketQualityPolicy.PassesFtBaseline(score));
        Assert.False(TicketQualityPolicy.Passes(score, 1.7));
        Assert.False(TicketQualityPolicy.HasCoverage(match, score.Candidate));
    }

    [Fact]
    public void FtOnlyStillRequiresPreviousWinnerAndUsedGoalSignalHistory()
    {
        var match = Fixture();
        var tally = new Tally();
        var candidate = TicketMarkets.BuildCandidates(match).Single(c => c.Pick == "1 & 2-5 golovi");
        for (int i = 0; i < 100; i++)
            tally.Add(TicketQualityPolicy.FtBaselinePrefix + candidate.Key, true);
        Assert.Null(TicketSelector.Select(match, tally,
            c => c.Market == "1X2" ? 2.4 : 1.8, true, true, true).Confirmed);
        for (int i = 0; i < 100; i++)
            tally.Add(TicketQualityPolicy.FtBaselinePrefix + "1X2|1|70+", i < 80);
        Assert.Null(TicketSelector.Select(match, tally,
            c => c.Market == "1X2" ? 2.4 : 1.8, true, true, true).Confirmed);
    }

    internal static Match Fixture(string side = "1", int sources = 3) => new()
    {
        PredictionSnapshots = Enumerable.Range(0, sources).Select(i => new PredictionSnapshot
        {
            Source = $"Source{i}", PredictedResult = side, Confidence = .75, Btts = "YES", Goals = "O2.5"
        }).ToList()
    };

    internal static Tally Calibrate(Match match, int correct = 80, int total = 100,
        int comboCorrect = 80, int comboTotal = 100)
    {
        var tally = new Tally();
        foreach (var candidate in TicketMarkets.BuildCalibrationCandidates(match, true).DistinctBy(c => c.Key))
        {
            int n = candidate.Market == "Combo" ? comboTotal : total;
            int wins = candidate.Market == "Combo" ? comboCorrect : correct;
            if (candidate.Pick.Contains("T1") || candidate.Pick.Contains("T2")) continue;
            for (int i = 0; i < n; i++)
            {
                tally.Add(candidate.Key, i < wins);
                tally.Add(TicketQualityPolicy.FtBaselinePrefix + candidate.Key, i < wins);
            }
        }
        return tally;
    }
}
