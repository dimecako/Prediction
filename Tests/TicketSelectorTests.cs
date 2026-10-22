using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class TicketSelectorTests
{
    [Theory]
    [InlineData(1.2)]
    [InlineData(1.3)]
    [InlineData(1.399)]
    public void LowBaseOddsAllowHalfTimeFullTime(double odds)
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? odds : c.Pick == "1-1" ? 1.7 : 1.1, false);
        Assert.Equal("1-1", decision.Confirmed!.Candidate.Pick);
    }

    [Fact]
    public void MissingBaseOddsDoesNotBecomeUnconfirmedProposal()
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match), _ => null, false);
        Assert.Null(decision.Confirmed);
        Assert.Null(decision.Proposal);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidBaseOddsDoNotProduceSelections(double odds)
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? odds : 2.0, false);
        Assert.Null(decision.Confirmed);
        Assert.Null(decision.Proposal);
    }

    [Theory]
    [InlineData(1.3, "2-2")]
    [InlineData(1.5, "2 & 2-5 golovi")]
    public void AwayWinnerUsesSameOddsBands(double odds, string pick)
    {
        var match = Fixture("2");
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? odds : c.Pick == pick ? 1.7 : 1.1, false);
        Assert.Equal(pick, decision.Confirmed!.Candidate.Pick);
    }

    [Fact]
    public void HighBaseOddsAllowStandaloneOverWithReliableGoalsSignal()
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? 2.1 : c.Market == "Goals" ? 1.8 : 1.1, false);
        Assert.Equal("Goals", decision.Confirmed!.Candidate.Market);
        Assert.Equal("O2.5", decision.Confirmed.Candidate.Pick);
        Assert.Null(decision.Proposal);
    }

    [Theory]
    [InlineData("1", 1.7)]
    [InlineData("1", 1.999)]
    [InlineData("2", 2.0)]
    public void HighBaseOddsKeepFixEvenIfCombinationLooksBetter(string side, double odds)
    {
        var match = Fixture(side);
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? odds : 4, false);
        Assert.Equal("1X2", decision.Confirmed!.Candidate.Market);
        Assert.Equal(side, decision.Confirmed.Candidate.Pick);
        Assert.Null(decision.Proposal);
    }

    [Theory]
    [InlineData(1.40)]
    [InlineData(1.45)]
    [InlineData(1.699)]
    public void MediumBaseOddsChooseConfirmedAlternativeAtExactMinimum(double odds)
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match), c =>
            c.Market == "1X2" ? odds : c.Pick == "1 & 2-5 golovi" ? 1.70 : null, false);
        Assert.Equal("1 & 2-5 golovi", decision.Confirmed!.Candidate.Pick);
        Assert.Null(decision.Proposal);
    }

    [Fact]
    public void KnownLowCombinationOddsAreNotPretendedToBeUnknown()
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? 1.4 : 1.699, false);
        Assert.Null(decision.Confirmed);
        Assert.Null(decision.Proposal);
    }

    [Fact]
    public void UnknownCombinationIsSeparateFromConfirmedTicket()
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? 1.4 : null, false);
        Assert.Null(decision.Confirmed);
        Assert.NotNull(decision.Proposal);
        Assert.Null(decision.Proposal.Odds);
        Assert.NotEqual("1X2", decision.Proposal.Candidate.Market);
    }

    [Theory]
    [InlineData(54, 100)]
    [InlineData(55, 99)]
    public void WinRequiresBothAccuracyAndSampleThreshold(int correct, int total)
    {
        var match = Fixture();
        var tally = Calibrate(match, winCorrect: correct, winTotal: total);
        var decision = TicketSelector.Select(match, tally, _ => 1.7, false);
        Assert.Null(decision.Confirmed);
    }

    [Fact]
    public void ExactWinAccuracyAndSampleThresholdPass()
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match, winCorrect: 55), _ => 1.7, false);
        Assert.Equal(0.55, decision.Confirmed!.Accuracy);
        Assert.Equal(100, decision.Confirmed.Sample);
    }

    [Theory]
    [InlineData(27, 50, 1.8, false)]
    [InlineData(25, 49, 2.2, false)]
    [InlineData(25, 50, 2.2, true)]
    [InlineData(28, 50, 1.8, true)]
    public void EntireCombinationMustMeetAccuracyOrExpectedReturnThreshold(int correct, int total, double odds, bool accepted)
    {
        var match = Fixture();
        var tally = Calibrate(match, comboCorrect: correct, comboTotal: total);
        var decision = TicketSelector.Select(match, tally,
            c => c.Market == "1X2" ? 1.4 : c.Pick == "1 & 2-5 golovi" ? odds : 1.1, false);
        Assert.Equal(accepted, decision.Confirmed != null);
    }

    [Theory]
    [InlineData(59, 100, false)]
    [InlineData(60, 99, false)]
    [InlineData(60, 100, true)]
    public void GoalsComponentRequiresSixtyPercentAndHundredSamples(int correct, int total, bool accepted)
    {
        var match = Fixture();
        var tally = Calibrate(match, signalCorrect: correct, signalTotal: total);
        var decision = TicketSelector.Select(match, tally,
            c => c.Market == "1X2" ? 1.4 : c.Pick == "1 & 2-6 golovi" ? 1.8 : 1.1, false);
        Assert.Equal(accepted, decision.Confirmed != null);
    }

    [Fact]
    public void FixDoesNotRequireBttsOrGoalsThreshold()
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match, signalCorrect: 10), _ => 1.7, false);
        Assert.Equal("1X2", decision.Confirmed!.Candidate.Market);
    }

    [Fact]
    public void HalfTimeFullTimeWithoutGoalsDoesNotRequireBinarySignals()
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match, signalCorrect: 10),
            c => c.Market == "1X2" ? 1.3 : c.Pick == "1-1" ? 1.8 : 1.1, false);
        Assert.Equal("1-1", decision.Confirmed!.Candidate.Pick);
    }

    [Fact]
    public void NoBttsAndUnderAreEvidenceNotAllowedPicks()
    {
        var match = Fixture(btts: "NO", goals: "U2.5");
        var tally = Calibrate(match);
        Assert.Equal("NO", TicketMarkets.BinarySignal(match, "BTTS")!.Pick);
        Assert.Equal("U2.5", TicketMarkets.BinarySignal(match, "Goals")!.Pick);
        var decision = TicketSelector.Select(match, tally, c =>
            c.Market == "1X2" ? 1.4 : c.Pick == "1 & 2-5 golovi" ? 1.8 : 1.1, false);
        Assert.Equal("1 & 2-5 golovi", decision.Confirmed!.Candidate.Pick);
        Assert.DoesNotContain(TicketMarkets.BuildCandidates(match), c => c.Pick is "NO" or "U2.5");
    }

    [Fact]
    public void BttsOverCombinationNeedsBothReliableSignals()
    {
        var match = Fixture();
        var tally = Calibrate(match);
        var btts = TicketMarkets.BinarySignal(match, "BTTS")!;
        for (int i = 0; i < 100; i++)
            tally.Add(btts.Key, false);
        var decision = TicketSelector.Select(match, tally, c =>
            c.Market == "1X2" ? 2.1 : c.Pick == "BTTS & 3+" ? 1.8 : 1.1, false);
        Assert.Null(decision.Confirmed);
        Assert.Null(decision.Proposal);
    }

    [Fact]
    public void TiedBinaryVotesDoNotCountAsReliableSignal()
    {
        var match = Fixture();
        match.PredictionSnapshots.Add(new()
        {
            Source = "Other", PredictedResult = "1", Confidence = 0.7, Btts = "NO", Goals = "U2.5"
        });
        Assert.Null(TicketMarkets.BinarySignal(match, "BTTS"));
        Assert.Null(TicketMarkets.BinarySignal(match, "Goals"));
        var decision = TicketSelector.Select(match, Calibrate(match), c =>
            c.Market == "1X2" ? 2.1 : c.Pick == "BTTS & 3+" ? 1.8 : 1.1, false);
        Assert.Null(decision.Confirmed);
    }

    [Theory]
    [InlineData("BTTS", "YES")]
    public void StandaloneAlternativeRequiresReliableRelevantSignal(string market, string pick)
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match), c =>
            c.Market == "1X2" ? 2.1 : c.Market == market && c.Pick == pick ? 1.65 : 1.1, false);
        Assert.Equal(market, decision.Confirmed!.Candidate.Market);
        Assert.Equal(pick, decision.Confirmed.Candidate.Pick);
    }

    [Fact]
    public void SafetyRankingDoesNotRelaxMinimumOdds()
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match), c =>
            c.Market == "1X2" ? 1.4 : c.Pick == "1 & 2-5 golovi" ? 1.7 : 1.699, true);
        Assert.Equal("1 & 2-5 golovi", decision.Confirmed!.Candidate.Pick);
    }

    [Fact]
    public void EmptyHistoryProducesNeitherTicketNorProposal()
    {
        var decision = TicketSelector.Select(Fixture(), new Tally(), _ => 1.8, false);
        Assert.Null(decision.Confirmed);
        Assert.Null(decision.Proposal);
    }

    [Theory]
    [InlineData(1.399, "1-1", true)]
    [InlineData(1.399, "1-1 & 2+", true)]
    [InlineData(1.399, "1 & 2-5 golovi", true)]
    [InlineData(1.4, "1-1", false)]
    [InlineData(1.4, "1 & 2-5 golovi", true)]
    [InlineData(1.699, "1 & 2-6 golovi", true)]
    [InlineData(1.699, "BTTS & 3+", false)]
    [InlineData(2.001, "1 & 2-5 golovi", false)]
    [InlineData(2.001, "O2.5", true)]
    [InlineData(2.001, "BTTS & 3+", true)]
    [InlineData(2.001, "1-2 prvo & 1-2 vtoro", true)]
    [InlineData(2.001, "1-3 prvo & 1-3 vtoro", true)]
    [InlineData(2.001, "1-3 prvo & 3+", true)]
    public void OddsBandsOnlyAllowRequestedMarkets(double baseOdds, string pick, bool accepted)
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? baseOdds : c.Pick == pick ? 1.7 : 1.1, false);
        Assert.Equal(accepted, decision.Confirmed != null);
        Assert.Null(decision.Proposal);
        if (accepted)
            Assert.Equal(pick, decision.Confirmed!.Candidate.Pick);
    }

    [Theory]
    [InlineData("YES", 1.65, true)]
    [InlineData("YES", 1.649, false)]
    [InlineData("BTTS & 3+", 1.70, true)]
    [InlineData("BTTS & 3+", 1.699, false)]
    [InlineData("1-2 prvo & 1-2 vtoro", 1.70, true)]
    [InlineData("1-2 prvo & 1-2 vtoro", 1.699, false)]
    public void HighOddsAlternativesUseTheirOwnMinimum(string pick, double odds, bool accepted)
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? 2.1 : c.Pick == pick ? odds : 1.1, false);
        Assert.Equal(accepted, decision.Confirmed != null);
        Assert.Null(decision.Proposal);
    }

    [Theory]
    [InlineData(27, 50, 1.8, 60, false)]
    [InlineData(28, 49, 1.8, 60, false)]
    [InlineData(28, 50, 1.8, 59, false)]
    [InlineData(28, 50, 1.8, 60, true)]
    [InlineData(24, 50, 2.2, 60, true)]
    public void HalfGoalsNeedReliableOverSignalAndHistoryOrExpectedReturn(int correct, int total, double odds, int signalCorrect, bool accepted)
    {
        var match = Fixture(btts: "NO", goals: "O2.5");
        var decision = TicketSelector.Select(match,
            Calibrate(match, winCorrect: 0, comboCorrect: correct, comboTotal: total, signalCorrect: signalCorrect),
            c => c.Market == "1X2" ? 2.1 : c.Pick == "1-3 prvo & 3+" ? odds : 1.1, false);
        Assert.Equal(accepted, decision.Confirmed != null);
    }

    [Fact]
    public void UnknownHalfGoalsOddsRemainSeparateProposal()
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? 2.1 : c.Pick == "1-3 prvo & 3+" ? null : 1.1, false);
        Assert.Null(decision.Confirmed);
        Assert.Equal("HalfGoals", decision.Proposal!.Candidate.Market);
    }

    [Fact]
    public void HighOddsBttsDoesNotRequireReliableWinner()
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match, winCorrect: 0),
            c => c.Market == "1X2" ? 2.1 : c.Market == "BTTS" ? 1.65 : 1.1, false);
        Assert.Equal("BTTS", decision.Confirmed!.Candidate.Market);
    }

    [Theory]
    [InlineData(false, "1 & 2-6 golovi")]
    [InlineData(true, "1 & 2-5 golovi")]
    public void SafetyFlagPreservesRankingWithinAllowedBand(bool safe, string expected)
    {
        var match = Fixture();
        var tally = Calibrate(match);
        string key = TicketMarkets.BuildCandidates(match).Single(c => c.Pick == "1 & 2-5 golovi").Key;
        for (int i = 0; i < 100; i++)
            tally.Add(key, true);
        var decision = TicketSelector.Select(match, tally, c => c.Market == "1X2" ? 1.4 :
            c.Pick == "1 & 2-5 golovi" ? 1.7 : c.Pick == "1 & 2-6 golovi" ? 3.0 : 1.1, safe);
        Assert.Equal(expected, decision.Confirmed!.Candidate.Pick);
    }

    [Fact]
    public void NoBttsAndUnderAreGradedCorrectlyForEvidence()
    {
        var result = new MatchResult { HomeGoals = 2, AwayGoals = 0 };
        Assert.True(TicketMarkets.IsCorrect(new("BTTS", "NO", "", ""), result));
        Assert.True(TicketMarkets.IsCorrect(new("Goals", "U2.5", "", ""), result));
        result.AwayGoals = 1;
        Assert.False(TicketMarkets.IsCorrect(new("BTTS", "NO", "", ""), result));
        Assert.False(TicketMarkets.IsCorrect(new("Goals", "U2.5", "", ""), result));
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.75)]
    [InlineData(2.35)]
    public void DedicatedFirstHalf1To3And3PlusAllowsAnyBaseOddsWithReliableOverSignal(double baseOdds)
    {
        var match = Fixture();
        var decision = TicketSelector.SelectFirstHalf1To3And3Plus(match, Calibrate(match),
            c => c.Market == "1X2" ? baseOdds : c.Pick == "1-3 prvo & 3+" ? 1.85 : null);
        Assert.NotNull(decision.Confirmed);
        Assert.Equal("HalfGoals", decision.Confirmed.Candidate.Market);
        Assert.Equal("1-3 prvo & 3+", decision.Confirmed.Candidate.Pick);
        Assert.Equal(baseOdds, decision.BaseOdds);
    }

    [Fact]
    public void DedicatedFirstHalf1To3And3PlusRejectsMissingOrWeakOverSignal()
    {
        var underMatch = Fixture(goals: "U2.5");
        Assert.Null(TicketSelector.SelectFirstHalf1To3And3Plus(underMatch, Calibrate(underMatch), _ => 1.9).Confirmed);

        var weakOverMatch = Fixture(goals: "O2.5");
        var weakTally = Calibrate(weakOverMatch, signalCorrect: 59, signalTotal: 100);
        Assert.Null(TicketSelector.SelectFirstHalf1To3And3Plus(weakOverMatch, weakTally, _ => 1.9).Confirmed);
    }

    private static Match Fixture(string side = "1", string btts = "YES", string goals = "O2.5") => new()
    {
        PredictionSnapshots =
        [
            new() { Source = "Test", PredictedResult = side, Confidence = 0.7, Btts = btts, Goals = goals }
        ]
    };

    private static Tally Calibrate(Match match, int winCorrect = 70, int winTotal = 100,
        int comboCorrect = 60, int comboTotal = 100, int signalCorrect = 60, int signalTotal = 100)
    {
        var tally = new Tally();
        foreach (var c in TicketMarkets.BuildCalibrationCandidates(match))
        {
            bool signal = c.Key.StartsWith("SIGNAL|", StringComparison.Ordinal);
            int correct = signal ? signalCorrect : c.Market == "1X2" ? winCorrect : comboCorrect;
            int total = signal ? signalTotal : c.Market == "1X2" ? winTotal : comboTotal;
            for (int i = 0; i < total; i++)
                tally.Add(c.Key, i < correct);
        }
        return tally;
    }
}
