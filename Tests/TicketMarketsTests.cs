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
        Assert.Equal(10, candidates.Count);
        Assert.Equal(new[]
        {
            $"fix {side}", $"{side} & 1+ prvo", $"{side} & 1+ prvo & 1+ vtoro",
            $"{side} & 2-5 golovi", $"{side} & 2-6 golovi",
            $"{side}-{side}", $"{side}-{side} & 2+", "BTTS", "BTTS & 3+", "3+"
        }, candidates.Select(x => x.DisplayPick));
        Assert.DoesNotContain(candidates, x => x.Market == "DC");
        Assert.Equal(candidates.Count, candidates.Select(x => x.Key).Distinct().Count());
    }

    [Fact]
    public void DrawNoBttsAndUnderCannotEnterTicket()
    {
        Assert.Empty(TicketMarkets.BuildCandidates(Fixture("X", "NO", "U2.5")));
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
