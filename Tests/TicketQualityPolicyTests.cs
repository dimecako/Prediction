using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class TicketQualityPolicyTests
{
    private static readonly DateOnly Target = new(2030, 1, 10);
    private static readonly DateTime Kickoff = new(2030, 1, 8, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void HistoryUsesOnlyPrematchSnapshotsAndResultsKnownBeforeTargetSkopjeDate()
    {
        var match = Historical();
        match.PredictionSnapshots.Add(new()
        {
            Source = "Source0", PredictedResult = "2", Confidence = .99,
            CapturedAtUtc = Kickoff.AddMinutes(1)
        });
        var prepared = Assert.Single(TicketQualityPolicy.PrepareHistory([match], Target, _ => { }));
        Assert.Equal(3, prepared.PredictionSnapshots.Count);
        Assert.All(prepared.PredictionSnapshots, s => Assert.Equal("1", s.PredictedResult));
        Assert.Equal(4, match.PredictionSnapshots.Count);
        Assert.Equal(1, TicketQualityPolicy.Calibrate([prepared]).Get("1X2|1|70+").Total);
    }

    [Theory]
    [InlineData("2030-01-09T23:00:00Z")]
    [InlineData("2030-01-10T00:00:00Z")]
    public void ResultLearnedAtOrAfterTargetMidnightIsExcluded(string collected)
    {
        var match = Historical();
        match.Result!.CollectedAtUtc = DateTime.Parse(collected).ToUniversalTime();
        Assert.Empty(TicketQualityPolicy.PrepareHistory([match], Target, _ => { }));
    }

    [Fact]
    public void SameDayAndFutureMatchesAreExcludedEvenWhenResultAlreadyPresent()
    {
        var match = Historical();
        match.MatchDate = Target;
        Assert.Empty(TicketQualityPolicy.PrepareHistory([match], Target, _ => { }));
        match.MatchDate = Target.AddDays(1);
        Assert.Empty(TicketQualityPolicy.PrepareHistory([match], Target, _ => { }));
    }

    [Fact]
    public void NoPrematchSnapshotsDoesNotFallBackToPostmatchEvidence()
    {
        var match = Historical();
        foreach (var s in match.PredictionSnapshots) s.CapturedAtUtc = Kickoff;
        Assert.Empty(TicketQualityPolicy.PrepareHistory([match], Target, _ => { }));
    }

    [Fact]
    public void MissingKickoffUsesStartOfFixtureDateNotAssumedEvening()
    {
        var match = Historical();
        match.KickoffUtc = null;
        Assert.Empty(TicketQualityPolicy.PrepareHistory([match], Target, _ => { }));
        foreach (var s in match.PredictionSnapshots) s.CapturedAtUtc = Kickoff.AddDays(-1);
        Assert.Single(TicketQualityPolicy.PrepareHistory([match], Target, _ => { }));
    }

    [Fact]
    public void DuplicateHistoryIsNotMultipliedAndConflictsAreExcluded()
    {
        var a = Historical();
        var b = Historical();
        b.Id = 2;
        Assert.Single(TicketQualityPolicy.PrepareHistory([a, b], Target, _ => { }));
        b.Result!.AwayGoals = 3;
        Assert.Empty(TicketQualityPolicy.PrepareHistory([a, b], Target, _ => { }));
    }

    [Fact]
    public void ConflictingScoreCannotBeHiddenByMissingPrematchSnapshots()
    {
        var a = Historical();
        var b = Historical();
        b.Id = 2;
        b.Result!.AwayGoals = 3;
        b.PredictionSnapshots.Clear();
        Assert.Empty(TicketQualityPolicy.PrepareHistory([a, b], Target, _ => { }));
        b.Result.CollectedAtUtc = Target.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        Assert.Single(TicketQualityPolicy.PrepareHistory([a, b], Target, _ => { }));
    }

    [Fact]
    public void UnknownCollectionTimestampsCannotBecomeAsOfEvidence()
    {
        var match = Historical();
        match.Result!.CollectedAtUtc = default;
        Assert.Empty(TicketQualityPolicy.PrepareHistory([match], Target, _ => { }));
        match = Historical();
        foreach (var snapshot in match.PredictionSnapshots) snapshot.CapturedAtUtc = default;
        Assert.Empty(TicketQualityPolicy.PrepareHistory([match], Target, _ => { }));
    }

    [Theory]
    [InlineData(1.2)]
    [InlineData(-.1)]
    [InlineData(double.NaN)]
    public void InvalidSourceProbabilityIsExcludedFromHistory(double confidence)
    {
        var match = Historical();
        match.PredictionSnapshots.First().Confidence = confidence;
        Assert.Empty(TicketQualityPolicy.PrepareHistory([match], Target, _ => { }));
    }

    [Fact]
    public void TeamGoalsCalibrateTheirWholeEventAndHalfMarketsCannotEnterQualityHistory()
    {
        var match = Historical();
        var history = TicketQualityPolicy.PrepareHistory([match], Target, _ => { });
        var tally = TicketQualityPolicy.Calibrate(history);
        Assert.Equal((1, 1), tally.Get("COMBO|1 & T1 2+|70+"));
        Assert.Equal((0, 0), tally.Get("HALFGOALS|1-3 prvo & 3+"));
        match.Result!.HomeGoals = 1;
        tally = TicketQualityPolicy.Calibrate(history);
        Assert.Equal((1, 0), tally.Get("COMBO|1 & T1 2+|70+"));
    }

    [Fact]
    public void OddsCannotOverrideLowAccuracyEvenWhenExpectedReturnLooksPositive()
    {
        var candidate = new TicketCandidate("Combo", "1 & 2-5 golovi", "", "");
        var score = new ScoredTicketCandidate(candidate, .50, .40, 100, 4);
        Assert.False(TicketQualityPolicy.Passes(score, 1.7));
    }

    [Fact]
    public void CoverageCountsSourcesNotRepeatedSnapshots()
    {
        var match = Historical();
        foreach (var s in match.PredictionSnapshots) s.Source = "Same";
        Assert.False(TicketQualityPolicy.HasCoverage(match, new("1X2", "1", "", "")));
    }

    [Theory]
    [InlineData("BTTS & 3+")]
    [InlineData("1 & 2-5 golovi")]
    public void EveryUsedComponentNeedsThreeSourcesEvenWhenBothHaveFullAgreement(string pick)
    {
        var match = Historical();
        match.PredictionSnapshots.Last().Goals = null;
        Assert.False(TicketQualityPolicy.HasCoverage(match, new("Combo", pick, "", "")));
        Assert.True(TicketQualityPolicy.HasCoverage(match, new("Combo", "1 & T1 2+", "", "")));
        var candidate = TicketMarkets.BuildCandidates(match, includeTeamGoals: true)
            .Single(c => c.Market == "Combo" && c.Pick == pick);
        Assert.Equal(0, TicketQualityPolicy.Calibrate([match]).Get(candidate.Key).Total);
    }

    [Fact]
    public void UnderSignalCanSupportRangeHistoryWithoutEnablingUnderBets()
    {
        var match = Historical();
        foreach (var snapshot in match.PredictionSnapshots) snapshot.Goals = "U2.5";
        Assert.Equal((1, 1), TicketQualityPolicy.Calibrate([match]).Get("SIGNAL|Goals|U2.5|3"));
        Assert.True(TicketQualityPolicy.HasCoverage(match, new("Combo", "1 & 2-5 golovi", "", "")));
        Assert.False(TicketQualityPolicy.IsAllowed(new("Goals", "U2.5", "", "")));
    }

    [Theory]
    [InlineData("HalfGoals", "1-3 prvo & 3+")]
    [InlineData("Combo", "1-1")]
    [InlineData("Combo", "1 & 1+ prvo")]
    public void HistoricalSettlementStillWorksForRetiredTypes(string market, string pick)
    {
        var result = new MatchResult
        {
            HomeGoals = 2, AwayGoals = 1, HalfTimeHomeGoals = 1, HalfTimeAwayGoals = 0
        };
        Assert.True(TicketMarkets.IsCorrect(new(market, pick, "", ""), result));
        Assert.False(TicketQualityPolicy.IsAllowed(new(market, pick, "", "")));
    }

    private static Match Historical()
    {
        var match = TicketSelectorTests.Fixture();
        match.Id = 1;
        match.HomeTeam = "Home United";
        match.AwayTeam = "Away City";
        match.MatchDate = DateOnly.FromDateTime(Kickoff);
        match.KickoffUtc = Kickoff;
        match.Result = new()
        {
            HomeGoals = 2, AwayGoals = 0, TotalGoals = 2, Result = "1",
            CollectedAtUtc = Kickoff.AddHours(3)
        };
        foreach (var s in match.PredictionSnapshots) s.CapturedAtUtc = Kickoff.AddHours(-2);
        return match;
    }
}
