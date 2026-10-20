using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class TicketMarketsTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public void CandidatesContainOnlyRequestedMarkets(string side)
    {
        var match = Fixture(side, "YES", "O2.5");
        var candidates = TicketMarkets.BuildCandidates(match).ToList();
        Assert.Equal(13, candidates.Count);
        Assert.Equal(new[]
        {
            $"fix {side}", $"{side} & 1+ prvo", $"{side} & 1+ prvo & 1+ vtoro",
            $"{side} & 2-5 golovi", $"{side} & 2-6 golovi",
            $"{side}-{side}", $"{side}-{side} & 2+", "BTTS", "BTTS & 3+", "3+",
            "1-2 prvo & 1-2 vtoro", "1-3 prvo & 1-3 vtoro", "1-3 prvo & 3+"
        }, candidates.Select(x => x.DisplayPick));
        Assert.DoesNotContain(candidates, x => x.Market == "DC");
        Assert.Equal(candidates.Count, candidates.Select(x => x.Key).Distinct().Count());
    }

    [Fact]
    public void DrawNoBttsAndUnderOnlyGenerateIndependentHalfGoals()
    {
        Assert.All(TicketMarkets.BuildCandidates(Fixture("X", "NO", "U2.5")),
            c => Assert.Equal("HalfGoals", c.Market));
    }

    [Fact]
    public void HomeAndAwayHistoryUseDifferentKeys()
    {
        var home = TicketMarkets.BuildCandidates(Fixture("1", "NO", "U2.5")).First();
        var away = TicketMarkets.BuildCandidates(Fixture("2", "NO", "U2.5")).First();
        Assert.NotEqual(home.Key, away.Key);
    }

    [Theory]
    [InlineData("1 & 1+ prvo", 2, 0, 0, 0, false)]
    [InlineData("1 & 1+ prvo", 2, 1, 0, 1, true)]
    [InlineData("2 & 1+ prvo", 1, 2, 1, 0, true)]
    [InlineData("1 & 1+ prvo & 1+ vtoro", 2, 0, 2, 0, false)]
    [InlineData("1 & 1+ prvo & 1+ vtoro", 2, 0, 1, 0, true)]
    [InlineData("2 & 1+ prvo & 1+ vtoro", 0, 2, 0, 1, true)]
    [InlineData("1-1", 2, 1, 0, 1, false)]
    [InlineData("1-1", 2, 1, 1, 1, false)]
    [InlineData("1-1", 2, 1, 1, 0, true)]
    [InlineData("2-2", 1, 2, 0, 1, true)]
    [InlineData("1-1 & 2+", 1, 0, 1, 0, false)]
    [InlineData("1-1 & 2+", 2, 0, 1, 0, true)]
    [InlineData("2-2 & 2+", 0, 1, 0, 1, false)]
    [InlineData("2-2 & 2+", 0, 2, 0, 1, true)]
    public void HalfTimeCombinationsGradeWholeBet(
        string pick, int home, int away, int htHome, int htAway, bool expected)
    {
        Assert.Equal(expected, TicketMarkets.IsCorrect(Combo(pick), Result(home, away, htHome, htAway)));
    }

    [Theory]
    [InlineData("1 & 1+ prvo")]
    [InlineData("2 & 1+ prvo & 1+ vtoro")]
    [InlineData("1-1")]
    [InlineData("2-2 & 2+")]
    public void MissingHalfTimeIsNotGraded(string pick)
    {
        Assert.Null(TicketMarkets.IsCorrect(Combo(pick), Result(2, 1)));
        Assert.Null(TicketMarkets.IsCorrect(Combo(pick), Result(2, 1, 1)));
    }

    [Theory]
    [InlineData("1 & 2-5 golovi", 1, 0, false)]
    [InlineData("1 & 2-5 golovi", 2, 0, true)]
    [InlineData("1 & 2-5 golovi", 4, 1, true)]
    [InlineData("1 & 2-5 golovi", 5, 1, false)]
    [InlineData("1 & 2-6 golovi", 5, 1, true)]
    [InlineData("1 & 2-6 golovi", 6, 1, false)]
    [InlineData("2 & 2-5 golovi", 0, 2, true)]
    [InlineData("2 & 2-5 golovi", 1, 4, true)]
    [InlineData("2 & 2-6 golovi", 1, 5, true)]
    [InlineData("2 & 2-6 golovi", 1, 6, false)]
    [InlineData("1 & 2-5 golovi", 1, 1, false)]
    [InlineData("BTTS & 3+", 1, 1, false)]
    [InlineData("BTTS & 3+", 2, 1, true)]
    [InlineData("BTTS & 3+", 3, 0, false)]
    public void FullTimeCombinationsGradeInclusiveBoundaries(string pick, int home, int away, bool expected)
    {
        Assert.Equal(expected, TicketMarkets.IsCorrect(Combo(pick), Result(home, away)));
    }

    [Fact]
    public void InvalidHalfTimeFailsExplicitly()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TicketMarkets.IsCorrect(Combo("1-1"), Result(1, 0, 2, 0)));
    }

    [Fact]
    public void UnknownMarketsFailExplicitly()
    {
        Assert.Throws<ArgumentException>(() =>
            TicketMarkets.IsCorrect(new("DC", "1X", "", ""), Result(1, 0)));
    }

    [Fact]
    public void CompositeOddsAreNotManufacturedFromComponentOdds()
    {
        var offer = new BookmakerMatch("Home", "Away", DateTime.UtcNow,
            new Dictionary<string, double> { ["1"] = 2, ["24"] = 1.8, ["272"] = 1.7 });
        Assert.Null(MaxbetOddsProvider.GetOdds(offer, "Combo", "1 & 2-5 golovi"));
        Assert.Null(MaxbetOddsProvider.GetOdds(offer, "Combo", "BTTS & 3+"));
        Assert.Equal(2, MaxbetOddsProvider.GetOdds(offer, "1X2", "1"));
    }

    [Theory]
    [InlineData("1-2 prvo & 1-2 vtoro", 2, 0, 1, 0, true)]
    [InlineData("1-2 prvo & 1-2 vtoro", 2, 2, 1, 1, true)]
    [InlineData("1-2 prvo & 1-2 vtoro", 3, 2, 1, 1, false)]
    [InlineData("1-2 prvo & 1-2 vtoro", 2, 0, 0, 0, false)]
    [InlineData("1-2 prvo & 1-2 vtoro", 2, 0, 2, 0, false)]
    [InlineData("1-3 prvo & 1-3 vtoro", 3, 3, 2, 1, true)]
    [InlineData("1-3 prvo & 1-3 vtoro", 4, 3, 2, 1, false)]
    [InlineData("1-3 prvo & 1-3 vtoro", 4, 1, 3, 1, false)]
    [InlineData("1-3 prvo & 1-3 vtoro", 1, 0, 0, 0, false)]
    [InlineData("1-3 prvo & 1-3 vtoro", 2, 0, 2, 0, false)]
    [InlineData("1-3 prvo & 3+", 2, 1, 1, 0, true)]
    [InlineData("1-3 prvo & 3+", 3, 0, 3, 0, true)]
    [InlineData("1-3 prvo & 3+", 1, 1, 1, 0, false)]
    [InlineData("1-3 prvo & 3+", 3, 1, 3, 1, false)]
    [InlineData("1-3 prvo & 3+", 2, 1, 0, 0, false)]
    public void HalfGoalsGradeInclusiveRanges(
        string pick, int home, int away, int htHome, int htAway, bool expected)
    {
        Assert.Equal(expected, TicketMarkets.IsCorrect(new("HalfGoals", pick, "", ""),
            Result(home, away, htHome, htAway)));
    }

    [Fact]
    public void HalfGoalsRequireCompleteValidHalfTimeScores()
    {
        foreach (string pick in TicketMarkets.HalfGoalPicks)
        {
            var candidate = new TicketCandidate("HalfGoals", pick, "", "");
            Assert.Null(TicketMarkets.IsCorrect(candidate, Result(2, 1)));
            Assert.Null(TicketMarkets.IsCorrect(candidate, Result(2, 1, 1)));
            Assert.Throws<InvalidOperationException>(() =>
                TicketMarkets.IsCorrect(candidate, Result(2, 1, 3, 0)));
        }
    }

    [Fact]
    public void StandaloneHalfGoalCalibrationDoesNotDependOnPredictedWinner()
    {
        var home = TicketMarkets.BuildCalibrationCandidates(Fixture("1", "YES", "O2.5"))
            .Where(c => c.Market == "HalfGoals").Select(c => c.Key).ToList();
        var away = TicketMarkets.BuildCalibrationCandidates(Fixture("2", "NO", "U2.5"))
            .Where(c => c.Market == "HalfGoals").Select(c => c.Key).ToList();
        Assert.Equal(3, home.Count);
        Assert.Equal(home, away);
    }

    [Theory]
    [InlineData("Combo", "1 & 1+ prvo", "50247")]
    [InlineData("Combo", "2 & 1+ prvo", "50250")]
    [InlineData("Combo", "1 & 1+ prvo & 1+ vtoro", "50726")]
    [InlineData("Combo", "2 & 1+ prvo & 1+ vtoro", "50730")]
    [InlineData("Combo", "1 & 2-5 golovi", "50723")]
    [InlineData("Combo", "2 & 2-5 golovi", "50727")]
    [InlineData("Combo", "1 & 2-6 golovi", "51784")]
    [InlineData("Combo", "2 & 2-6 golovi", "51785")]
    [InlineData("Combo", "1-1", "10")]
    [InlineData("Combo", "2-2", "18")]
    [InlineData("Combo", "1-1 & 2+", "862")]
    [InlineData("Combo", "2-2 & 2+", "863")]
    [InlineData("Combo", "BTTS & 3+", "303")]
    [InlineData("HalfGoals", "1-2 prvo & 1-2 vtoro", "51428")]
    [InlineData("HalfGoals", "1-3 prvo & 1-3 vtoro", "51568")]
    [InlineData("HalfGoals", "1-3 prvo & 3+", "55173")]
    public void VerifiedCombinedMarketUsesExactPrice(string market, string pick, string code)
    {
        var offer = new BookmakerMatch("Home", "Away", DateTime.UtcNow,
            new Dictionary<string, double>
            {
                [code] = 1.85, ["1"] = 1.3, ["24"] = 1.9, ["272"] = 1.75,
                ["536"] = 1.1
            });
        Assert.Equal(1.85, MaxbetOddsProvider.GetOdds(offer, market, pick));
    }

    private static TicketCandidate Combo(string pick) => new("Combo", pick, pick, "");

    private static MatchResult Result(int home, int away, int? htHome = null, int? htAway = null) =>
        new()
        {
            HomeGoals = home, AwayGoals = away,
            HalfTimeHomeGoals = htHome, HalfTimeAwayGoals = htAway
        };

    private static Match Fixture(string side, string btts, string goals) => new()
    {
        PredictionSnapshots =
        [
            new() { Source = "Test", PredictedResult = side, Confidence = 0.7, Btts = btts, Goals = goals }
        ]
    };
}
