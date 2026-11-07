using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class DailyLeagueNoSelectionTests
{
    private static readonly DateOnly Date = new(2030, 1, 1);
    private static readonly DateTime Now = new(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("UEFA Champions League", LeagueEligibility.IntentionallyExcluded)]
    public void LeagueFilterCanProduceValidNoSelectionWithoutInventingTicket(
        string? league, LeagueEligibility expected)
    {
        var match = Fixture(1);
        var selection = Build([match], [Offer(match, league)], Date, Now, 15, true);
        Assert.Empty(selection.Picks);
        Assert.True(selection.NoSelection);
        Assert.Equal(1, selection.Eligible);
        Assert.Equal(expected, Assert.Single(selection.LeagueDiagnoses).Eligibility);
    }

    [Fact]
    public void LeagueModeFallsBackToQualifiedUnrecognizedLeagueWhenPriorityLeagueHasNoPick()
    {
        var match = Fixture(1);
        var selection = Build([match], [Offer(match, "New Competition")], Date, Now, 15, true);

        Assert.Single(selection.Picks);
        Assert.Equal(1, selection.Picks[0].MatchId);
        Assert.False(selection.NoSelection);
        Assert.Equal(LeagueEligibility.Unrecognized, Assert.Single(selection.LeagueDiagnoses).Eligibility);
    }

    [Fact]
    public void QualifiedExcludedCandidateDoesNotRequireSourceDataForUnrelatedFixtures()
    {
        var valid = Fixture(1);
        var missing = Fixture(2);
        missing.PredictionSnapshots.Clear();
        var selection = Build([valid, missing],
            [Offer(valid, "UEFA Youth League"), Offer(missing, "ENGLAND 1")], Date, Now, 15, true);
        Assert.Empty(selection.Picks);
        Assert.True(selection.NoSelection);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingLeagueMetadataCannotBeMaskedByAnExcludedCandidate(string? league)
    {
        var excluded = Fixture(1);
        var missing = Fixture(2);
        var selection = Build([excluded, missing],
            [Offer(excluded, "FA Cup"), Offer(missing, league)], Date, Now, 15, true);
        Assert.Empty(selection.Picks);
        Assert.False(selection.NoSelection);
        Assert.Contains(selection.LeagueDiagnoses, d => d.Eligibility == LeagueEligibility.Missing);
    }

    [Fact]
    public void MissingBookmakerDataAndAbsentQualifiedCandidatesAreNotNoSelectionSuccess()
    {
        var match = Fixture(1);
        Assert.False(Build([match], [], Date, Now, 15, true).NoSelection);
        Assert.False(Build([], [Offer(match, "FA Cup")], Date, Now, 15, true).NoSelection);
        var unpriced = Offer(match, "FA Cup") with { Odds = new Dictionary<string, double>() };
        Assert.False(Build([match], [unpriced], Date, Now, 15, true).NoSelection);
    }

    [Fact]
    public void PriorityAndFallbackCandidatesCanProduceTicketsAndExcludedCupReportsNoSelection()
    {
        var match = Fixture(1);
        var eligible = Build([match], [Offer(match, "ENGLAND 1")], Date, Now, 15, true);
        Assert.Single(eligible.Picks);
        Assert.False(eligible.NoSelection);
        var fallback = Build([match], [Offer(match, "New Competition")], Date, Now, 15, false);
        Assert.Single(fallback.Picks);
        Assert.False(fallback.NoSelection);
        var cup = Build([match], [Offer(match, "FA Cup")], Date, Now, 15, false);
        Assert.Empty(cup.Picks);
        Assert.True(cup.NoSelection);
    }

    [Fact]
    public void ValidPricedSourceFixtureWithNoAllowedAlternativeIsNoSelection()
    {
        var match = Fixture(1);
        var offer = Offer(match, "ENGLAND 1") with
        {
            Odds = new Dictionary<string, double> { ["1"] = 1.4 }
        };
        var selection = Build([match], [offer], Date, Now, 15, true);
        Assert.Empty(selection.Picks);
        Assert.Equal(0, selection.Eligible);
        Assert.True(selection.NoSelection);
        Assert.True(Build([match], [offer], Date, Now, 15, false).NoSelection);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1.0)]
    public void QualifiedExcludedCandidateDoesNotRequireValidOddsForUnrelatedFixtures(double invalidOdds)
    {
        var excluded = Fixture(1);
        var invalid = Fixture(2);
        var badOffer = Offer(invalid, "ENGLAND 1") with
        {
            Odds = new Dictionary<string, double> { ["1"] = invalidOdds }
        };
        var selection = Build([excluded, invalid],
            [Offer(excluded, "FA Cup"), badOffer], Date, Now, 15, true);
        Assert.Empty(selection.Picks);
        Assert.True(selection.NoSelection);
    }

    [Fact]
    public void UnrelatedUnpairedFixtureDoesNotInvalidateQualifiedExcludedCandidate()
    {
        var excluded = Fixture(1);
        var unmatched = Fixture(2);
        var selection = Build([excluded, unmatched],
            [Offer(excluded, "FA Cup")], Date, Now, 15, true);
        Assert.Empty(selection.Picks);
        Assert.True(selection.NoSelection);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1.0)]
    public void EmptyThresholdOutcomeStillFailsForInvalidMatchedBaseOdds(double invalidOdds)
    {
        var valid = Fixture(1);
        var invalid = Fixture(2);
        var thresholdOffer = Offer(valid, "ENGLAND 1") with
        {
            Odds = new Dictionary<string, double> { ["1"] = 1.4 }
        };
        var invalidOffer = Offer(invalid, "ITALY 1") with
        {
            Odds = new Dictionary<string, double> { ["1"] = invalidOdds }
        };
        var selection = Build([valid, invalid],
            [thresholdOffer, invalidOffer], Date, Now, 15, true);
        Assert.Empty(selection.Picks);
        Assert.Equal(0, selection.Eligible);
        Assert.False(selection.NoSelection);
    }

    private static Match Fixture(long id) => new()
    {
        Id = id, MatchDate = Date, HomeTeam = $"Home {id}", AwayTeam = $"Away {id}",
        PredictionSnapshots = Enumerable.Range(1, 3).Select(i => new PredictionSnapshot
        {
            Source = $"Source{i}", PredictedResult = "1", Confidence = .75, CapturedAtUtc = Now
        }).ToList()
    };

    private static DailyTicketSelection Build(IReadOnlyList<Match> matches,
        IReadOnlyList<BookmakerMatch> offers, DateOnly date, DateTime now, int count, bool leagueOnly) =>
        DailyTicketSelector.Build(matches, offers, date, now, count, leagueOnly,
            calibration: matches.Count == 0 ? new Tally() : TicketSelectorTests.Calibrate(matches[0]));

    private static BookmakerMatch Offer(Match match, string? league) =>
        new(match.HomeTeam, match.AwayTeam, Now.AddHours(5),
            new Dictionary<string, double> { ["1"] = 1.8 }, league);
}
