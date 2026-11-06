using Prediction.Entities;
using Prediction.Services;
using Xunit;

namespace Prediction.Tests;

public sealed class DailyTicketSelectorTests
{
    private static readonly DateOnly Date = new(2030, 1, 1);
    private static readonly DateTime Now = new(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DailyAndFixedDoNotSelectWithoutExactHistory()
    {
        var match = Fixture(1);
        Assert.Null(DailyTicketSelector.Select(match, Offer(match)).Pick);
        Assert.Empty(DailyTicketSelector.BuildFixed([match], [Offer(match)], Date, Now, 15).Picks);
    }

    [Fact]
    public void SuccessfullyLoadedButEmptyHistoryIsQualityRejectionNotTechnicalFailure()
    {
        var match = Fixture(1);
        var offer = Offer(match);
        var emptyHistory = new Tally();
        var daily = DailyTicketSelector.Build([match], [offer], Date, Now, 15, false,
            calibration: emptyHistory);
        var fixedTicket = DailyTicketSelector.BuildFixed([match], [offer], Date, Now, 15,
            calibration: emptyHistory);
        Assert.Empty(daily.Picks);
        Assert.True(daily.NoSelection);
        Assert.Empty(fixedTicket.Picks);
        Assert.True(fixedTicket.NoSelection);
    }

    [Theory]
    [InlineData(1.2)]
    [InlineData(-.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSourceProbabilityCannotEnterDailyOrFixedTickets(double confidence)
    {
        var match = Fixture(1, confidence);
        var offer = Offer(match);
        Assert.Null(Select(match, offer).Pick);
        var daily = Build([match], [offer], 15, false);
        var fixedTicket = DailyTicketSelector.BuildFixed([match], [offer], Date, Now, 15,
            calibration: TicketSelectorTests.Calibrate(match));
        Assert.Empty(daily.Picks);
        Assert.False(daily.NoSelection);
        Assert.Empty(fixedTicket.Picks);
        Assert.False(fixedTicket.NoSelection);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ValidDrawOrTiedVotesAreAbstentionNotTechnicalFailure(bool draw)
    {
        var match = Fixture(1);
        int i = 0;
        foreach (var snapshot in match.PredictionSnapshots)
            snapshot.PredictedResult = draw ? "X" : new[] { "1", "X", "2" }[i++];
        var offer = Offer(match) with
        {
            Odds = new Dictionary<string, double> { ["1"] = 1.8, ["2"] = 3.2, ["3"] = 2.4 }
        };
        var daily = Build([match], [offer], 15, false);
        var fixedTicket = DailyTicketSelector.BuildFixed([match], [offer], Date, Now, 15,
            calibration: new Tally());
        Assert.Empty(daily.Picks);
        Assert.True(daily.NoSelection);
        Assert.Empty(fixedTicket.Picks);
        Assert.True(fixedTicket.NoSelection);
    }

    [Fact]
    public void QualifiedDailyFixIncludesVersionAndHistoryEvidence()
    {
        var match = Fixture(1);
        var decision = Select(match, Offer(match));
        Assert.Equal("1", decision.Pick!.Candidate.Pick);
        Assert.Equal(.8, decision.Pick.HistoricalAccuracy);
        Assert.Equal(100, decision.Pick.HistoricalSample);
        Assert.Contains("Policy=exact-market-v1", decision.Pick.Evidence);
        Assert.Null(match.Result);
    }

    [Fact]
    public void LowOddsDoNotAutomaticallyBecomeHalfTimeCombo()
    {
        var match = Fixture(1);
        var offer = Offer(match, baseOdds: 1.3, extraOdds: new Dictionary<string, double> { ["7"] = 1.9 });
        Assert.Null(Select(match, offer).Pick);
    }

    [Theory]
    [InlineData(1.69, false)]
    [InlineData(1.7, true)]
    public void ExactQualifiedAlternativesRequireValidMinimumOdds(double odds, bool expected)
    {
        var match = Fixture(1);
        var decision = Select(match, Offer(match, baseOdds: 1.4,
            extraOdds: new Dictionary<string, double> { ["50723"] = odds }));
        Assert.Equal(expected, decision.Pick != null);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidAlternativePriceIsSurfacedAsDataError(double odds)
    {
        var match = Fixture(1);
        Assert.Throws<InvalidOperationException>(() => Select(match, Offer(match, baseOdds: 1.4,
            extraOdds: new Dictionary<string, double> { ["50723"] = odds })));
    }

    [Fact]
    public void HighOddsCanUseOverWithExactHistory()
    {
        var match = Fixture(1);
        var decision = Select(match, Offer(match, baseOdds: 2.2,
            extraOdds: new Dictionary<string, double> { ["24"] = 1.8 }));
        Assert.Equal("O2.5", decision.Pick!.Candidate.Pick);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothModesReturnFifteenOnlyWhenFifteenPass(bool leagueOnly)
    {
        var matches = Enumerable.Range(1, 20).Select(i => Fixture(i)).ToList();
        var result = Build(matches, matches.Select(m => Offer(m)).ToList(), 15, leagueOnly);
        Assert.Equal(15, result.Picks.Count);
        Assert.Equal(15, result.Picks.Select(p => p.MatchId).Distinct().Count());
    }

    [Fact]
    public void RankingUsesExactHistoryNotHigherSourceProbability()
    {
        var strongSource = Fixture(1, .9);
        var strongHistory = Fixture(2, .6);
        var tally = TicketSelectorTests.Calibrate(strongSource, correct: 70);
        foreach (var candidate in TicketMarkets.BuildCalibrationCandidates(strongHistory, true).DistinctBy(c => c.Key))
            for (int i = 0; i < 100; i++) tally.Add(candidate.Key, i < 90);
        var result = DailyTicketSelector.Build([strongSource, strongHistory],
            [Offer(strongSource), Offer(strongHistory)], Date, Now, 2, false, calibration: tally);
        Assert.Equal([2L, 1L], result.Picks.Select(p => p.MatchId));
    }

    [Fact]
    public void ExcludesWrongDatesStartedAndUnmatchedFixtures()
    {
        var matches = Enumerable.Range(1, 4).Select(i => Fixture(i)).ToList();
        matches[1].MatchDate = Date.AddDays(-1);
        var result = Build(matches, [Offer(matches[0]), Offer(matches[1]),
            Offer(matches[2]) with { KickoffUtc = Now }], 15, false);
        Assert.Equal(1, Assert.Single(result.Picks).MatchId);
        Assert.Contains(result.Rejections, r => r.Contains("zapocnal"));
        Assert.Contains(result.Rejections, r => r.Contains("Nema Maxbet"));
    }

    [Fact]
    public void DeduplicatesMatchingOffersAndKeepsBestCoveredRecord()
    {
        var first = Fixture(1);
        var duplicate = Fixture(2);
        duplicate.HomeTeam = first.HomeTeam;
        duplicate.AwayTeam = first.AwayTeam;
        duplicate.PredictionSnapshots.Add(new() { Source = "Fourth", PredictedResult = "1", Confidence = .75 });
        var result = Build([first, duplicate], [Offer(first)], 15, false);
        Assert.Equal(1, result.Duplicates);
        Assert.Equal(2, Assert.Single(result.Picks).MatchId);
    }

    [Fact]
    public void FixedAiChecksAllQualifiedCandidatesBeforeTakingFifteen()
    {
        var matches = Enumerable.Range(1, 20).Select(i => Fixture(i)).ToList();
        var evaluated = new List<long>();
        var result = DailyTicketSelector.BuildFixed(matches, matches.Select(m => Offer(m)).ToList(),
            Date, Now, 15, (match, _) =>
            {
                evaluated.Add(match.Id);
                return match.Id <= 3 ? .54 : .65;
            }, TicketSelectorTests.Calibrate(matches[0]));
        Assert.Equal(20, evaluated.Count);
        Assert.Equal(17, result.Eligible);
        Assert.Equal(15, result.Picks.Count);
        Assert.All(result.Picks, p => Assert.True(p.MatchId > 3));
        Assert.Equal(15, TicketLedger.CreateFixedAuto(Date, result.Picks, Now).Selections.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1.01)]
    public void FixedAiRejectsUnknownOrInvalidProbabilities(double? probability)
    {
        var match = Fixture(1);
        var result = DailyTicketSelector.BuildFixed([match], [Offer(match)], Date, Now, 15,
            (_, _) => probability, TicketSelectorTests.Calibrate(match));
        Assert.Empty(result.Picks);
        Assert.Contains("AI confidence", Assert.Single(result.Rejections));
    }

    [Fact]
    public void NoForcedFillingAndNoHistoryFallback()
    {
        var matches = new[] { Fixture(1), Fixture(2, .6) };
        var result = Build(matches, matches.Select(m => Offer(m)).ToList(), 15, false,
            TicketSelectorTests.Calibrate(matches[0]));
        Assert.Equal(1, Assert.Single(result.Picks).MatchId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void MoreThanFifteenOrZeroIsRejected(int count)
    {
        Assert.Throws<ArgumentException>(() => Build([], [], count, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DailyTicketSelector.BuildFixed([], [], Date, Now, count));
    }

    private static DailyTicketDecision Select(Match match, BookmakerMatch offer) =>
        DailyTicketSelector.Select(match, offer, calibration: TicketSelectorTests.Calibrate(match));

    private static DailyTicketSelection Build(IReadOnlyList<Match> matches,
        IReadOnlyList<BookmakerMatch> offers, int count, bool leagueOnly, Tally? tally = null) =>
        DailyTicketSelector.Build(matches, offers, Date, Now, count, leagueOnly,
            calibration: tally ?? (matches.Count > 0 ? TicketSelectorTests.Calibrate(matches[0]) : new()));

    private static Match Fixture(int id, double confidence = .75)
    {
        var match = TicketSelectorTests.Fixture();
        match.Id = id;
        match.MatchDate = Date;
        match.HomeTeam = $"Home{id:D2}";
        match.AwayTeam = $"Away{id:D2}";
        foreach (var s in match.PredictionSnapshots) { s.Confidence = confidence; s.CapturedAtUtc = Now; }
        return match;
    }

    private static BookmakerMatch Offer(Match match, double baseOdds = 1.8,
        IReadOnlyDictionary<string, double>? extraOdds = null)
    {
        var odds = new Dictionary<string, double> { ["1"] = baseOdds, ["3"] = baseOdds };
        if (extraOdds != null) foreach (var (key, value) in extraOdds) odds[key] = value;
        return new(match.HomeTeam, match.AwayTeam, Now.AddHours(5), odds, "ITALY 1");
    }
}
