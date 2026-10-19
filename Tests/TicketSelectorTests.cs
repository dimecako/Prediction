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
    public void BaseOddsBelowMinimumRejectEvenStrongCombinations(double odds)
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match), _ => odds, false);
        Assert.Null(decision.Confirmed);
        Assert.Null(decision.Proposal);
        Assert.Contains("1.40", decision.Reason);
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
    [InlineData("1", 1.7)]
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
            c.Market == "1X2" ? odds : c.Pick == "1 & 2-5 golovi" ? 1.50 : null, false);
        Assert.Equal("1 & 2-5 golovi", decision.Confirmed!.Candidate.Pick);
        Assert.Null(decision.Proposal);
    }

    [Fact]
    public void KnownLowCombinationOddsAreNotPretendedToBeUnknown()
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match),
            c => c.Market == "1X2" ? 1.4 : 1.49, false);
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
    [InlineData(54, 100, false)]
    [InlineData(55, 99, false)]
    [InlineData(55, 100, true)]
    public void EntireCombinationMustMeetItsOwnThreshold(int correct, int total, bool accepted)
    {
        var match = Fixture();
        var tally = Calibrate(match, comboCorrect: correct, comboTotal: total);
        var decision = TicketSelector.Select(match, tally,
            c => c.Market == "1X2" ? 1.4 : c.Pick == "1 & 2-5 golovi" ? 1.8 : 1.1, false);
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
            c => c.Market == "1X2" ? 1.4 : c.Pick == "1-1" ? 1.8 : 1.1, false);
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
            c.Market == "1X2" ? 1.4 : c.Pick == "BTTS & 3+" ? 1.8 : 1.1, false);
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
            c.Market == "1X2" ? 1.4 : c.Pick == "BTTS & 3+" ? 1.8 : 1.1, false);
        Assert.Null(decision.Confirmed);
    }

    [Theory]
    [InlineData("BTTS", "YES")]
    [InlineData("Goals", "O2.5")]
    public void StandaloneAlternativeRequiresReliableRelevantSignal(string market, string pick)
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match), c =>
            c.Market == "1X2" ? 1.4 : c.Market == market && c.Pick == pick ? 1.5 : 1.1, false);
        Assert.Equal(market, decision.Confirmed!.Candidate.Market);
        Assert.Equal(pick, decision.Confirmed.Candidate.Pick);
    }

    [Fact]
    public void SafetyRankingDoesNotRelaxMinimumOdds()
    {
        var match = Fixture();
        var decision = TicketSelector.Select(match, Calibrate(match), c =>
            c.Market == "1X2" ? 1.4 : c.Pick == "1 & 2-5 golovi" ? 1.5 : 1.49, true);
        Assert.Equal("1 & 2-5 golovi", decision.Confirmed!.Candidate.Pick);
    }

    [Fact]
    public void EmptyHistoryProducesNeitherTicketNorProposal()
    {
        var decision = TicketSelector.Select(Fixture(), new Tally(), _ => 1.8, false);
        Assert.Null(decision.Confirmed);
        Assert.Null(decision.Proposal);
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
